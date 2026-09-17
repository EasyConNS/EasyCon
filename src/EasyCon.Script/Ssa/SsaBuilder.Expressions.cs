using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Script.Ssa;

/// <summary>
/// 将 Bound IR 转换为 SSA IR。
/// 遍历 Bound 的结构化语句，生成扁平的 SsaBlock + SsaValue。
/// </summary>
sealed partial class SsaBuilder
{
    private static void SetConstPayload(SsaValue v, object value)
    {
        switch (v.Op)
        {
            case SsaOp.ConstBool: v.Const.SetBool((bool)value); break;
            case SsaOp.ConstByte: v.Const.SetByte((byte)value); break;
            case SsaOp.ConstInt: v.Const.SetInt(value is int i ? i : Convert.ToInt32(value)); break;
            case SsaOp.ConstUInt: v.Const.SetUInt((uint)value); break;
            case SsaOp.ConstUInt64: v.Const.SetUInt64((ulong)value); break;
            case SsaOp.ConstDouble: v.Const.SetDouble((double)value); break;
            case SsaOp.ConstString: v.ConstString = (string)value; break;
            case SsaOp.ConstPtr: v.Const.SetPtr((long)value); break;
        }
    }

    private SsaValue EmitVariable(BoundVariableExpression var)
    {
        // 常量内联：只读变量有编译期值时直接 emit 常量
        if (var.Variable.IsReadOnly && var.Variable.Value != null)
            return EmitConst(var.Type, var.Variable.Value);

        // 全局变量：保留 memory-SSA（每次 LoadGlobal），不进值 SSA
        // （全局可被 extern 调用修改，memory-SSA 是正确选择）
        if (var.Variable is GlobalVariableSymbol)
        {
            var load = NewValue(SsaOp.LoadGlobal, var.Type, aux: var.Variable);
            AddInst(load);
            return load;
        }

        // 局部变量：走 Braun ReadVariable。命中定义即返回；
        // 合并点自动插入 phi；未定义首次读取会得到占位 phi（运行时为默认值）。
        return _vars.ReadVariable(var.Variable, var.Type, _currentBlock);
    }

    private SsaValue EmitBinary(BoundBinaryExpression bin)
    {
        // 短路求值：拆为多块 + Phi
        if (bin.Op.Kind == BoundBinaryOperatorKind.LogicalAnd)
        {
            var left = EmitExpression(bin.Left);
            return EmitShortCircuitAnd(left, bin.Right);
        }
        if (bin.Op.Kind == BoundBinaryOperatorKind.LogicalOr)
        {
            var left = EmitExpression(bin.Left);
            return EmitShortCircuitOr(left, bin.Right);
        }

        var l = EmitExpression(bin.Left);
        var r = EmitExpression(bin.Right);

        // 字符串/数组拼接：+ 或 & 遇到 string/array 类型 → Concat（跳过 MapBinaryOp）
        if (bin.Op.Kind is BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.BitwiseAnd)
        {
            if (bin.Op.LeftType.Equals(ScriptType.String) || bin.Op.LeftType is ArrayType)
                return EmitAndAdd(SsaOp.Concat, bin.Op.Type, l, r);
        }

        var op = MapBinaryOp(bin.Op);
        var result = NewValue(op, bin.Op.Type, l, r);
        AddInst(result);
        return result;
    }

    private SsaValue EmitShortCircuitAnd(SsaValue left, BoundExpr rightExpr)
    {
        var evalRight = CreateBlock();
        var endBlock = CreateBlock();

        // 当前块：left → evalRight(left=true) 或 endBlock(left=false)
        _currentBlock.BranchCondition = left;
        left.Uses++;
        _currentBlock.TrueSuccessor = evalRight;
        _currentBlock.FalseSuccessor = endBlock;
        evalRight.AddPredecessor(_currentBlock);
        endBlock.AddPredecessor(_currentBlock);

        // left=false 路径的值：ConstBool(false)
        var falseVal = NewValue(SsaOp.ConstBool, ScriptType.Bool);
        falseVal.Const.SetBool(false);
        falseVal.Block = _currentBlock;
        AddInst(falseVal);

        // evalRight: 求值 right
        // evalRight（条件分支目标，清空 defs）
        SwitchToBlock(evalRight, fromConditionalBranch: true);
        var rightVal = EmitExpression(rightExpr);
        _currentBlock.JumpTarget = endBlock;
        endBlock.AddPredecessor(_currentBlock);

        // endBlock: Phi
        SwitchToBlock(endBlock);
        var phi = NewValue(SsaOp.Phi, ScriptType.Bool);
        phi.ExtraArgs = new List<SsaValue> { falseVal, rightVal };
        AddExtraUses(phi.ExtraArgs);
        endBlock.Phis.Add(phi);
        return phi;
    }

    private SsaValue EmitShortCircuitOr(SsaValue left, BoundExpr rightExpr)
    {
        var evalRight = CreateBlock();
        var endBlock = CreateBlock();

        _currentBlock.BranchCondition = left;
        left.Uses++;
        _currentBlock.TrueSuccessor = endBlock;
        _currentBlock.FalseSuccessor = evalRight;
        endBlock.AddPredecessor(_currentBlock);
        evalRight.AddPredecessor(_currentBlock);

        var trueVal = NewValue(SsaOp.ConstBool, ScriptType.Bool);
        trueVal.Const.SetBool(true);
        trueVal.Block = _currentBlock;
        AddInst(trueVal);

        // evalRight（条件分支目标，清空 defs）
        SwitchToBlock(evalRight, fromConditionalBranch: true);
        var rightVal = EmitExpression(rightExpr);
        _currentBlock.JumpTarget = endBlock;
        endBlock.AddPredecessor(_currentBlock);

        SwitchToBlock(endBlock);
        var phi = NewValue(SsaOp.Phi, ScriptType.Bool);
        phi.ExtraArgs = new List<SsaValue> { trueVal, rightVal };
        AddExtraUses(phi.ExtraArgs);
        endBlock.Phis.Add(phi);
        return phi;
    }

    private SsaValue EmitUnary(BoundUnaryExpression un)
    {
        var operand = EmitExpression(un.Operand);
        return un.Op.Kind switch
        {
            BoundUnaryOperatorKind.LogicNot =>
                EmitAndAdd(SsaOp.LogicNot, ScriptType.Bool, operand),
            BoundUnaryOperatorKind.BitwiseNot =>
                EmitAndAdd(SsaOp.NotInt, un.Op.Type, operand),
            BoundUnaryOperatorKind.Subtraction =>
                EmitNegate(un.Op.Type, operand),
            _ => throw new InvalidOperationException($"未知一元运算: {un.Op.Kind}")
        };
    }

    private SsaValue EmitNegate(ScriptType type, SsaValue operand)
    {
        var zeroOp = type.Equals(ScriptType.UInt) ? SsaOp.ConstUInt
            : type.Equals(ScriptType.UInt64) ? SsaOp.ConstUInt64
            : type.Equals(ScriptType.Byte) ? SsaOp.ConstByte
            : SsaOp.ConstInt;
        var zero = NewValue(zeroOp, type);
        AddInst(zero);  // 常量必须加入指令列表
        var op = type switch
        {
            _ when type.Equals(ScriptType.UInt) => SsaOp.SubUInt,
            _ when type.Equals(ScriptType.UInt64) => SsaOp.SubUInt64,
            _ => SsaOp.SubInt
        };
        return EmitAndAdd(op, type, zero, operand);
    }

    private SsaValue EmitConversion(BoundConversionExpression conv)
    {
        var inner = EmitExpression(conv.Expression);
        // 同类型无需转换
        if (inner.Type.Equals(conv.Type))
            return inner;

        var convOp = MapConversion(inner.Type, conv.Type);
        if (convOp == SsaOp.Nop)
            return inner;
        return EmitAndAdd(convOp, conv.Type, inner);
    }

    private SsaValue EmitCall(BoundCallExpression call)
    {
        // 编译器内联伪函数：直接生成对应 SSA op
        if (BuiltinFunctions.IsIntrinsic(call.Function))
            return EmitIntrinsic(call);

        // 收集参数
        var args = new List<SsaValue>();
        foreach (var arg in call.Arguments)
            args.Add(EmitExpression(arg));

        // 第一个参数放入 Arg0（外部函数走 Call，其余走 StaticCall）
        var firstArg = args.Count > 0 ? args[0] : null;
        var op = _externFunctions.Contains(call.Function) ? SsaOp.Call : SsaOp.StaticCall;
        var result = NewValue(op, call.Type, firstArg, aux: call.Function);

        // 多余参数存入 ExtraArgs
        if (args.Count > 1)
        {
            result.ExtraArgs = new List<SsaValue>();
            for (int i = 1; i < args.Count; i++)
                result.ExtraArgs.Add(args[i]);
            AddExtraUses(result.ExtraArgs);
        }

        AddInst(result);
        return result;
    }

    private SsaValue EmitIndex(BoundIndexVariableExpression idx)
    {
        // 检测 base 是字段访问且字段为数组类型 → LoadFieldIndex
        if (idx.BaseExpression is BoundFieldAccessExpression fa && fa.Field.FieldType is ArrayType)
        {
            var target = EmitExpression(fa.Target);
            var idxVal = EmitExpression(idx.Index);
            var elemType = ((ArrayType)fa.Field.FieldType).ElementType;
            return EmitAndAdd(SsaOp.LoadFieldIndex, elemType, target, idxVal, aux: fa.Field);
        }

        var baseVal = EmitExpression(idx.BaseExpression);
        var indexVal = EmitExpression(idx.Index);
        return EmitAndAdd(SsaOp.LoadIndex, idx.Type, baseVal, indexVal);
    }

    private SsaValue EmitSlice(BoundSliceExpression slice)
    {
        var baseVal = EmitExpression(slice.BaseExpression);
        var startVal = EmitExpression(slice.Start);
        var endVal = EmitExpression(slice.End);
        var result = EmitAndAdd(SsaOp.Slice, slice.Type, baseVal, startVal);
        // End 存入 ExtraArgs
        result.ExtraArgs = new List<SsaValue> { endVal };
        AddExtraUses(result.ExtraArgs);
        return result;
    }

    private SsaValue EmitArrayInit(BoundIndexDeclxpression decl)
    {
        var result = NewValue(SsaOp.ArrayInit, decl.Type, aux: null);
        if (decl.Items.Length > 0)
        {
            result.Arg0 = EmitExpression(decl.Items[0]);
            result.Arg0.Uses++;
        }
        if (decl.Items.Length > 1)
        {
            result.ExtraArgs = new List<SsaValue>();
            for (int i = 1; i < decl.Items.Length; i++)
            {
                var itemVal = EmitExpression(decl.Items[i]);
                result.ExtraArgs.Add(itemVal);
            }
            AddExtraUses(result.ExtraArgs);
        }
        AddInst(result);
        return result;
    }

    private SsaValue EmitStructInit(BoundStructInitExpression si)
    {
        var v = NewValue(SsaOp.StructInit, si.Type, aux: si.Definition);
        AddInst(v);
        return v;
    }

    private SsaValue EmitFieldAccess(BoundFieldAccessExpression fa)
    {
        var target = EmitExpression(fa.Target);
        return EmitAndAdd(SsaOp.LoadField, fa.Type, target, aux: fa.Field);
    }

    private SsaValue EmitRuntimeValue(BoundRuntimeValueExpression rv)
    {
        // RuntimeValue 的 name 存在 Aux（特殊用法：将 string 名字包装为 Symbol）
        return EmitAndAdd(SsaOp.RuntimeValue, rv.Type, aux: new RuntimeValueNameSymbol(rv.Name));
    }

    private SsaValue EmitImageLabel(BoundImageLabelExpression il)
    {
        return EmitAndAdd(SsaOp.ImageLabel, il.Type, aux: new RuntimeValueNameSymbol(il.Name));
    }

    // ============ 辅助方法 ============

    private SsaValue EmitAndAdd(SsaOp op, ScriptType type,
        SsaValue? arg0 = null, SsaValue? arg1 = null,
        object? aux = null)
    {
        var v = NewValue(op, type, arg0, arg1, aux);
        AddInst(v);
        return v;
    }

    // ============ 操作码映射 ============
}