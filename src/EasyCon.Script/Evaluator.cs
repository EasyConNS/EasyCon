using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using static EasyCon.Script.Binding.BoundNodeKind;

namespace EasyCon.Script;

public class ScriptException(string message, int address) : Exception(message)
{
    public int Address { get; private set; } = address;
}

internal sealed class Evaluator : IEvalContext, IDisposable
{
    private readonly BoundProgram _program;
    private readonly RuntimeHeap _heap = new();
    private readonly Stack<EvalFrame> _localFrames = new();
    private readonly Dictionary<FunctionSymbol, BoundBlockStatement> _functions = [];
    private readonly Dictionary<string, Func<Value>> _runtimeValueGetters = [];

    // 类型化全局存储
    private int[] _globalInts = [];
    private long[] _globalLongs = [];
    private double[] _globalDoubles = [];
    private int[] _globalHandles = [];
    private readonly Dictionary<VariableSymbol, SlotDesc> _globalSlots = [];

    private readonly long _TIME = DateTime.Now.Ticks;
    private readonly Random _rand = new();
    private bool _cancelLineBreak = false;
    private CancellationToken _token;
    private Value _lastValue;
    private int _yieldCounter;
    private Value[]? _tailCallArgsBuffer;

    private readonly Dictionary<FunctionSymbol, ICallable> _callables = [];

    public IOutputAdapter? Output { get; set; }
    public ICGamePad? GamePad { get; set; }

    public OcrDelegate? Ocr { get; set; }
    public FrameDelegate? Frame { get; set; }
    public RoiDelegate? Roi { get; set; }
    public LabelMatchDelegate? LabelMatch { get; set; }

    // IEvalContext
    ICGamePad? IEvalContext.GamePad => GamePad;
    IOutputAdapter? IEvalContext.Output => Output;
    OcrDelegate? IEvalContext.Ocr => Ocr;
    FrameDelegate? IEvalContext.Frame => Frame;
    RoiDelegate? IEvalContext.Roi => Roi;
    LabelMatchDelegate? IEvalContext.LabelMatch => LabelMatch;
    Random IEvalContext.Rand => _rand;
    int IEvalContext.Timestamp => (int)((DateTime.Now.Ticks - _TIME) / 10_000);

    bool IEvalContext.CancelLineBreak { get => _cancelLineBreak; set => _cancelLineBreak = value; }

    public Evaluator(BoundProgram program, CancellationToken token)
    {
        _program = program;
        _token = token;
        _localFrames.Push(new EvalFrame(0, 0, 0, 0));

        foreach (var kv in _program.Functions)
            _functions.Add(kv.Key, kv.Value);

        // 扫描所有函数体，递归收集全局变量并建立类型化索引映射
        var globalVarList = new List<VariableSymbol>();
        var seen = new HashSet<VariableSymbol>();
        foreach (var body in _functions.Values)
            CollectGlobalVars(body, globalVarList, seen);

        int intIdx = 0, longIdx = 0, doubleIdx = 0, handleIdx = 0;
        foreach (var gv in globalVarList)
        {
            var cat = GetSlotCategory(gv.Type);
            var idx = cat switch
            {
                SlotCategory.Int => intIdx++,
                SlotCategory.Long => longIdx++,
                SlotCategory.Double => doubleIdx++,
                SlotCategory.Handle => handleIdx++,
                _ => intIdx++
            };
            _globalSlots[gv] = new SlotDesc(cat, idx);
        }
        _globalInts = new int[intIdx];
        _globalLongs = new long[longIdx];
        _globalDoubles = new double[doubleIdx];
        _globalHandles = new int[handleIdx];

        RegisterCallables();
        RegisterRuntimeValueGetters();
    }

    private void RegisterCallables()
    {
        foreach (var (symbol, callable) in BuiltinCallable.GetAll())
            _callables[symbol] = callable;

        foreach (var fn in _functions.Keys)
        {
            if (_callables.ContainsKey(fn)) continue;
            _callables[fn] = new DelegateCallable((args, ctx, tk) =>
            {
                if (ctx is Evaluator evaluator)
                    return evaluator.EvaluateFunctionBodyWithTailRecursion(fn, args, tk);
                else
                    return ctx.EvaluateFunctionBody(fn);
            });
        }

        if (!_program.ExternFunctions.IsEmpty)
        {
            var loader = new NativeLoader();
            foreach (var (symbol, callable) in loader.RegisterExternFunctions(_program.ExternFunctions))
                _callables[symbol] = callable;
        }
    }

    private void RegisterRuntimeValueGetters()
    {
        _runtimeValueGetters["__TIME__"] = () => ((IEvalContext)this).Timestamp;
    }

    public Value Evaluate()
    {
        var function = _program.MainFunction;
        if (function == null) return Value.Void;

        var body = _functions[function];
        PushFrame(function);
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
        out System.Collections.Immutable.ImmutableArray<Value> tailCallArgs)
    {
        return EvaluateStatementCore(body, tailRecursionTarget, out isTailCall, out tailCallArgs);
    }

    private Value EvaluateStatementCore(
        BoundBlockStatement body,
        FunctionSymbol? tailRecursionTarget,
        out bool isTailCall,
        out System.Collections.Immutable.ImmutableArray<Value> tailCallArgs)
    {
        isTailCall = false;
        tailCallArgs = System.Collections.Immutable.ImmutableArray<Value>.Empty;

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
                            tailCallArgs = System.Collections.Immutable.ImmutableArray.Create(buf, 0, argLen);
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
        if (node.ConstantValue != null)
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
            case RuntimeValue:
                var rv = (BoundRuntimeValueExpression)node;
                if (_runtimeValueGetters.TryGetValue(rv.Name, out var rvGetter))
                    return rvGetter();
                throw new Exception($"找不到运行时变量 \"{rv.Name}\" 的getter");
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
        Debug.Assert(n.ConstantValue != null);
        return Value.From(n.ConstantValue);
    }

    #region 类型化 Slot 读写

    private static SlotCategory GetSlotCategory(ScriptType type)
    {
        if (type.Equals(ScriptType.Bool) || type.Equals(ScriptType.Byte) ||
            type.Equals(ScriptType.Int) || type.Equals(ScriptType.UInt))
            return SlotCategory.Int;
        if (type.Equals(ScriptType.UInt64) || type.Equals(ScriptType.Ptr))
            return SlotCategory.Long;
        if (type.Equals(ScriptType.Double))
            return SlotCategory.Double;
        return SlotCategory.Handle;
    }

    private Value GetValue(VariableSymbol v)
    {
        if (v is GlobalVariableSymbol)
        {
            var desc = _globalSlots[v];
            return ReadGlobalSlot(desc, v.Type);
        }
        var local = (LocalVariableSymbol)v;
        var frame = _localFrames.Peek();
        return ReadSlot(local.Slot, frame, v.Type);
    }

    private Value ReadSlot(SlotDesc desc, EvalFrame frame, ScriptType type) => desc.Category switch
    {
        SlotCategory.Int => ReadIntSlot(frame.Ints[desc.Index], type),
        SlotCategory.Long => ReadLongSlot(frame.Longs[desc.Index], type),
        SlotCategory.Double => Value.FromDouble(frame.Doubles[desc.Index]),
        SlotCategory.Handle => _heap.Deref(frame.Handles[desc.Index], type),
        _ => Value.Void
    };

    private Value ReadGlobalSlot(SlotDesc desc, ScriptType type) => desc.Category switch
    {
        SlotCategory.Int => ReadIntSlot(_globalInts[desc.Index], type),
        SlotCategory.Long => ReadLongSlot(_globalLongs[desc.Index], type),
        SlotCategory.Double => Value.FromDouble(_globalDoubles[desc.Index]),
        SlotCategory.Handle => _heap.Deref(_globalHandles[desc.Index], type),
        _ => Value.Void
    };

    private static Value ReadIntSlot(int val, ScriptType type)
    {
        if (type.Equals(ScriptType.Bool)) return Value.FromBool(val != 0);
        if (type.Equals(ScriptType.Byte)) return Value.FromByte((byte)val);
        if (type.Equals(ScriptType.UInt)) return Value.FromUInt(unchecked((uint)val));
        return Value.FromInt(val);
    }

    private static Value ReadLongSlot(long val, ScriptType type)
    {
        if (type.Equals(ScriptType.UInt64)) return Value.FromUInt64((ulong)val);
        return Value.FromPtr(val);
    }

    private void Assign(VariableSymbol variable, Value value)
    {
        if (variable is GlobalVariableSymbol)
        {
            var desc = _globalSlots[variable];
            WriteGlobalSlot(desc, variable.Type, value);
            return;
        }
        var local = (LocalVariableSymbol)variable;
        var frame = _localFrames.Peek();
        WriteSlot(local.Slot, frame, variable.Type, value);
    }

    private void WriteSlot(SlotDesc desc, EvalFrame frame, ScriptType type, Value value)
    {
        switch (desc.Category)
        {
            case SlotCategory.Int:
                frame.Ints[desc.Index] = WriteIntSlot(type, value);
                break;
            case SlotCategory.Long:
                frame.Longs[desc.Index] = type.Equals(ScriptType.UInt64)
                    ? (long)value.AsUInt64()
                    : value.AsPtr();
                break;
            case SlotCategory.Double:
                frame.Doubles[desc.Index] = value.AsDouble();
                break;
            case SlotCategory.Handle:
                if (frame.Handles[desc.Index] != 0)
                    _heap.Free(frame.Handles[desc.Index]);
                frame.Handles[desc.Index] = StoreHandle(type, value);
                break;
        }
    }

    private void WriteGlobalSlot(SlotDesc desc, ScriptType type, Value value)
    {
        switch (desc.Category)
        {
            case SlotCategory.Int:
                _globalInts[desc.Index] = WriteIntSlot(type, value);
                break;
            case SlotCategory.Long:
                _globalLongs[desc.Index] = type.Equals(ScriptType.UInt64)
                    ? (long)value.AsUInt64()
                    : value.AsPtr();
                break;
            case SlotCategory.Double:
                _globalDoubles[desc.Index] = value.AsDouble();
                break;
            case SlotCategory.Handle:
                if (_globalHandles[desc.Index] != 0)
                    _heap.Free(_globalHandles[desc.Index]);
                _globalHandles[desc.Index] = StoreHandle(type, value);
                break;
        }
    }

    private static int WriteIntSlot(ScriptType type, Value value)
    {
        if (type.Equals(ScriptType.Bool)) return value.AsBool() ? 1 : 0;
        if (type.Equals(ScriptType.Byte)) return value.AsByte();
        if (type.Equals(ScriptType.UInt)) return unchecked((int)value.AsUInt());
        return value.AsInt();
    }

    private int StoreHandle(ScriptType type, Value value)
    {
        if (type.Equals(ScriptType.String))
            return _heap.StoreString(value.AsString());
        if (type is ArrayType)
            return _heap.StoreArray(value.AsArray().Clone());
        if (type is StructType)
        {
            var src = value.AsStruct();
            var clone = new EcsStruct(src.Definition, src.NativePtr);
            return _heap.StoreStruct(clone);
        }
        throw new InvalidOperationException($"不支持 handle 存储: {type}");
    }

    private int GetHandle(VariableSymbol variable)
    {
        if (variable is GlobalVariableSymbol)
        {
            var desc = _globalSlots[variable];
            return desc.Category == SlotCategory.Handle ? _globalHandles[desc.Index] : 0;
        }
        var local = (LocalVariableSymbol)variable;
        var frame = _localFrames.Peek();
        return local.Slot.Category == SlotCategory.Handle ? frame.Handles[local.Slot.Index] : 0;
    }

    #endregion

    private Value EvaluateVariableExpression(BoundVariableExpression v)
    {
        return GetValue(v.Variable);
    }

    private Value EvaluateIndexDeclExpression(BoundIndexDeclxpression decl)
    {
        var elements = decl.Items.Select(EvaluateExpression);
        var elemType = ((ArrayType)decl.Type).ElementType;
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
            return callable.Invoke(ReadOnlySpan<Value>.Empty, this, _token);
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
            return callable.Invoke(args.AsSpan(0, argCount), this, _token);
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
                GamePad?.ReleaseButtons(node.Act);
            else
                GamePad?.PressButtons(node.Act);
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

    // IEvalContext 方法
    public Value EvaluateFunctionBody(FunctionSymbol function)
    {
        var body = _functions[function];
        return EvaluateStatement(body);
    }

    private void PushFrame(FunctionSymbol function)
    {
        var layout = function.Layout;
        _localFrames.Push(new EvalFrame(layout.IntSlots, layout.LongSlots, layout.DoubleSlots, layout.HandleSlots));
    }

    private void PopFrame()
    {
        var frame = _localFrames.Pop();
        _heap.FreeAll(frame.Handles);
    }

    /// <summary>
    /// 使用尾递归优化执行函数体
    /// </summary>
    public Value EvaluateFunctionBodyWithTailRecursion(FunctionSymbol function, ReadOnlySpan<Value> args, CancellationToken token)
    {
        var body = _functions[function];
        var layout = function.Layout;
        var frame = new EvalFrame(layout.IntSlots, layout.LongSlots, layout.DoubleSlots, layout.HandleSlots);

        for (int i = 0; i < args.Length; i++)
        {
            var param = function.Parameters[i];
            WriteSlot(param.Slot, frame, param.Type, args[i]);
        }

        int iterationCount = 0;
        while (true)
        {
            if (token.IsCancellationRequested)
                throw new OperationCanceledException(token);

            _localFrames.Push(frame);

            try
            {
                var result = EvaluateStatement(body, function, out bool isTailCall, out System.Collections.Immutable.ImmutableArray<Value> newArgs);

                if (!isTailCall)
                    return result;

                // 释放旧 handle，写入新参数
                _heap.FreeAll(frame.Handles);
                Array.Clear(frame.Handles);

                for (int i = 0; i < newArgs.Length; i++)
                {
                    var param = function.Parameters[i];
                    WriteSlot(param.Slot, frame, param.Type, newArgs[i]);
                }

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
        return Value.FromStruct(instance);
    }

    private Value EvaluateFieldAccessExpression(BoundFieldAccessExpression node)
    {
        var targetVal = EvaluateExpression(node.Target);
        var instance = targetVal.AsStruct();

        if (node.Field.FieldType is ArrayType arrType)
        {
            var elemType = arrType.ElementType;
            var count = arrType.Count;
            var items = new Value[count];
            for (int i = 0; i < count; i++)
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

    private Value EvaluateFieldIndexAccessExpression(BoundFieldIndexAccessExpression node)
    {
        var targetVal = EvaluateExpression(node.Target);
        var instance = targetVal.AsStruct();
        var index = EvaluateExpression(node.Index).AsInt();
        var field = node.Field;
        var elemType = node.Type;

        if (elemType is StructType)
        {
            var nested = instance.GetNested(field, index);
            return Value.FromStruct(nested);
        }

        var raw = instance.GetFieldElement(field, index);
        return RawToValue(raw, elemType);
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

        // 原地修改：直接通过 handle 修改数组，不需要写回
        if (node.Container is BoundVariableExpression varExpr)
        {
            var handle = GetHandle(varExpr.Variable);
            if (handle != 0)
            {
                var array = _heap.GetArray(handle);
                array.SetItem(indexVal, valueVal);
                return;
            }
        }

        // fallback（非 handle 路径，如 struct 字段中的数组等）
        containerVal.SetIndex(indexVal, valueVal);
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
        // 释放全局 handle
        _heap.FreeAll(_globalHandles);
        // 释放帧 handle（如果还有未弹出的帧）
        foreach (var frame in _localFrames)
            _heap.FreeAll(frame.Handles);
        _localFrames.Clear();
        // 释放堆中所有剩余数据
        _heap.FreeAll();
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

public sealed class EvaluationResult(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics, Value value)
{
    public System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
    public Value Result { get; } = value;
}