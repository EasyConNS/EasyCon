using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using static EasyCon.Script.Binding.BoundNodeKind;

namespace EasyCon.Script;

public class ScriptException(string message, int address) : Exception(message)
{
    public int Address { get; private set; } = address;
}

internal sealed class Evaluator : IEvalContext, IDisposable
{
    private readonly BoundProgram _program;
    private readonly Value[] _globalValues;
    private readonly Dictionary<VariableSymbol, int> _globalIndex;
    private readonly Stack<Value[]> _localFrames = new();
    private readonly Dictionary<FunctionSymbol, BoundBlockStatement> _functions = [];
    private readonly ImmutableDictionary<string, Func<int>> _externalGetters = [];

    private readonly long _TIME = DateTime.Now.Ticks;
    private readonly Random _rand = new();
    private bool _cancelLineBreak = false;
    private CancellationToken _token;
    private Value _lastValue;
    private int _yieldCounter;
    private Value[]? _tailCallArgsBuffer;

    private readonly Dictionary<FunctionSymbol, ICallable> _callables = [];
    private readonly List<EcsStruct> _structInstances = [];

    public IOutputAdapter? Output { get; set; }
    public ICGamePad? GamePad { get; set; }

    public IImageAdapter? ImageAdapter { get; set; }

    // IEvalContext
    ICGamePad? IEvalContext.GamePad => GamePad;
    IOutputAdapter? IEvalContext.Output => Output;
    IImageAdapter? IEvalContext.Image => ImageAdapter;
    Random IEvalContext.Rand => _rand;
    int IEvalContext.Timestamp => (int)((DateTime.Now.Ticks - _TIME) / 10_000);

    bool IEvalContext.CancelLineBreak { get => _cancelLineBreak; set => _cancelLineBreak = value; }

    public Evaluator(BoundProgram program, ImmutableDictionary<string, Func<int>> externalGetters, CancellationToken token)
    {
        _program = program;
        _token = token;
        _externalGetters = externalGetters;
        _localFrames.Push([]);

        foreach (var kv in _program.Functions)
        {
            _functions.Add(kv.Key, kv.Value);
        }

        // 扫描所有函数体，递归收集全局变量并建立索引映射
        var globalVarList = new List<VariableSymbol>();
        var seen = new HashSet<VariableSymbol>();
        foreach (var body in _functions.Values)
            CollectGlobalVars(body, globalVarList, seen);
        _globalValues = new Value[globalVarList.Count];
        for (int i = 0; i < _globalValues.Length; i++)
            _globalValues[i] = 0; // 默认值为 0
        _globalIndex = new Dictionary<VariableSymbol, int>(globalVarList.Count);
        for (int i = 0; i < globalVarList.Count; i++)
            _globalIndex[globalVarList[i]] = i;

        RegisterCallables();
    }

    private void RegisterCallables()
    {
        // 注册内置函数 callable
        foreach (var (symbol, callable) in BuiltinCallable.GetAll())
        {
            _callables[symbol] = callable;
        }

        // 注册用户函数 callable（_functions 中仅含用户函数 + $eval）
        foreach (var fn in _functions.Keys)
        {
            if (_callables.ContainsKey(fn)) continue;
            _callables[fn] = new DelegateCallable((args, ctx, tk) =>
            {
                if (ctx is Evaluator evaluator)
                {
                    return evaluator.EvaluateFunctionBodyWithTailRecursion(fn, args, tk);
                }
                else
                {
                    // fallback（不应在正常流程中到达）
                    return ctx.EvaluateFunctionBody(fn);
                }
            });
        }

        // 注册 EXTERN 函数（懒加载：首次调用时才解析库和函数地址）
        if (!_program.ExternFunctions.IsEmpty)
        {
            var loader = new NativeLoader();
            foreach (var (symbol, callable) in loader.RegisterExternFunctions(_program.ExternFunctions))
                _callables[symbol] = callable;
        }
    }

    public Value Evaluate()
    {
        var function = _program.MainFunction;
        if (function == null)
            return Value.Void;

        var body = _functions[function];
        PushFrame(function.LocalSlotCount);
        try
        {
            return EvaluateStatement(body);
        }
        finally
        {
            PopFrame();
        }
    }

    private Value EvaluateStatement(BoundBlockStatement body)
    {
        return EvaluateStatementCore(body, null, out _, out _);
    }

    private Value EvaluateStatement(
        BoundBlockStatement body,
        FunctionSymbol tailRecursionTarget,
        out bool isTailCall,
        out ImmutableArray<Value> tailCallArgs)
    {
        return EvaluateStatementCore(body, tailRecursionTarget, out isTailCall, out tailCallArgs);
    }

    private Value EvaluateStatementCore(
        BoundBlockStatement body,
        FunctionSymbol? tailRecursionTarget,
        out bool isTailCall,
        out ImmutableArray<Value> tailCallArgs)
    {
        isTailCall = false;
        tailCallArgs = ImmutableArray<Value>.Empty;

        var labelToIndex = body.LabelIndex;
        var index = 0;
        while (!_token.IsCancellationRequested && index < body.Statements.Length)
        {
            var s = body.Statements[index];
            switch (s.Kind)
            {
                case NopStatement:
                    index++;
                    break;
                case ConstantDeclaration:
                case VariableDeclaration:
                    EvaluateVariableDeclaration((BoundVariableDeclaration)s);
                    index++;
                    break;
                case ExpressionStatement:
                    EvaluateExpressionStatement((BoundExprStatement)s);
                    index++;
                    break;
                case KeyAction:
                    EvaluateKeyAction((BoundKeyActStatement)s);
                    index++;
                    break;
                case StickAction:
                    EvaluateStickKeyAction((BoundStickActStatement)s);
                    index++;
                    break;
                case GotoStatement:
                    var gs = (BoundGotoStatement)s;
                    index = labelToIndex[gs.Label];
                    YieldIfDue();
                    break;
                case ConditionGotoStatement:
                    var cgs = (BoundConditionalGotoStatement)s;
                    var condition = EvaluateExpression(cgs.Condition).AsBool();
                    if (condition == cgs.JumpIfTrue)
                        index = labelToIndex[cgs.Label];
                    else
                        index++;
                    YieldIfDue();
                    break;
                case Label:
                    index++;
                    break;
                case Return:
                    var rs = (BoundReturnStatement)s;
                    if (rs.Expression != null)
                    {
                        if (tailRecursionTarget != null
                            && rs.IsTailCall
                            && rs.TailCallFunction == tailRecursionTarget)
                        {
                            var callExpr = (BoundCallExpression)rs.Expression;
                            var argLen = callExpr.Arguments.Length;
                            var buf = _tailCallArgsBuffer ??= new Value[argLen];
                            for (int i = 0; i < argLen; i++)
                            {
                                buf[i] = EvaluateExpression(callExpr.Arguments[i]);
                                Debug.Assert(buf[i] != Value.Void);
                            }
                            isTailCall = true;
                            tailCallArgs = ImmutableArray.Create(buf, 0, argLen);
                            return Value.Void;
                        }
                        _lastValue = EvaluateExpression(rs.Expression);
                    }
                    return _lastValue;
                case BoundNodeKind.FieldAssignment:
                    EvaluateFieldAssignment((BoundFieldAssignStatement)s);
                    index++;
                    break;
                case BoundNodeKind.FieldIndexAssignment:
                    EvaluateFieldIndexAssignment((BoundFieldIndexAssignStatement)s);
                    index++;
                    break;
                case BoundNodeKind.IndexAssignment:
                    EvaluateIndexAssignment((BoundIndexAssignStatement)s);
                    index++;
                    break;
                default:
                    throw new ScriptException($"执行语句类型未知", index);
            }
        }
        return _lastValue;
    }

    private void YieldIfDue()
    {
        if (++_yieldCounter >= 1000)
        {
            _yieldCounter = 0;
            Thread.Yield();
        }
    }

    private void EvaluateVariableDeclaration(BoundVariableDeclaration node)
    {
        var value = EvaluateExpression(node.Initializer);
        Debug.Assert(value != Value.Void);

        _lastValue = value;
        Assign(node.Variable, value);
    }

    private void EvaluateExpressionStatement(BoundExprStatement node)
    {
        _lastValue = EvaluateExpression(node.Expression);
    }
    public Value EvaluateExpression(BoundExpr node)
    {
        if (node.ConstantValue != Value.Void)
            return EvaluateConstantExpression(node);

        switch (node.Kind)
        {
            case Variable:
                return EvaluateVariableExpression((BoundVariableExpression)node);
            case IndexDecl:
                return EvaluateIndexDeclExpression((BoundIndexDeclxpression)node);
            case IndexVariable:
                return EvaluateIndexVariableExpression((BoundIndexVariableExpression)node);
            case SliceVariable:
                return EvaluateSliceExpression((BoundSliceExpression)node);
            case ExLabelVariable:
                var imglabel = (BoundExternalVariableExpression)node;
                if (!_externalGetters.TryGetValue(imglabel.Name, out var getter))
                    throw new Exception($"找不到外部变量 \"{imglabel.Name}\" 的getter");
                return getter();
            case UnaryExpression:
                return EvaluateUnaryExpression((BoundUnaryExpression)node);
            case BinaryExpression:
                return EvaluateBinaryExpression((BoundBinaryExpression)node);
            case ConversionExpression:
                return EvaluateConversionExpression((BoundConversionExpression)node);
            case CallExpression:
                return EvaluateCallExpression((BoundCallExpression)node);
            case StructInit:
                return EvaluateStructInitExpression((BoundStructInitExpression)node);
            case FieldAccess:
                return EvaluateFieldAccessExpression((BoundFieldAccessExpression)node);
            case FieldIndexAccess:
                return EvaluateFieldIndexAccessExpression((BoundFieldIndexAccessExpression)node);
            default:
                throw new Exception($"无法执行的表达式{node.Kind}");
        }
    }

    private static Value EvaluateConstantExpression(BoundExpr n)
    {
        Debug.Assert(n.ConstantValue != Value.Void);

        return n.ConstantValue;
    }

    private Value GetValue(VariableSymbol v)
    {
        if (v is GlobalVariableSymbol)
            return _globalValues[_globalIndex[v]];
        var slot = ((LocalVariableSymbol)v).SlotIndex;
        Debug.Assert(slot >= 0, "局部变量未分配 slot");
        return _localFrames.Peek()[slot];
    }

    private Value EvaluateVariableExpression(BoundVariableExpression v)
    {
        return GetValue(v.Variable);
    }

    private Value EvaluateIndexDeclExpression(BoundIndexDeclxpression decl)
    {
        var elements = decl.Items.Select(EvaluateExpression);
        var elemType = ((GenericType)decl.Type).TypeArguments[0];
        return Value.CreateArray(elemType, elements);
    }

    private Value EvaluateIndexVariableExpression(BoundIndexVariableExpression idxExpr)
    {
        var container = EvaluateExpression(idxExpr.BaseExpression);
        var index = EvaluateExpression(idxExpr.Index).AsInt();
        if (index >= container.Length) throw new Exception($"数组下标越界");
        return container[index];
    }

    private Value EvaluateSliceExpression(BoundSliceExpression sliceExpr)
    {
        var target = EvaluateExpression(sliceExpr.BaseExpression);
        var start = EvaluateExpression(sliceExpr.Start).AsInt();
        var end = EvaluateExpression(sliceExpr.End);

        var endidx = end.Type == ScriptType.Int ? end.AsInt() : target.Length;
        if (start >= target.Length || endidx > target.Length || start > endidx) throw new Exception($"数组下标越界");
        return target[start..endidx];
    }

    private Value EvaluateConversionExpression(BoundConversionExpression node)
    {
        var value = EvaluateExpression(node.Expression);
        if (node.Type == ScriptType.Bool)
            return value.ToBoolean();
        if (node.Type == ScriptType.Int)
            return value.ToInt();
        if (node.Type == ScriptType.Double)
            return value.ToDouble();
        if (node.Type == ScriptType.UInt)
            return Value.FromUInt(unchecked((uint)value.AsInt()));
        if (node.Type == ScriptType.UInt64)
        {
            if (value.Type.Equals(ScriptType.UInt))
                return Value.FromUInt64(value.AsUInt());
            return Value.FromUInt64((ulong)(long)value.AsInt());
        }
        if (node.Type == ScriptType.Byte)
            return Value.FromByte((byte)value.AsInt());
        if (node.Type == ScriptType.String)
            return value.ToString();
        if (node.Type == ScriptType.Ptr)
        {
            if (value.TryGetStructPtr(out var ptr))
                return Value.FromPtr(ptr.ToInt64());
            return Value.FromPtr(value.AsInt());
        }
        if (node.Type is StructType st)
        {
            var sourcePtr = new IntPtr(value.AsPtr());
            var instance = new EcsStruct(st.Definition, sourcePtr);
            _structInstances.Add(instance);
            return Value.FromStruct(instance);
        }
        throw new Exception($"无效的类型转换{node.Type}");
    }

    private Value EvaluateUnaryExpression(BoundUnaryExpression u)
    {
        var operand = EvaluateExpression(u.Operand);

        Debug.Assert(operand != Value.Void);

        return Value.From(u.Op.Operate(operand));
    }

    private Value EvaluateBinaryExpression(BoundBinaryExpression b)
    {
        var left = EvaluateExpression(b.Left);
        if (b.Op.Kind == BoundBinaryOperatorKind.LogicalAnd && !left.AsBool()) return false;
        if (b.Op.Kind == BoundBinaryOperatorKind.LogicalOr && left.AsBool()) return true;
        var right = EvaluateExpression(b.Right);

        Debug.Assert(left != Value.Void && right != Value.Void);

        return b.Op.Kind switch
        {
            BoundBinaryOperatorKind.Addition => left + right,
            BoundBinaryOperatorKind.Subtraction => left - right,
            BoundBinaryOperatorKind.Multiplication => left * right,
            BoundBinaryOperatorKind.Division => left / right,
            BoundBinaryOperatorKind.Mod => left % right,
            BoundBinaryOperatorKind.RoundDiv => left.RoundDiv(right),
            BoundBinaryOperatorKind.BitwiseAnd => left & right,
            BoundBinaryOperatorKind.BitwiseOr => left | right,
            BoundBinaryOperatorKind.BitwiseXor => left ^ right,
            BoundBinaryOperatorKind.BitLeftShift => left << right,
            BoundBinaryOperatorKind.BitRightShift => left >> right,
            BoundBinaryOperatorKind.Equals => Value.FromBool(left.Equals(right)),
            BoundBinaryOperatorKind.NotEquals => Value.FromBool(!left.Equals(right)),
            BoundBinaryOperatorKind.Less => Value.FromBool(left < right),
            BoundBinaryOperatorKind.LessOrEquals => Value.FromBool(left <= right),
            BoundBinaryOperatorKind.Greater => Value.FromBool(left > right),
            BoundBinaryOperatorKind.GreaterOrEquals => Value.FromBool(left >= right),
            BoundBinaryOperatorKind.In => Value.FromBool(right.Contains(left)),
            BoundBinaryOperatorKind.LogicalAnd => Value.FromBool(left.AsBool() && right.AsBool()),
            BoundBinaryOperatorKind.LogicalOr => Value.FromBool(left.AsBool() || right.AsBool()),
            _ => throw new InvalidOperationException($"不支持的运算: {b.Op.Kind}")
        };
    }

    private Value EvaluateCallExpression(BoundCallExpression node)
    {
        var argCount = node.Arguments.Length;
        if (argCount == 0)
        {
            var callable = _callables[node.Function];
            var result = callable.Invoke(ReadOnlySpan<Value>.Empty, this, _token);
            if (result.Type is StructType) _structInstances.Add(result.AsStruct());
            return result;
        }

        Value[]? rented = null;
        var args = argCount <= 8
            ? (rented = ArrayPool<Value>.Shared.Rent(argCount))
            : new Value[argCount];
        try
        {
            for (int i = 0; i < argCount; i++)
            {
                args[i] = EvaluateExpression(node.Arguments[i]);
                Debug.Assert(args[i] != Value.Void);
            }
            var callable = _callables[node.Function];
            var result = callable.Invoke(args.AsSpan(0, argCount), this, _token);
            if (result.Type is StructType) _structInstances.Add(result.AsStruct());
            return result;
        }
        finally
        {
            if (rented != null)
                ArrayPool<Value>.Shared.Return(rented);
        }
    }

    private void EvaluateKeyAction(BoundKeyActStatement node)
    {
        if (node is BoundKeyPressStatement bps)
        {
            var dur = EvaluateExpression(bps.Duration).AsInt();
            GamePad?.ClickButtons(bps.Act, dur, _token);
        }
        else
        {
            if (node.Up)
            {
                GamePad?.ReleaseButtons(node.Act);
            }
            else
            {
                GamePad?.PressButtons(node.Act);
            }
        }
    }

    private void EvaluateStickKeyAction(BoundStickActStatement node)
    {
        if (node is BoundStickPressStatement bps)
        {
            var dur = EvaluateExpression(bps.Duration).AsInt();
            GamePad?.ClickStick(bps.Act, bps.X, bps.Y, dur, _token);
        }
        else
        {
            GamePad?.SetStick(node.Act, node.X, node.Y);
        }
    }

    private void Assign(VariableSymbol variable, Value value)
    {
        if (variable is GlobalVariableSymbol)
            _globalValues[_globalIndex[variable]] = value;
        else
        {
            var slot = ((LocalVariableSymbol)variable).SlotIndex;
            Debug.Assert(slot >= 0, "局部变量未分配 slot");
            _localFrames.Peek()[slot] = value;
        }
    }

    // IEvalContext 方法
    public Value EvaluateFunctionBody(FunctionSymbol function)
    {
        var body = _functions[function];
        return EvaluateStatement(body);
    }

    // 内部帧管理（尾递归使用）
    private void PushFrame(int slotCount) => _localFrames.Push(new Value[slotCount]);
    private void PopFrame() => _localFrames.Pop();

    /// <summary>
    /// 使用尾递归优化执行函数体
    /// </summary>
    public Value EvaluateFunctionBodyWithTailRecursion(FunctionSymbol function, ReadOnlySpan<Value> args, CancellationToken token)
    {
        var body = _functions[function];
        var frame = new Value[function.LocalSlotCount];
        for (int i = 0; i < args.Length; i++)
            frame[function.Parameters[i].SlotIndex] = args[i];

        int iterationCount = 0;
        while (true)
        {
            if (token.IsCancellationRequested)
                throw new OperationCanceledException(token);

            _localFrames.Push(frame);

            try
            {
                var result = EvaluateStatement(body, function, out bool isTailCall, out ImmutableArray<Value> newArgs);

                if (!isTailCall)
                    return result;

                // 复用 frame 数组，避免每次迭代分配新对象
                for (int i = 0; i < newArgs.Length; i++)
                    frame[function.Parameters[i].SlotIndex] = newArgs[i];

                iterationCount++;
                if (iterationCount > 100000)
                    throw new ScriptException("尾递归优化检测到可能的无限循环", 0);
            }
            finally
            {
                _localFrames.Pop();
            }
        }
    }

    private Value EvaluateStructInitExpression(BoundStructInitExpression node)
    {
        var instance = new EcsStruct(node.Definition);
        _structInstances.Add(instance);
        return Value.FromStruct(instance);
    }

    private Value EvaluateFieldAccessExpression(BoundFieldAccessExpression node)
    {
        var targetVal = EvaluateExpression(node.Target);
        var instance = targetVal.AsStruct();

        if (node.Field.FieldType is FixedArrayType fat)
        {
            var elemType = fat.ElementType;
            var items = new Value[fat.Count];
            for (int i = 0; i < fat.Count; i++)
            {
                var raw = instance.GetFieldElement(node.Field, i);
                items[i] = RawToValue(raw, elemType);
            }
            return Value.CreateArray(elemType, items);
        }

        if (node.Field.FieldType is StructType)
        {
            var nested = instance.GetNested(node.Field);
            return Value.FromStruct(nested);
        }

        return RawToValue(instance.GetField(node.Field), node.Field.FieldType);
    }

    private void EvaluateFieldAssignment(BoundFieldAssignStatement node)
    {
        var targetVal = EvaluateExpression(node.Target);
        var valueVal = EvaluateExpression(node.Value);
        var instance = targetVal.AsStruct();

        var ft = node.Field.FieldType;
        object fieldValue;

        if (ft.Equals(ScriptType.Byte)) fieldValue = valueVal.AsByte();
        else if (ft.Equals(ScriptType.Int)) fieldValue = valueVal.AsInt();
        else if (ft.Equals(ScriptType.Bool)) fieldValue = valueVal.AsBool();
        else if (ft.Equals(ScriptType.UInt)) fieldValue = valueVal.AsUInt();
        else if (ft.Equals(ScriptType.UInt64)) fieldValue = valueVal.AsUInt64();
        else if (ft.Equals(ScriptType.Ptr)) fieldValue = new IntPtr(valueVal.AsPtr());
        else if (ft.Equals(ScriptType.Double)) fieldValue = valueVal.AsDouble();
        else if (ft.Equals(ScriptType.String)) fieldValue = valueVal.AsString();
        else if (ft is StructType) fieldValue = valueVal.AsStruct();
        else fieldValue = valueVal.AsInt();

        instance.SetField(node.Field, fieldValue);
    }

    private Value EvaluateFieldIndexAccessExpression(BoundFieldIndexAccessExpression node)
    {
        var targetVal = EvaluateExpression(node.Target);
        var instance = targetVal.AsStruct();
        var index = EvaluateExpression(node.Index).AsInt();
        var field = node.Field;
        var elemType = node.Type; // already resolved to element type by binder

        if (elemType is StructType)
        {
            var nested = instance.GetNested(field, index);
            return Value.FromStruct(nested);
        }

        var raw = instance.GetFieldElement(field, index);
        return RawToValue(raw, elemType);
    }

    private void EvaluateFieldIndexAssignment(BoundFieldIndexAssignStatement node)
    {
        var targetVal = EvaluateExpression(node.Target);
        var valueVal = EvaluateExpression(node.Value);
        var index = EvaluateExpression(node.Index).AsInt();
        var instance = targetVal.AsStruct();

        var elemType = TypeLayout.GetElementType(node.Field.FieldType);
        object fieldValue;

        if (elemType.Equals(ScriptType.Byte)) fieldValue = valueVal.AsByte();
        else if (elemType.Equals(ScriptType.Int)) fieldValue = valueVal.AsInt();
        else if (elemType.Equals(ScriptType.Bool)) fieldValue = valueVal.AsBool();
        else if (elemType.Equals(ScriptType.UInt)) fieldValue = valueVal.AsUInt();
        else if (elemType.Equals(ScriptType.UInt64)) fieldValue = valueVal.AsUInt64();
        else if (elemType.Equals(ScriptType.Ptr)) fieldValue = new IntPtr(valueVal.AsPtr());
        else if (elemType.Equals(ScriptType.Double)) fieldValue = valueVal.AsDouble();
        else if (elemType.Equals(ScriptType.String)) fieldValue = valueVal.AsString();
        else if (elemType is StructType) fieldValue = valueVal.AsStruct();
        else fieldValue = valueVal.AsInt();

        instance.SetFieldElement(node.Field, index, fieldValue);
    }

    private void EvaluateIndexAssignment(BoundIndexAssignStatement node)
    {
        var containerVal = EvaluateExpression(node.Container);
        var indexVal = EvaluateExpression(node.Index).AsInt();
        var valueVal = EvaluateExpression(node.Value);

        var newArray = containerVal.SetIndex(indexVal, valueVal);

        // 写回变量槽：找到持有该数组的变量并更新
        if (node.Container is BoundVariableExpression varExpr)
        {
            Assign(varExpr.Variable, newArray);
        }
        else
        {
            throw new InvalidOperationException("数组赋值目标必须是变量");
        }
    }

    private static Value RawToValue(object raw, ScriptType type)
    {
        if (type.Equals(ScriptType.Byte)) return Value.FromByte((byte)raw);
        if (type.Equals(ScriptType.Int)) return Value.FromInt((int)raw);
        if (type.Equals(ScriptType.Bool)) return Value.FromBool((bool)raw);
        if (type.Equals(ScriptType.UInt)) return Value.FromUInt((uint)raw);
        if (type.Equals(ScriptType.UInt64)) return Value.FromUInt64((ulong)raw);
        if (type.Equals(ScriptType.Ptr)) return Value.FromPtr(((IntPtr)raw).ToInt64());
        if (type.Equals(ScriptType.Double)) return Value.FromDouble((double)raw);
        if (type.Equals(ScriptType.String)) return Value.FromString((string)raw);
        if (type is StructType) return Value.FromStruct((EcsStruct)raw);
        return Value.Void;
    }

    public void Dispose()
    {
        foreach (var s in _structInstances)
            s.Dispose();
        _structInstances.Clear();
    }

    private static void CollectGlobalVars(BoundBlockStatement body, List<VariableSymbol> list, HashSet<VariableSymbol> seen)
    {
        foreach (var stmt in body.Statements)
        {
            switch (stmt)
            {
                case BoundVariableDeclaration vd
                    when vd.Variable is GlobalVariableSymbol && seen.Add(vd.Variable):
                    list.Add(vd.Variable);
                    break;
                case BoundBlockStatement inner:
                    CollectGlobalVars(inner, list, seen);
                    break;
            }
        }
    }
}

public sealed class EvaluationResult(ImmutableArray<Diagnostic> diagnostics, Value value)
{
    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
    public Value Result { get; } = value;
}