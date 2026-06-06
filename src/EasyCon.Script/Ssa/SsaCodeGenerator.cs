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
sealed class SsaCodeGenerator
{
    private readonly FunctionSymbol _function;
    private readonly ImmutableHashSet<FunctionSymbol> _externFunctions;
    private readonly Dictionary<VariableSymbol, SsaValue> _defs = new();
    private readonly Dictionary<BoundLabel, SsaBlock> _labelBlocks = new();
    private readonly List<SsaBlock> _blocks = new();
    private SsaBlock _currentBlock;
    private int _nextValueId;
    private int _nextBlockId;
    private readonly AstNode _emptySyntax;

    public int NextValueId => _nextValueId;
    public int NextBlockId => _nextBlockId;

    public SsaCodeGenerator(FunctionSymbol function, AstNode emptySyntax,
        int startValueId = 0, int startBlockId = 0,
        ImmutableHashSet<FunctionSymbol>? externFunctions = null)
    {
        _function = function;
        _externFunctions = externFunctions ?? ImmutableHashSet<FunctionSymbol>.Empty;
        _emptySyntax = emptySyntax;
        _nextValueId = startValueId;
        _nextBlockId = startBlockId;
        SwitchToBlock(CreateBlock());
    }

    // ============ 工厂方法 ============

    private SsaBlock CreateBlock()
    {
        var block = new SsaBlock(_nextBlockId++);
        _blocks.Add(block);
        return block;
    }

    private SsaValue NewValue(SsaOp op, ScriptType type,
        SsaValue? arg0 = null, SsaValue? arg1 = null,
        object? aux = null)
    {
        var v = new SsaValue(_nextValueId++, op, type)
        {
            Arg0 = arg0,
            Arg1 = arg1,
            Aux = aux,
            Block = _currentBlock
        };
        if (arg0 != null) arg0.Uses++;
        if (arg1 != null) arg1.Uses++;
        return v;
    }

    private static void AddExtraUses(List<SsaValue> extras)
    {
        foreach (var v in extras) v.Uses++;
    }

    private void AddInst(SsaValue val)
    {
        _currentBlock.Instructions.Add(val);
    }

    private SsaBlock GetOrCreateLabelBlock(BoundLabel label)
    {
        if (!_labelBlocks.TryGetValue(label, out var block))
        {
            block = CreateBlock();
            _labelBlocks[label] = block;
        }
        return block;
    }

    private void SwitchToBlock(SsaBlock block, bool fromConditionalBranch = false)
    {
        _currentBlock = block;
        // 多前驱块（如循环头从回边进入、IF-ELSE 合流）必须清空 defs 强制重新加载。
        // 条件分支目标（如循环体、IF-THEN）也必须清空 defs，因为 defs 可能包含
        // 来自前一次迭代或来自不同控制流路径的 stale 值。
        // 仅在无条件跳转到单前驱块时保留 defs（线性序列优化）。
        if (block.Predecessors.Count > 1 || fromConditionalBranch)
            _defs.Clear();
    }

    private static bool NeedsTerminator(SsaBlock block)
        => !block.IsTerminated;

    // ============ 入口 ============

    public SsaFunction Generate(BoundBlockStatement body)
    {
        var func = new SsaFunction(_function);

        // 绑定参数为 StoreLocal，建立 _defs 映射
        foreach (var param in _function.Parameters)
        {
            // 参数值由调用者通过帧写入，此处发出 LoadLocal 获取
            var load = NewValue(SsaOp.LoadLocal, param.Type, aux: param);
            AddInst(load);
            _defs[param] = load;
        }

        // 生成语句
        EmitStatements(body.Statements);

        // 为所有未终止的块添加 Return（包括 IF 内 Return 导致的空 endLabel 块）
        foreach (var block in _blocks)
        {
            if (!block.IsTerminated)
            {
                var ret = NewValue(SsaOp.Return, ScriptType.Void);
                ret.Block = block;
                block.Instructions.Add(ret);
                block.IsReturn = true;
            }
        }

        func.Blocks.AddRange(_blocks);
        return func;
    }

    // ============ 语句发射 ============

    private void EmitStatements(ImmutableArray<BoundStmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            EmitStatement(stmt);
            if (_currentBlock.IsTerminated)
                break;
        }
    }

    private void EmitStatement(BoundStmt stmt)
    {
        switch (stmt)
        {
            case BoundVariableDeclaration decl:
                EmitVarDecl(decl.Variable, decl.Initializer);
                break;
            case BoundExprStatement exprStmt:
                EmitExpression(exprStmt.Expression);
                break;
            case BoundReturnStatement ret:
                EmitReturn(ret);
                break;
            case BoundIfStatement ifStmt:
                EmitIf(ifStmt);
                break;
            case BoundWhileStatement whileStmt:
                EmitWhile(whileStmt);
                break;
            case BoundForStatement forStmt:
                EmitFor(forStmt);
                break;
            case BoundUntilStatement untilStmt:
                EmitUntil(untilStmt);
                break;
            case BoundGotoStatement gs:
                EmitGoto(gs.Label);
                break;
            case BoundConditionalGotoStatement cgs:
                EmitCondGoto(cgs);
                break;
            case BoundLabelStatement ls:
                EmitLabel(ls.Label);
                break;
            case BoundKeyActStatement key:
                EmitKeyAction(key);
                break;
            case BoundStickActStatement stick:
                EmitStickAction(stick);
                break;
            case BoundFieldAssignStatement fa:
                EmitFieldAssign(fa);
                break;
            case BoundIndexAssignStatement ia:
                EmitIndexAssign(ia);
                break;
            case BoundNop:
                break;
            case BoundBlockStatement block:
                EmitStatements(block.Statements);
                break;
        }
    }

    private void EmitVarDecl(VariableSymbol variable, BoundExpr initializer)
    {
        var initVal = EmitExpression(initializer);

        if (variable is GlobalVariableSymbol)
        {
            var store = NewValue(SsaOp.StoreGlobal, variable.Type, initVal, aux: variable);
            AddInst(store);
            // 全局变量不缓存：后续引用走 LoadGlobal 读取全局存储的当前值
        }
        else
        {
            var store = NewValue(SsaOp.StoreLocal, variable.Type, initVal, aux: variable);
            AddInst(store);
            _defs[variable] = initVal;
        }
    }

    private void EmitReturn(BoundReturnStatement ret)
    {
        SsaValue? retVal = null;
        if (ret.Expression != null)
            retVal = EmitExpression(ret.Expression);

        var returnVal = NewValue(SsaOp.Return, ScriptType.Void, retVal);
        AddInst(returnVal);
        _currentBlock.IsReturn = true;
    }

    private void EmitIf(BoundIfStatement ifStmt)
    {
        var cond = EmitExpression(ifStmt.Condition);

        var thenBlock = CreateBlock();
        var endBlock = CreateBlock();
        SsaBlock? elseBlock = ifStmt.ElseBody != null || !ifStmt.ElseIfs.IsDefaultOrEmpty
            ? CreateBlock() : null;

        // 当前块发出条件分支
        _currentBlock.BranchCondition = cond;
        cond.Uses++;
        _currentBlock.TrueSuccessor = thenBlock;
        _currentBlock.FalseSuccessor = elseBlock ?? endBlock;
        thenBlock.Predecessors.Add(_currentBlock);
        _currentBlock.FalseSuccessor.Predecessors.Add(_currentBlock);

        // Then 分支（条件分支目标，清空 defs）
        SwitchToBlock(thenBlock, fromConditionalBranch: true);
        EmitStatements(ifStmt.Body.Statements);
        if (NeedsTerminator(_currentBlock))
        {
            _currentBlock.JumpTarget = endBlock;
            endBlock.Predecessors.Add(_currentBlock);
        }

        // ElseIfs + Else
        if (elseBlock != null)
        {
            SwitchToBlock(elseBlock, fromConditionalBranch: true);
            // 处理 ElseIfs 链
            if (!ifStmt.ElseIfs.IsDefaultOrEmpty)
            {
                foreach (var (elifCond, elifBody) in ifStmt.ElseIfs)
                {
                    var elifCondVal = EmitExpression(elifCond);
                    var elifThen = CreateBlock();
                    var elifEnd = CreateBlock(); // 临时，下一轮 else 或 end

                    _currentBlock.BranchCondition = elifCondVal;
                    elifCondVal.Uses++;
                    _currentBlock.TrueSuccessor = elifThen;
                    _currentBlock.FalseSuccessor = elifEnd;
                    elifThen.Predecessors.Add(_currentBlock);
                    elifEnd.Predecessors.Add(_currentBlock);

                    SwitchToBlock(elifThen, fromConditionalBranch: true);
                    EmitStatements(elifBody.Statements);
                    if (NeedsTerminator(_currentBlock))
                    {
                        _currentBlock.JumpTarget = endBlock;
                        endBlock.Predecessors.Add(_currentBlock);
                    }

                    SwitchToBlock(elifEnd);
                }
            }
            // Else body
            if (ifStmt.ElseBody != null)
            {
                EmitStatements(ifStmt.ElseBody.Statements);
            }
            if (NeedsTerminator(_currentBlock))
            {
                _currentBlock.JumpTarget = endBlock;
                endBlock.Predecessors.Add(_currentBlock);
            }
        }

        SwitchToBlock(endBlock);
    }

    private void EmitWhile(BoundWhileStatement whileStmt)
    {
        var headerBlock = CreateBlock();
        var bodyBlock = CreateBlock();
        var endBlock = CreateBlock();

        // 注册 break/continue 标签映射
        _labelBlocks[whileStmt.BreakLabel] = endBlock;
        _labelBlocks[whileStmt.ContinueLabel] = headerBlock;

        // 当前块 → headerBlock
        _currentBlock.JumpTarget = headerBlock;
        headerBlock.Predecessors.Add(_currentBlock);

        // headerBlock：判断条件
        SwitchToBlock(headerBlock);
        var cond = EmitExpression(whileStmt.Condition);
        _currentBlock.BranchCondition = cond;
        cond.Uses++;
        _currentBlock.TrueSuccessor = bodyBlock;
        _currentBlock.FalseSuccessor = endBlock;
        bodyBlock.Predecessors.Add(_currentBlock);
        endBlock.Predecessors.Add(_currentBlock);

        // bodyBlock：循环体（条件分支目标，清空 defs）
        SwitchToBlock(bodyBlock, fromConditionalBranch: true);
        EmitStatements(whileStmt.Body.Statements);
        if (NeedsTerminator(_currentBlock))
        {
            _currentBlock.JumpTarget = headerBlock; // 回边
            headerBlock.Predecessors.Add(_currentBlock);
        }

        SwitchToBlock(endBlock);
    }

    private void EmitFor(BoundForStatement forStmt)
    {
        var headerBlock = CreateBlock();
        var endBlock = CreateBlock();

        _labelBlocks[forStmt.BreakLabel] = endBlock;

        if (forStmt.Kind2 == ForKind.Infinite)
        {
            var bodyBlock = CreateBlock();

            _labelBlocks[forStmt.ContinueLabel] = headerBlock;

            _currentBlock.JumpTarget = headerBlock;
            headerBlock.Predecessors.Add(_currentBlock);

            // header: 无条件 true → body
            SwitchToBlock(headerBlock);
            var trueVal = NewValue(SsaOp.ConstBool, ScriptType.Bool);
            trueVal.Const.SetBool(true);
            AddInst(trueVal);
            _currentBlock.BranchCondition = trueVal;
            trueVal.Uses++;
            _currentBlock.TrueSuccessor = bodyBlock;
            _currentBlock.FalseSuccessor = endBlock;
            bodyBlock.Predecessors.Add(_currentBlock);
            endBlock.Predecessors.Add(_currentBlock);

            // body（条件分支目标，清空 defs）
            SwitchToBlock(bodyBlock, fromConditionalBranch: true);
            EmitStatements(forStmt.Body.Statements);

            if (NeedsTerminator(_currentBlock))
            {
                _currentBlock.JumpTarget = headerBlock;
                headerBlock.Predecessors.Add(_currentBlock);
            }
        }
        else
        {
            // For_Static / For_Full
            var variable = forStmt.Variable!;
            var upperBound = forStmt.UpperBound!;
            var bodyBlock = CreateBlock();
            var continueBlock = CreateBlock();

            // CONTINUE 跳到 continueBlock（step 发生的地方）
            _labelBlocks[forStmt.ContinueLabel] = continueBlock;

            // 初始化变量: $i = lower
            var initVal = EmitExpression(forStmt.LowerBound!);
            var storeOp = variable is GlobalVariableSymbol ? SsaOp.StoreGlobal : SsaOp.StoreLocal;
            var store = NewValue(storeOp, ScriptType.Void, initVal, aux: variable);
            AddInst(store);
            _defs[variable] = initVal;

            // 计算上限值
            var upperVal = EmitExpression(upperBound);

            // 当前块 → header
            _currentBlock.JumpTarget = headerBlock;
            headerBlock.Predecessors.Add(_currentBlock);

            // header: LoadLocal $i + LeqInt → body / end
            SwitchToBlock(headerBlock);
            var loadOp = variable is GlobalVariableSymbol ? SsaOp.LoadGlobal : SsaOp.LoadLocal;
            var varVal = NewValue(loadOp, ScriptType.Int, aux: variable);
            AddInst(varVal);
            var cond = NewValue(SsaOp.LeqInt, ScriptType.Bool, varVal, upperVal);
            AddInst(cond);
            _currentBlock.BranchCondition = cond;
            cond.Uses++;
            _currentBlock.TrueSuccessor = bodyBlock;
            _currentBlock.FalseSuccessor = endBlock;
            bodyBlock.Predecessors.Add(_currentBlock);
            endBlock.Predecessors.Add(_currentBlock);

            // body: 用户代码（条件分支目标，清空 defs）
            SwitchToBlock(bodyBlock, fromConditionalBranch: true);
            EmitStatements(forStmt.Body.Statements);

            // body 落空 → postBodyCheck: 检查是否需要increment
            //    如果当前值 == upper：已经是最后迭代的值，直接跳到end（不increment，保持$i = upper）
            //    如果当前值 < upper：需要继续循环，跳到continueBlock执行increment
            if (NeedsTerminator(_currentBlock))
            {
                var varValCheck = NewValue(loadOp, ScriptType.Int, aux: variable);
                AddInst(varValCheck);
                var isLastIter = NewValue(SsaOp.EqInt, ScriptType.Bool, varValCheck, upperVal);
                AddInst(isLastIter);
                _currentBlock.BranchCondition = isLastIter;
                isLastIter.Uses++;
                _currentBlock.TrueSuccessor = endBlock;         // 最后一次 → 跳到end（不increment）
                _currentBlock.FalseSuccessor = continueBlock;   // 否则 → increment
                endBlock.Predecessors.Add(_currentBlock);
                continueBlock.Predecessors.Add(_currentBlock);
            }

            // continueBlock: increment $i = $i + 1, jump to header
            SwitchToBlock(continueBlock);
            var varValForInc = NewValue(loadOp, ScriptType.Int, aux: variable);
            AddInst(varValForInc);
            var oneVal = NewValue(SsaOp.ConstInt, ScriptType.Int);
            oneVal.Const.SetInt(1);
            AddInst(oneVal);
            var stepVal = NewValue(SsaOp.AddInt, ScriptType.Int, varValForInc, oneVal);
            AddInst(stepVal);
            var stepStore = NewValue(storeOp, ScriptType.Void, stepVal, aux: variable);
            AddInst(stepStore);

            _currentBlock.JumpTarget = headerBlock;
            headerBlock.Predecessors.Add(_currentBlock);
        }

        SwitchToBlock(endBlock);
    }

    private void EmitUntil(BoundUntilStatement untilStmt)
    {
        var headerBlock = CreateBlock();
        var bodyBlock = CreateBlock();
        var endBlock = CreateBlock();

        _labelBlocks[untilStmt.BreakLabel] = endBlock;
        _labelBlocks[untilStmt.ContinueLabel] = headerBlock;

        _currentBlock.JumpTarget = headerBlock;
        headerBlock.Predecessors.Add(_currentBlock);

        // headerBlock: 评估条件，true → break, false → body
        SwitchToBlock(headerBlock);
        var cond = EmitExpression(untilStmt.Condition);
        _currentBlock.BranchCondition = cond;
        cond.Uses++;
        _currentBlock.TrueSuccessor = endBlock;   // 条件为 true → 结束
        _currentBlock.FalseSuccessor = bodyBlock;  // 条件为 false → 继续循环
        endBlock.Predecessors.Add(_currentBlock);
        bodyBlock.Predecessors.Add(_currentBlock);

        // bodyBlock: 循环体（条件分支目标，清空 defs） → 跳回 header
        SwitchToBlock(bodyBlock, fromConditionalBranch: true);
        EmitStatements(untilStmt.Body.Statements);
        if (NeedsTerminator(_currentBlock))
        {
            _currentBlock.JumpTarget = headerBlock;
            headerBlock.Predecessors.Add(_currentBlock);
        }

        SwitchToBlock(endBlock);
    }

    private void EmitGoto(BoundLabel label)
    {
        var target = GetOrCreateLabelBlock(label);
        _currentBlock.JumpTarget = target;
        target.Predecessors.Add(_currentBlock);
        // 创建新块供后续（不可达）语句使用
        SwitchToBlock(CreateBlock());
    }

    private void EmitCondGoto(BoundConditionalGotoStatement cgs)
    {
        var cond = EmitExpression(cgs.Condition);
        var target = GetOrCreateLabelBlock(cgs.Label);
        var fallThrough = CreateBlock();

        _currentBlock.BranchCondition = cond;
        cond.Uses++;
        if (cgs.JumpIfTrue)
        {
            _currentBlock.TrueSuccessor = target;
            _currentBlock.FalseSuccessor = fallThrough;
        }
        else
        {
            _currentBlock.FalseSuccessor = target;
            _currentBlock.TrueSuccessor = fallThrough;
        }
        target.Predecessors.Add(_currentBlock);
        fallThrough.Predecessors.Add(_currentBlock);
        SwitchToBlock(fallThrough, fromConditionalBranch: true);
    }

    private void EmitLabel(BoundLabel label)
    {
        var labelBlock = GetOrCreateLabelBlock(label);
        // 如果当前块未终止，连接到 labelBlock
        if (NeedsTerminator(_currentBlock))
        {
            _currentBlock.JumpTarget = labelBlock;
            labelBlock.Predecessors.Add(_currentBlock);
        }
        SwitchToBlock(labelBlock);
    }

    private void EmitKeyAction(BoundKeyActStatement key)
    {
        if (key is BoundKeyPressStatement bps)
        {
            var dur = EmitExpression(bps.Duration);
            var v = NewValue(SsaOp.KeyPress, ScriptType.Void, dur, aux: new GamePadKeySymbol(bps.Act));
            AddInst(v);
        }
        else
        {
            var v = NewValue(SsaOp.KeyAction, ScriptType.Void, aux: new GamePadKeySymbol(key.Act));
            v.Const.SetBool(key.Up);
            AddInst(v);
        }
    }

    private void EmitStickAction(BoundStickActStatement stick)
    {
        if (stick is BoundStickPressStatement bps)
        {
            var dur = EmitExpression(bps.Duration);
            var v = NewValue(SsaOp.StickPress, ScriptType.Void, dur, aux: new GamePadKeySymbol(bps.Act));
            v.Const.SetInt(((int)bps.X << 16) | ((int)bps.Y << 8) | bps.Degree);
            AddInst(v);
        }
        else
        {
            var v = NewValue(SsaOp.StickAction, ScriptType.Void, aux: new GamePadKeySymbol(stick.Act));
            v.Const.SetInt(((int)stick.X << 16) | ((int)stick.Y << 8) | stick.Degree);
            AddInst(v);
        }
    }

    private void EmitFieldAssign(BoundFieldAssignStatement fa)
    {
        var target = EmitExpression(fa.Target);
        var value = EmitExpression(fa.Value);
        var v = NewValue(SsaOp.StoreField, fa.Field.FieldType, target, value, aux: fa.Field);
        AddInst(v);
    }

    private void EmitIndexAssign(BoundIndexAssignStatement ia)
    {
        // 检测 container 是字段访问且字段为数组类型 → StoreFieldIndex
        if (ia.Container is BoundFieldAccessExpression fa && fa.Field.FieldType is ArrayType)
        {
            var target = EmitExpression(fa.Target);
            var idx = EmitExpression(ia.Index);
            var val = EmitExpression(ia.Value);
            var elemType = ((ArrayType)fa.Field.FieldType).ElementType;
            var sv = NewValue(SsaOp.StoreFieldIndex, elemType, target, idx, aux: fa.Field);
            sv.ExtraArgs = new List<SsaValue> { val };
            AddExtraUses(sv.ExtraArgs);
            AddInst(sv);
            return;
        }

        var container = EmitExpression(ia.Container);
        var index = EmitExpression(ia.Index);
        var value = EmitExpression(ia.Value);
        var v = NewValue(SsaOp.StoreIndex, ScriptType.Void, container, index);
        v.ExtraArgs = new List<SsaValue> { value };
        AddExtraUses(v.ExtraArgs);
        AddInst(v);
    }

    // ============ 表达式发射 ============

    private SsaValue EmitExpression(BoundExpr expr)
    {
        // 仅对 BoundLiteralExpression 使用常量快速路径
        // BoundVariableExpression 也设置了 ConstantValue（初始值），但变量可变，必须走 Load 路径
        return expr switch
        {
            BoundLiteralExpression lit => EmitConst(lit.Type, lit.ConstantValue),
            BoundVariableExpression var => EmitVariable(var),
            BoundBinaryExpression bin => EmitBinary(bin),
            BoundUnaryExpression un => EmitUnary(un),
            BoundConversionExpression conv => EmitConversion(conv),
            BoundCallExpression call => EmitCall(call),
            BoundIndexVariableExpression idx => EmitIndex(idx),
            BoundSliceExpression slice => EmitSlice(slice),
            BoundIndexDeclxpression decl => EmitArrayInit(decl),
            BoundStructInitExpression si => EmitStructInit(si),
            BoundFieldAccessExpression fa => EmitFieldAccess(fa),
            BoundRuntimeValueExpression rv => EmitRuntimeValue(rv),
            BoundImageLabelExpression il => EmitImageLabel(il),
            BoundErrorExpression => NewValue(SsaOp.Nop, ScriptType.Int),
            _ => throw new InvalidOperationException($"未知表达式: {expr.Kind}")
        };
    }

    private SsaValue EmitConst(ScriptType type, object? value)
    {
        var op = type switch
        {
            _ when type.Equals(ScriptType.Bool) => SsaOp.ConstBool,
            _ when type.Equals(ScriptType.Byte) => SsaOp.ConstByte,
            _ when type.Equals(ScriptType.Int) => SsaOp.ConstInt,
            _ when type.Equals(ScriptType.UInt) => SsaOp.ConstUInt,
            _ when type.Equals(ScriptType.UInt64) => SsaOp.ConstUInt64,
            _ when type.Equals(ScriptType.Double) => SsaOp.ConstDouble,
            _ when type.Equals(ScriptType.String) => SsaOp.ConstString,
            _ when type.Equals(ScriptType.Ptr) => SsaOp.ConstPtr,
            _ => SsaOp.ConstInt
        };
        var v = NewValue(op, type);
        if (value != null)
            SetConstPayload(v, value);
        if (op == SsaOp.ConstString && value == null)
            v.ConstString = "";
        AddInst(v);  // 常量必须加入指令列表以便 Evaluator 执行并写入 _valueCache
        return v;
    }

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
        if (_defs.TryGetValue(var.Variable, out var def))
            return def;

        // 常量内联：只读变量有编译期值时直接 emit 常量
        if (var.Variable.IsReadOnly && var.Variable.Value != null)
            return EmitConst(var.Type, var.Variable.Value);

        // 全局变量或首次使用
        var op = var.Variable is GlobalVariableSymbol ? SsaOp.LoadGlobal : SsaOp.LoadLocal;
        var load = NewValue(op, var.Type, aux: var.Variable);
        AddInst(load);
        return load;
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
        evalRight.Predecessors.Add(_currentBlock);
        endBlock.Predecessors.Add(_currentBlock);

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
        endBlock.Predecessors.Add(_currentBlock);

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
        endBlock.Predecessors.Add(_currentBlock);
        evalRight.Predecessors.Add(_currentBlock);

        var trueVal = NewValue(SsaOp.ConstBool, ScriptType.Bool);
        trueVal.Const.SetBool(true);
        trueVal.Block = _currentBlock;
        AddInst(trueVal);

        // evalRight（条件分支目标，清空 defs）
        SwitchToBlock(evalRight, fromConditionalBranch: true);
        var rightVal = EmitExpression(rightExpr);
        _currentBlock.JumpTarget = endBlock;
        endBlock.Predecessors.Add(_currentBlock);

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

    private SsaValue EmitIntrinsic(BoundCallExpression call)
    {
        var fn = call.Function;
        if (fn == BuiltinFunctions.IntConvert)
        {
            var arg = EmitExpression(call.Arguments[0]);
            return EmitIntrinsicInt(arg);
        }
        if (fn == BuiltinFunctions.StrConvert)
        {
            var arg = EmitExpression(call.Arguments[0]);
            if (arg.Type.Equals(ScriptType.String)) return arg;
            return EmitAndAdd(SsaOp.ConvToString, ScriptType.String, arg);
        }
        if (fn == BuiltinFunctions.Length)
        {
            var arg = EmitExpression(call.Arguments[0]);
            return EmitAndAdd(SsaOp.ArrayLen, ScriptType.Int, arg);
        }
        if (fn == BuiltinFunctions.Append)
        {
            var arr = EmitExpression(call.Arguments[0]);
            var val = EmitExpression(call.Arguments[1]);
            return EmitAndAdd(SsaOp.ArrayAppend, call.Type, arr, val);
        }
        if (fn == BuiltinFunctions.Wait)
        {
            var dur = EmitExpression(call.Arguments[0]);
            return EmitAndAdd(SsaOp.Wait, ScriptType.Void, dur);
        }
        if (fn == BuiltinFunctions.Rand)
        {
            var max = EmitExpression(call.Arguments[0]);
            return EmitAndAdd(SsaOp.Rand, ScriptType.Int, max);
        }
        if (fn == BuiltinFunctions.CaptureHole)
        {
            var x = EmitExpression(call.Arguments[0]);
            var y = EmitExpression(call.Arguments[1]);
            var w = EmitExpression(call.Arguments[2]);
            var h = EmitExpression(call.Arguments[3]);
            var v = NewValue(SsaOp.Capture, ScriptType.String, x, y);
            v.ExtraArgs = new List<SsaValue> { w, h };
            AddExtraUses(v.ExtraArgs);
            AddInst(v);
            return v;
        }
        if (fn == BuiltinFunctions.OcrHole)
        {
            var x = EmitExpression(call.Arguments[0]);
            var y = EmitExpression(call.Arguments[1]);
            var w = EmitExpression(call.Arguments[2]);
            var h = EmitExpression(call.Arguments[3]);
            var lang = EmitExpression(call.Arguments[4]);
            var v = NewValue(SsaOp.Ocr, ScriptType.String, x, y);
            v.ExtraArgs = new List<SsaValue> { w, h, lang };
            AddExtraUses(v.ExtraArgs);
            AddInst(v);
            return v;
        }
        if (fn == BuiltinFunctions.RoiHole)
        {
            var image = EmitExpression(call.Arguments[0]);
            var x = EmitExpression(call.Arguments[1]);
            var y = EmitExpression(call.Arguments[2]);
            var w = EmitExpression(call.Arguments[3]);
            var h = EmitExpression(call.Arguments[4]);
            var v = NewValue(SsaOp.Roi, ScriptType.String, image, x);
            v.ExtraArgs = new List<SsaValue> { y, w, h };
            AddExtraUses(v.ExtraArgs);
            AddInst(v);
            return v;
        }
        if (fn == BuiltinFunctions.OcrInitHole)
        {
            var lang = EmitExpression(call.Arguments[0]);
            var dataPath = EmitExpression(call.Arguments[1]);
            var engineMode = EmitExpression(call.Arguments[2]);
            var psmode = EmitExpression(call.Arguments[3]);
            var v = NewValue(SsaOp.OcrInit, ScriptType.Bool, lang, dataPath);
            v.ExtraArgs = new List<SsaValue> { engineMode, psmode };
            AddExtraUses(v.ExtraArgs);
            AddInst(v);
            return v;
        }
        throw new InvalidOperationException($"未知内联函数: {fn.Name}");
    }

    private SsaValue EmitIntrinsicInt(SsaValue arg)
    {
        if (arg.Type.Equals(ScriptType.Int)) return arg;
        if (arg.Type.Equals(ScriptType.Double)) return EmitAndAdd(SsaOp.ConvDoubleToInt, ScriptType.Int, arg);
        if (arg.Type.Equals(ScriptType.Bool)) return EmitAndAdd(SsaOp.ConvBoolToInt, ScriptType.Int, arg);
        if (arg.Type.Equals(ScriptType.Byte)) return EmitAndAdd(SsaOp.ConvByteToInt, ScriptType.Int, arg);
        if (arg.Type.Equals(ScriptType.UInt)) return EmitAndAdd(SsaOp.ConvIntToUInt, ScriptType.Int, arg);
        if (arg.Type.Equals(ScriptType.UInt64)) return EmitAndAdd(SsaOp.ConvUInt64ToInt, ScriptType.Int, arg);
        return EmitAndAdd(SsaOp.ConvToInt, ScriptType.Int, arg);
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

    private static SsaOp MapBinaryOp(BoundBinaryOperator op) => op.Kind switch
    {
        BoundBinaryOperatorKind.Addition when op.LeftType.Equals(ScriptType.Int) => SsaOp.AddInt,
        BoundBinaryOperatorKind.Addition when op.LeftType.Equals(ScriptType.UInt) => SsaOp.AddUInt,
        BoundBinaryOperatorKind.Addition when op.LeftType.Equals(ScriptType.Double) => SsaOp.AddDouble,
        BoundBinaryOperatorKind.Addition when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.AddUInt64,
        BoundBinaryOperatorKind.Addition when op.LeftType.Equals(ScriptType.Byte) => SsaOp.AddInt,

        BoundBinaryOperatorKind.Subtraction when op.LeftType.Equals(ScriptType.Int) => SsaOp.SubInt,
        BoundBinaryOperatorKind.Subtraction when op.LeftType.Equals(ScriptType.UInt) => SsaOp.SubUInt,
        BoundBinaryOperatorKind.Subtraction when op.LeftType.Equals(ScriptType.Double) => SsaOp.SubDouble,
        BoundBinaryOperatorKind.Subtraction when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.SubUInt64,
        BoundBinaryOperatorKind.Subtraction when op.LeftType.Equals(ScriptType.Byte) => SsaOp.SubInt,

        BoundBinaryOperatorKind.Multiplication when op.LeftType.Equals(ScriptType.Int) => SsaOp.MulInt,
        BoundBinaryOperatorKind.Multiplication when op.LeftType.Equals(ScriptType.UInt) => SsaOp.MulUInt,
        BoundBinaryOperatorKind.Multiplication when op.LeftType.Equals(ScriptType.Double) => SsaOp.MulDouble,
        BoundBinaryOperatorKind.Multiplication when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.MulUInt64,
        BoundBinaryOperatorKind.Multiplication when op.LeftType.Equals(ScriptType.Byte) => SsaOp.MulInt,

        BoundBinaryOperatorKind.Division when op.LeftType.Equals(ScriptType.Int) => SsaOp.DivInt,
        BoundBinaryOperatorKind.Division when op.LeftType.Equals(ScriptType.UInt) => SsaOp.DivUInt,
        BoundBinaryOperatorKind.Division when op.LeftType.Equals(ScriptType.Double) => SsaOp.DivDouble,
        BoundBinaryOperatorKind.Division when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.DivUInt64,
        BoundBinaryOperatorKind.Division when op.LeftType.Equals(ScriptType.Byte) => SsaOp.DivInt,

        BoundBinaryOperatorKind.Mod when op.LeftType.Equals(ScriptType.Int) => SsaOp.ModInt,
        BoundBinaryOperatorKind.Mod when op.LeftType.Equals(ScriptType.UInt) => SsaOp.ModUInt,
        BoundBinaryOperatorKind.Mod when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.ModUInt64,
        BoundBinaryOperatorKind.Mod when op.LeftType.Equals(ScriptType.Byte) => SsaOp.ModInt,

        BoundBinaryOperatorKind.RoundDiv when op.LeftType.Equals(ScriptType.Int) => SsaOp.RoundDivInt,
        BoundBinaryOperatorKind.RoundDiv when op.LeftType.Equals(ScriptType.Byte) => SsaOp.RoundDivInt,

        BoundBinaryOperatorKind.BitwiseAnd when op.LeftType.Equals(ScriptType.Int) => SsaOp.AndInt,
        BoundBinaryOperatorKind.BitwiseAnd when op.LeftType.Equals(ScriptType.UInt) => SsaOp.AndInt,
        BoundBinaryOperatorKind.BitwiseAnd when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.AndInt,
        BoundBinaryOperatorKind.BitwiseAnd when op.LeftType.Equals(ScriptType.Byte) => SsaOp.AndInt,

        BoundBinaryOperatorKind.BitwiseOr when op.LeftType.Equals(ScriptType.Int) => SsaOp.OrInt,
        BoundBinaryOperatorKind.BitwiseOr when op.LeftType.Equals(ScriptType.UInt) => SsaOp.OrInt,
        BoundBinaryOperatorKind.BitwiseOr when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.OrInt,
        BoundBinaryOperatorKind.BitwiseOr when op.LeftType.Equals(ScriptType.Byte) => SsaOp.OrInt,

        BoundBinaryOperatorKind.BitwiseXor when op.LeftType.Equals(ScriptType.Int) => SsaOp.XorInt,
        BoundBinaryOperatorKind.BitwiseXor when op.LeftType.Equals(ScriptType.UInt) => SsaOp.XorInt,
        BoundBinaryOperatorKind.BitwiseXor when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.XorInt,
        BoundBinaryOperatorKind.BitwiseXor when op.LeftType.Equals(ScriptType.Byte) => SsaOp.XorInt,

        BoundBinaryOperatorKind.BitLeftShift when op.LeftType.Equals(ScriptType.Int) => SsaOp.ShlInt,
        BoundBinaryOperatorKind.BitLeftShift when op.LeftType.Equals(ScriptType.UInt) => SsaOp.ShlInt,
        BoundBinaryOperatorKind.BitLeftShift when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.ShlInt,
        BoundBinaryOperatorKind.BitLeftShift when op.LeftType.Equals(ScriptType.Byte) => SsaOp.ShlInt,

        BoundBinaryOperatorKind.BitRightShift when op.LeftType.Equals(ScriptType.Int) => SsaOp.ShrInt,
        BoundBinaryOperatorKind.BitRightShift when op.LeftType.Equals(ScriptType.UInt) => SsaOp.ShrInt,
        BoundBinaryOperatorKind.BitRightShift when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.ShrInt,
        BoundBinaryOperatorKind.BitRightShift when op.LeftType.Equals(ScriptType.Byte) => SsaOp.ShrInt,

        // 比较
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.Int) => SsaOp.EqInt,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.UInt) => SsaOp.EqUInt,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.Double) => SsaOp.EqDouble,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.EqUInt64,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.Bool) => SsaOp.EqBool,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.String) => SsaOp.EqString,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.Ptr) => SsaOp.EqPtr,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.Byte) => SsaOp.EqByte,

        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.Int) => SsaOp.NeqInt,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.UInt) => SsaOp.NeqUInt,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.Double) => SsaOp.NeqDouble,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.NeqUInt64,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.Bool) => SsaOp.NeqBool,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.String) => SsaOp.NeqString,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.Ptr) => SsaOp.NeqPtr,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.Byte) => SsaOp.NeqByte,

        BoundBinaryOperatorKind.Less when op.LeftType.Equals(ScriptType.Int) => SsaOp.LtInt,
        BoundBinaryOperatorKind.Less when op.LeftType.Equals(ScriptType.UInt) => SsaOp.LtUInt,
        BoundBinaryOperatorKind.Less when op.LeftType.Equals(ScriptType.Double) => SsaOp.LtDouble,
        BoundBinaryOperatorKind.Less when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.LtUInt64,
        BoundBinaryOperatorKind.Less when op.LeftType.Equals(ScriptType.Byte) => SsaOp.LtByte,

        BoundBinaryOperatorKind.LessOrEquals when op.LeftType.Equals(ScriptType.Int) => SsaOp.LeqInt,
        BoundBinaryOperatorKind.LessOrEquals when op.LeftType.Equals(ScriptType.UInt) => SsaOp.LeqUInt,
        BoundBinaryOperatorKind.LessOrEquals when op.LeftType.Equals(ScriptType.Double) => SsaOp.LeqDouble,
        BoundBinaryOperatorKind.LessOrEquals when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.LeqUInt64,
        BoundBinaryOperatorKind.LessOrEquals when op.LeftType.Equals(ScriptType.Byte) => SsaOp.LeqByte,

        BoundBinaryOperatorKind.Greater when op.LeftType.Equals(ScriptType.Int) => SsaOp.GtInt,
        BoundBinaryOperatorKind.Greater when op.LeftType.Equals(ScriptType.UInt) => SsaOp.GtUInt,
        BoundBinaryOperatorKind.Greater when op.LeftType.Equals(ScriptType.Double) => SsaOp.GtDouble,
        BoundBinaryOperatorKind.Greater when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.GtUInt64,
        BoundBinaryOperatorKind.Greater when op.LeftType.Equals(ScriptType.Byte) => SsaOp.GtByte,

        BoundBinaryOperatorKind.GreaterOrEquals when op.LeftType.Equals(ScriptType.Int) => SsaOp.GeqInt,
        BoundBinaryOperatorKind.GreaterOrEquals when op.LeftType.Equals(ScriptType.UInt) => SsaOp.GeqUInt,
        BoundBinaryOperatorKind.GreaterOrEquals when op.LeftType.Equals(ScriptType.Double) => SsaOp.GeqDouble,
        BoundBinaryOperatorKind.GreaterOrEquals when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.GeqUInt64,
        BoundBinaryOperatorKind.GreaterOrEquals when op.LeftType.Equals(ScriptType.Byte) => SsaOp.GeqByte,

        BoundBinaryOperatorKind.In => SsaOp.Contains,

        _ => throw new InvalidOperationException(
            $"未映射的二元运算: {op.Kind} + {op.LeftType}")
    };

    private static SsaOp MapConversion(ScriptType from, ScriptType to) => (from, to) switch
    {
        var (f, t) when f.Equals(t) => SsaOp.Nop,

        _ when to.Equals(ScriptType.Int) && from.Equals(ScriptType.Bool) => SsaOp.ConvBoolToInt,
        _ when to.Equals(ScriptType.Int) && from.Equals(ScriptType.Byte) => SsaOp.ConvByteToInt,
        _ when to.Equals(ScriptType.UInt) && from.Equals(ScriptType.Int) => SsaOp.ConvIntToUInt,
        _ when to.Equals(ScriptType.UInt64) && from.Equals(ScriptType.Int) => SsaOp.ConvIntToUInt64,
        _ when to.Equals(ScriptType.UInt64) && from.Equals(ScriptType.UInt) => SsaOp.ConvUIntToUInt64,
        _ when to.Equals(ScriptType.Double) && from.Equals(ScriptType.Int) => SsaOp.ConvIntToDouble,
        _ when to.Equals(ScriptType.Byte) && from.Equals(ScriptType.Int) => SsaOp.ConvIntToByte,
        _ when to.Equals(ScriptType.String) => SsaOp.ConvToString,
        _ when to.Equals(ScriptType.Ptr) && from.Equals(ScriptType.UInt64) => SsaOp.ConvUInt64ToPtr,
        _ when to.Equals(ScriptType.Ptr) && from.Equals(ScriptType.Int) => SsaOp.ConvIntToPtr,
        _ when to.Equals(ScriptType.Int) && from.Equals(ScriptType.Ptr) => SsaOp.ConvPtrToInt,
        _ when to.Equals(ScriptType.Int) && from.Equals(ScriptType.Double) => SsaOp.ConvDoubleToInt,
        _ when to.Equals(ScriptType.UInt64) && from.Equals(ScriptType.UInt64) => SsaOp.Nop,
        _ when to.Equals(ScriptType.Int) && from.Equals(ScriptType.UInt64) => SsaOp.ConvUInt64ToInt,
        _ when to.Equals(ScriptType.UInt) && from.Equals(ScriptType.UInt) => SsaOp.Nop,

        _ => throw new InvalidOperationException($"未映射的转换: {from} → {to}")
    };
}