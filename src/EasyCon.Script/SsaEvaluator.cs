using EasyCon.Script.Binding;
using EasyCon.Script.Binding.Ssa;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EasyCon.Script;

/// <summary>
/// 消费 SSA IR 的执行器。
/// 直接遍历 SsaBlock + SsaValue，无需 Bound 树的递归表达式求值。
/// </summary>
internal sealed class SsaEvaluator : IEvalContext, IDisposable
{
    private readonly SsaProgram _program;
    private readonly RuntimeHeap _heap = new();
    private readonly Stack<EvalFrame> _localFrames = new();
    private readonly Dictionary<FunctionSymbol, SsaFunction> _functions = [];
    private readonly Dictionary<FunctionSymbol, ICallable> _callables = [];

    // 尾调用优化状态
    private SsaFunction? _currentFunc;
    private bool _tailCallRequested;
    private Value[] _tailCallArgs = [];
    private readonly Dictionary<string, Func<Value>> _runtimeValueGetters = [];

    // 类型化全局存储
    private int[] _globalInts = [];
    private long[] _globalLongs = [];
    private double[] _globalDoubles = [];
    private int[] _globalHandles = [];
    private readonly Dictionary<VariableSymbol, SlotDesc> _globalSlots = [];

    // SSA 值缓存：SsaValue.Id → 运行时结果
    private Value[] _valueCache = [];

    private readonly long _TIME = DateTime.Now.Ticks;
    private readonly Random _rand = new();
    private bool _cancelLineBreak = false;
    private CancellationToken _token;
    private Value _lastValue;
    private int _yieldCounter;

    // IEvalContext
    public IOutputAdapter? Output { get; set; }
    public ICGamePad? GamePad { get; set; }
    public OcrDelegate? Ocr { get; set; }
    public FrameDelegate? Frame { get; set; }
    public RoiDelegate? Roi { get; set; }
    public LabelMatchDelegate? LabelMatch { get; set; }

    ICGamePad? IEvalContext.GamePad => GamePad;
    IOutputAdapter? IEvalContext.Output => Output;
    OcrDelegate? IEvalContext.Ocr => Ocr;
    FrameDelegate? IEvalContext.Frame => Frame;
    RoiDelegate? IEvalContext.Roi => Roi;
    LabelMatchDelegate? IEvalContext.LabelMatch => LabelMatch;
    Random IEvalContext.Rand => _rand;
    int IEvalContext.Timestamp => (int)((DateTime.Now.Ticks - _TIME) / 10_000);
    bool IEvalContext.CancelLineBreak { get => _cancelLineBreak; set => _cancelLineBreak = value; }

    public SsaEvaluator(SsaProgram program, CancellationToken token)
    {
        _program = program;
        _token = token;
        _localFrames.Push(new EvalFrame(0, 0, 0, 0));

        foreach (var kv in _program.Functions)
            _functions.Add(kv.Key, kv.Value);

        // 收集全局变量
        var globalVarList = new List<VariableSymbol>();
        var seen = new HashSet<VariableSymbol>();
        foreach (var func in _functions.Values)
            CollectGlobalVars(func, globalVarList, seen);

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

        // 预分配 valueCache
        int maxId = 0;
        foreach (var func in _functions.Values)
            foreach (var block in func.Blocks)
                foreach (var val in block.Instructions.Concat(block.Phis))
                    if (val.Id > maxId) maxId = val.Id;
        _valueCache = new Value[maxId + 1];
    }

    private void RegisterCallables()
    {
        foreach (var (symbol, callable) in BuiltinCallable.GetAll())
            _callables[symbol] = callable;

        foreach (var (symbol, callable) in BuiltinCallable.GetCaptureHoleCallables())
            _callables[symbol] = callable;

        foreach (var fn in _functions.Keys)
        {
            if (_callables.ContainsKey(fn)) continue;
            _callables[fn] = new DelegateCallable((args, ctx, tk) =>
            {
                if (ctx is SsaEvaluator eval)
                    return eval.EvaluateFunctionWithTailRecursion(fn, args, tk);
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
        var func = _program.MainFunction;
        if (func == null) return Value.Void;

        PushFrame(func.Symbol);
        try
        {
            var result = EvaluateFunction(func);
            return result;
        }
        finally
        {
            PopFrame();
        }
    }

    // ============ 主执行循环 ============

    private Value EvaluateFunction(SsaFunction func)
    {
        var prevFunc = _currentFunc;
        _currentFunc = func;
        try
        {
            var frame = _localFrames.Peek();
            SsaBlock current = func.Entry;
            SsaBlock? lastBlock = null;

            while (!_token.IsCancellationRequested)
            {
                // 1. 执行 Phi 节点
                foreach (var phi in current.Phis)
                {
                    int armIdx = FindPredecessorIndex(current, lastBlock);
                    Debug.Assert(phi.ExtraArgs != null && armIdx < phi.ExtraArgs.Count);
                    var val = _valueCache[phi.ExtraArgs[armIdx].Id];
                    WriteToSlot(phi, val, frame);
                    _valueCache[phi.Id] = val;
                }

                // 2. 执行普通指令
                int instCount = current.Instructions.Count;
                for (int i = 0; i < instCount; i++)
                {
                    var inst = current.Instructions[i];

                    // 尾调用前截：Call 紧跟 Return，且目标是当前函数 → 跳过实际调用，直接更新帧
                    if (inst.Op == SsaOp.Call && inst.Aux is FunctionSymbol called
                        && called == func.Symbol
                        && i + 1 < instCount
                        && current.Instructions[i + 1].Op == SsaOp.Return
                        && current.Instructions[i + 1].Arg0 == inst)
                    {
                        // 收集参数值（不直接写帧，由 EvaluateFunctionWithTailRecursion 负责释放旧帧再写入）
                        var argCount = (inst.Arg0 != null ? 1 : 0) + (inst.ExtraArgs?.Count ?? 0);
                        if (argCount > 0)
                        {
                            if (_tailCallArgs.Length < argCount) _tailCallArgs = new Value[argCount];
                            int ai = 0;
                            if (inst.Arg0 != null) _tailCallArgs[ai++] = _valueCache[inst.Arg0.Id];
                            if (inst.ExtraArgs != null)
                                foreach (var ea in inst.ExtraArgs)
                                    _tailCallArgs[ai++] = _valueCache[ea.Id];
                        }
                        _tailCallRequested = true;
                        return Value.Void;
                    }

                    // LoadLocal/LoadGlobal 需要每次重新读取帧（循环头可能重新执行，帧已被 body 更新）
                    if (inst.Op is SsaOp.LoadLocal or SsaOp.LoadGlobal)
                    {
                        _valueCache[inst.Id] = ExecuteValue(inst, frame);
                    }
                    else
                    {
                        var result = ExecuteValue(inst, frame);
                        _valueCache[inst.Id] = result;
                        // 跟踪最后一个非 Void 指令结果（供无参 Return 使用）
                        if (result.Type != ScriptType.Void)
                            _lastValue = result;
                    }
                }

                // 3. 跳转
                lastBlock = current;

                if (current.IsReturn)
                {
                    // 找到最后一个 Return 指令的值
                    for (int i = current.Instructions.Count - 1; i >= 0; i--)
                        if (current.Instructions[i].Op == SsaOp.Return)
                            return _valueCache[current.Instructions[i].Id];
                    return _lastValue;
                }

                if (current.BranchCondition != null)
                {
                    var cond = _valueCache[current.BranchCondition.Id];
                    current = cond.AsBool()
                        ? current.TrueSuccessor!
                        : current.FalseSuccessor!;
                }
                else if (current.JumpTarget != null)
                {
                    current = current.JumpTarget;
                }
                else
                {
                    throw new ScriptException("块无终止指令", current.Id);
                }

                YieldIfDue();
            }

            throw new OperationCanceledException(_token);
        }
        finally
        {
            _currentFunc = prevFunc;
        }
    }

    private static int FindPredecessorIndex(SsaBlock block, SsaBlock? last)
    {
        if (last == null) return 0;
        for (int i = 0; i < block.Predecessors.Count; i++)
            if (block.Predecessors[i] == last) return i;
        return 0;
    }

    private void YieldIfDue()
    {
        if (++_yieldCounter >= 1000)
        {
            _yieldCounter = 0;
            Thread.Yield();
        }
    }

    // ============ 指令执行 ============

    private Value ExecuteValue(SsaValue val, EvalFrame frame)
    {
        return val.Op switch
        {
            // ---- 常量 ----
            SsaOp.ConstBool => Value.FromBool(val.Const.GetBool()),
            SsaOp.ConstByte => Value.FromByte(val.Const.GetByte()),
            SsaOp.ConstInt => Value.FromInt(val.Const.GetInt()),
            SsaOp.ConstUInt => Value.FromUInt(val.Const.GetUInt()),
            SsaOp.ConstUInt64 => Value.FromUInt64(val.Const.GetUInt64()),
            SsaOp.ConstDouble => Value.FromDouble(val.Const.GetDouble()),
            SsaOp.ConstString => Value.FromString(val.ConstString!),
            SsaOp.ConstPtr => Value.FromPtr(val.Const.GetPtr()),

            // ---- 加载/存储 ----
            SsaOp.LoadLocal => ReadSlot(((LocalVariableSymbol)val.Aux!).Slot, frame, val.Type),
            SsaOp.StoreLocal => StoreLocal(val, frame),
            SsaOp.LoadGlobal => ReadGlobalSlot(_globalSlots[(VariableSymbol)val.Aux!], val.Type),
            SsaOp.StoreGlobal => StoreGlobal(val),

            // ---- 算术 (int) ----
            SsaOp.AddInt => Value.FromInt(V0i(val) + V1i(val)),
            SsaOp.SubInt => Value.FromInt(V0i(val) - V1i(val)),
            SsaOp.MulInt => Value.FromInt(V0i(val) * V1i(val)),
            SsaOp.DivInt => Value.FromInt(V0i(val) / V1i(val)),
            SsaOp.ModInt => Value.FromInt(V0i(val) % V1i(val)),
            SsaOp.RoundDivInt => Value.FromInt(RoundDiv(V0i(val), V1i(val))),

            // ---- 算术 (uint) ----
            SsaOp.AddUInt => Value.FromUInt(unchecked((uint)((long)V0u(val) + V1u(val)))),
            SsaOp.SubUInt => Value.FromUInt(unchecked((uint)((long)V0u(val) - V1u(val)))),
            SsaOp.MulUInt => Value.FromUInt(unchecked((uint)((long)V0u(val) * V1u(val)))),
            SsaOp.DivUInt => Value.FromUInt(V0u(val) / V1u(val)),
            SsaOp.ModUInt => Value.FromUInt(V0u(val) % V1u(val)),

            // ---- 算术 (double) ----
            SsaOp.AddDouble => Value.FromDouble(V0d(val) + V1d(val)),
            SsaOp.SubDouble => Value.FromDouble(V0d(val) - V1d(val)),
            SsaOp.MulDouble => Value.FromDouble(V0d(val) * V1d(val)),
            SsaOp.DivDouble => Value.FromDouble(V0d(val) / V1d(val)),

            // ---- 算术 (uint64) ----
            SsaOp.AddUInt64 => Value.FromUInt64(V0u64(val) + V1u64(val)),
            SsaOp.SubUInt64 => Value.FromUInt64(V0u64(val) - V1u64(val)),
            SsaOp.MulUInt64 => Value.FromUInt64(V0u64(val) * V1u64(val)),
            SsaOp.DivUInt64 => Value.FromUInt64(V0u64(val) / V1u64(val)),
            SsaOp.ModUInt64 => Value.FromUInt64(V0u64(val) % V1u64(val)),

            // ---- 位运算 ----
            SsaOp.AndInt => Value.FromInt(V0i(val) & V1i(val)),
            SsaOp.OrInt => Value.FromInt(V0i(val) | V1i(val)),
            SsaOp.XorInt => Value.FromInt(V0i(val) ^ V1i(val)),
            SsaOp.ShlInt => Value.FromInt(V0i(val) << V1i(val)),
            SsaOp.ShrInt => Value.FromInt(V0i(val) >> V1i(val)),
            SsaOp.NotInt => Value.FromInt(~V0i(val)),

            // ---- 比较 (int) ----
            SsaOp.EqInt => Value.FromBool(V0i(val) == V1i(val)),
            SsaOp.NeqInt => Value.FromBool(V0i(val) != V1i(val)),
            SsaOp.LtInt => Value.FromBool(V0i(val) < V1i(val)),
            SsaOp.LeqInt => Value.FromBool(V0i(val) <= V1i(val)),
            SsaOp.GtInt => Value.FromBool(V0i(val) > V1i(val)),
            SsaOp.GeqInt => Value.FromBool(V0i(val) >= V1i(val)),

            // ---- 比较 (uint) ----
            SsaOp.EqUInt => Value.FromBool(V0u(val) == V1u(val)),
            SsaOp.NeqUInt => Value.FromBool(V0u(val) != V1u(val)),
            SsaOp.LtUInt => Value.FromBool(V0u(val) < V1u(val)),
            SsaOp.LeqUInt => Value.FromBool(V0u(val) <= V1u(val)),
            SsaOp.GtUInt => Value.FromBool(V0u(val) > V1u(val)),
            SsaOp.GeqUInt => Value.FromBool(V0u(val) >= V1u(val)),

            // ---- 比较 (double) ----
            SsaOp.EqDouble => Value.FromBool(V0d(val) == V1d(val)),
            SsaOp.NeqDouble => Value.FromBool(V0d(val) != V1d(val)),
            SsaOp.LtDouble => Value.FromBool(V0d(val) < V1d(val)),
            SsaOp.LeqDouble => Value.FromBool(V0d(val) <= V1d(val)),
            SsaOp.GtDouble => Value.FromBool(V0d(val) > V1d(val)),
            SsaOp.GeqDouble => Value.FromBool(V0d(val) >= V1d(val)),

            // ---- 比较 (uint64) ----
            SsaOp.EqUInt64 => Value.FromBool(V0u64(val) == V1u64(val)),
            SsaOp.NeqUInt64 => Value.FromBool(V0u64(val) != V1u64(val)),
            SsaOp.LtUInt64 => Value.FromBool(V0u64(val) < V1u64(val)),
            SsaOp.LeqUInt64 => Value.FromBool(V0u64(val) <= V1u64(val)),
            SsaOp.GtUInt64 => Value.FromBool(V0u64(val) > V1u64(val)),
            SsaOp.GeqUInt64 => Value.FromBool(V0u64(val) >= V1u64(val)),

            // ---- 比较 (bool/string/ptr/byte) ----
            SsaOp.EqBool => Value.FromBool(V0b(val) == V1b(val)),
            SsaOp.NeqBool => Value.FromBool(V0b(val) != V1b(val)),
            SsaOp.EqString => Value.FromBool(V0s(val) == V1s(val)),
            SsaOp.NeqString => Value.FromBool(V0s(val) != V1s(val)),
            SsaOp.EqPtr => Value.FromBool(V0p(val) == V1p(val)),
            SsaOp.NeqPtr => Value.FromBool(V0p(val) != V1p(val)),
            SsaOp.EqByte => Value.FromBool(V0by(val) == V1by(val)),
            SsaOp.NeqByte => Value.FromBool(V0by(val) != V1by(val)),
            SsaOp.LtByte => Value.FromBool(V0by(val) < V1by(val)),
            SsaOp.LeqByte => Value.FromBool(V0by(val) <= V1by(val)),
            SsaOp.GtByte => Value.FromBool(V0by(val) > V1by(val)),
            SsaOp.GeqByte => Value.FromBool(V0by(val) >= V1by(val)),

            // ---- 逻辑 ----
            SsaOp.LogicNot => Value.FromBool(!V0b(val)),

            // ---- 类型转换 ----
            SsaOp.ConvBoolToInt => Value.FromInt(V0b(val) ? 1 : 0),
            SsaOp.ConvByteToInt => Value.FromInt(V0by(val)),
            SsaOp.ConvIntToUInt => Value.FromUInt(unchecked((uint)V0i(val))),
            SsaOp.ConvIntToUInt64 => Value.FromUInt64((ulong)V0i(val)),
            SsaOp.ConvIntToDouble => Value.FromDouble(V0i(val)),
            SsaOp.ConvIntToByte => Value.FromByte((byte)V0i(val)),
            SsaOp.ConvUIntToUInt64 => Value.FromUInt64(V0u(val)),
            SsaOp.ConvUInt64ToPtr => Value.FromPtr((long)V0u64(val)),
            SsaOp.ConvPtrToInt => Value.FromInt((int)V0p(val)),
            SsaOp.ConvDoubleToInt => Value.FromInt((int)V0d(val)),
            SsaOp.ConvToString => Value.FromString(V0(val).ToString()),
            SsaOp.ConvUInt64ToInt => Value.FromInt(unchecked((int)V0u64(val))),
            SsaOp.ConvToInt => V0(val).ToInt(),
            SsaOp.ArrayAppend => V0(val).Append(V1(val)),

            // ---- 控制流（Phi 在主循环中处理） ----
            SsaOp.Phi => Value.Void,
            SsaOp.CondBranch => Value.Void,
            SsaOp.Branch => Value.Void,
            SsaOp.Return => HandleReturn(val),

            // ---- 调用 ----
            SsaOp.Call => ExecuteCall(val),

            // ---- 复合数据 ----
            SsaOp.ArrayInit => ExecuteArrayInit(val),
            SsaOp.LoadIndex => ExecuteLoadIndex(val),
            SsaOp.StoreIndex => ExecuteStoreIndex(val),
            SsaOp.Slice => ExecuteSlice(val),
            SsaOp.ArrayLen => Value.FromInt(V0(val).Length),
            SsaOp.Contains => Value.FromBool(V1(val).Contains(V0(val))),
            SsaOp.Concat => ExecuteConcat(val),
            SsaOp.DeepCopy => ExecuteDeepCopy(val, frame),

            // ---- 结构体 ----
            SsaOp.StructInit => Value.FromStruct(new EcsStruct(((StructType)val.Type).Definition)),
            SsaOp.LoadField => ExecuteLoadField(val),
            SsaOp.StoreField => ExecuteStoreField(val),
            SsaOp.LoadFieldIndex => ExecuteLoadFieldIndex(val),
            SsaOp.StoreFieldIndex => ExecuteStoreFieldIndex(val),

            // ---- 领域操作 ----
            SsaOp.KeyAction => ExecuteKeyAction(val),
            SsaOp.KeyPress => ExecuteKeyPress(val),
            SsaOp.StickAction => ExecuteStickAction(val),
            SsaOp.StickPress => ExecuteStickPress(val),
            SsaOp.Wait => ExecuteWait(val),

            // ---- 采集卡打洞函数 ----
            SsaOp.Capture => ExecuteCapture(val),
            SsaOp.Ocr => ExecuteOcr(val),
            SsaOp.Roi => ExecuteRoi(val),

            // ---- 运行时 ----
            SsaOp.RuntimeValue => ExecuteRuntimeValue(val),
            SsaOp.ImageLabel => ExecuteImageLabel(val),

            SsaOp.Nop => Value.Void,
            _ => throw new InvalidOperationException($"未实现的 SsaOp: {val.Op}")
        };
    }

    // ============ 值缓存访问器 ============

    private Value V0(SsaValue v) => _valueCache[v.Arg0!.Id];
    private Value V1(SsaValue v) => _valueCache[v.Arg1!.Id];
    private int V0i(SsaValue v) => _valueCache[v.Arg0!.Id].AsInt();
    private int V1i(SsaValue v) => _valueCache[v.Arg1!.Id].AsInt();
    private uint V0u(SsaValue v) => _valueCache[v.Arg0!.Id].AsUInt();
    private uint V1u(SsaValue v) => _valueCache[v.Arg1!.Id].AsUInt();
    private double V0d(SsaValue v) => _valueCache[v.Arg0!.Id].AsDouble();
    private double V1d(SsaValue v) => _valueCache[v.Arg1!.Id].AsDouble();
    private ulong V0u64(SsaValue v) => _valueCache[v.Arg0!.Id].AsUInt64();
    private ulong V1u64(SsaValue v) => _valueCache[v.Arg1!.Id].AsUInt64();
    private bool V0b(SsaValue v) => _valueCache[v.Arg0!.Id].AsBool();
    private bool V1b(SsaValue v) => _valueCache[v.Arg1!.Id].AsBool();
    private byte V0by(SsaValue v) => _valueCache[v.Arg0!.Id].AsByte();
    private byte V1by(SsaValue v) => _valueCache[v.Arg1!.Id].AsByte();
    private string V0s(SsaValue v) => _valueCache[v.Arg0!.Id].AsString();
    private string V1s(SsaValue v) => _valueCache[v.Arg1!.Id].AsString();
    private long V0p(SsaValue v) => _valueCache[v.Arg0!.Id].AsPtr();
    private long V1p(SsaValue v) => _valueCache[v.Arg1!.Id].AsPtr();

    // ============ 辅助执行方法 ============

    private Value StoreLocal(SsaValue val, EvalFrame frame)
    {
        var variable = (LocalVariableSymbol)val.Aux!;
        var value = _valueCache[val.Arg0!.Id];
        WriteSlot(variable.Slot, frame, variable.Type, value);
        return Value.Void;
    }

    private Value StoreGlobal(SsaValue val)
    {
        var variable = (VariableSymbol)val.Aux!;
        var argVal = val.Arg0 != null ? _valueCache[val.Arg0.Id] : Value.Void;
        WriteGlobalSlot(_globalSlots[variable], variable.Type, argVal);
        return Value.Void;
    }

    private Value HandleReturn(SsaValue val)
    {
        // 尾调用检测：块末尾的 RETURN f(...) 且 f == 当前函数
        // 必须是块的最后一条 Return，且前一条指令是 Call
        if (_currentFunc != null && val.Arg0 is { Op: SsaOp.Call, Aux: FunctionSymbol called }
            && called == _currentFunc.Symbol)
        {
            // 验证这是块末尾的 Call→Return 模式
            var block = val.Block;
            var insts = block.Instructions;
            int retIdx = insts.Count - 1;
            if (retIdx >= 1 && insts[retIdx] == val && insts[retIdx - 1] == val.Arg0)
            {
                // 更新帧参数
                var frame = _localFrames.Peek();
                var callVal = val.Arg0;
                if (callVal.Arg0 != null)
                {
                    var p0 = called.Parameters[0];
                    WriteSlot(p0.Slot, frame, p0.Type, _valueCache[callVal.Arg0.Id]);
                }
                if (callVal.ExtraArgs != null)
                {
                    for (int i = 0; i < callVal.ExtraArgs.Count; i++)
                    {
                        var p = called.Parameters[i + 1];
                        WriteSlot(p.Slot, frame, p.Type, _valueCache[callVal.ExtraArgs[i].Id]);
                    }
                }
                _tailCallRequested = true;
                return Value.Void;
            }
        }

        return val.Arg0 != null ? _valueCache[val.Arg0.Id] : _lastValue;
    }

    private Value ExecuteCall(SsaValue val)
    {
        var function = (FunctionSymbol)val.Aux!;

        // 收集参数
        var argCount = 0;
        if (val.Arg0 != null) argCount = 1;
        if (val.ExtraArgs != null) argCount += val.ExtraArgs.Count;

        if (argCount == 0)
        {
            return _callables[function].Invoke(ReadOnlySpan<Value>.Empty, this, _token);
        }

        Value[]? rented = null;
        var args = argCount <= 8
            ? (rented = ArrayPool<Value>.Shared.Rent(argCount))
            : new Value[argCount];
        try
        {
            if (val.Arg0 != null) args[0] = _valueCache[val.Arg0.Id];
            if (val.ExtraArgs != null)
                for (int i = 0; i < val.ExtraArgs.Count; i++)
                    args[i + 1] = _valueCache[val.ExtraArgs[i].Id];

            return _callables[function].Invoke(args.AsSpan(0, argCount), this, _token);
        }
        finally
        {
            if (rented != null) ArrayPool<Value>.Shared.Return(rented);
        }
    }

    private Value ExecuteArrayInit(SsaValue val)
    {
        var items = new List<Value>();
        if (val.Arg0 != null)
        {
            items.Add(_valueCache[val.Arg0.Id]);
            if (val.ExtraArgs != null)
                foreach (var arg in val.ExtraArgs)
                    items.Add(_valueCache[arg.Id]);
        }
        var elemType = ((ArrayType)val.Type).ElementType;
        return Value.CreateArray(elemType, items);
    }

    private Value ExecuteLoadIndex(SsaValue val)
    {
        var container = _valueCache[val.Arg0!.Id];
        var index = _valueCache[val.Arg1!.Id].AsInt();
        if (index < 0 || index >= container.Length) throw new Exception("数组下标越界");
        return container[index];
    }

    private Value ExecuteStoreIndex(SsaValue val)
    {
        var container = _valueCache[val.Arg0!.Id];
        var index = _valueCache[val.Arg1!.Id].AsInt();
        var value = _valueCache[val.ExtraArgs![0].Id];

        // 原地修改
        if (val.Arg0 is { Op: SsaOp.LoadLocal, Aux: LocalVariableSymbol local })
        {
            var frame = _localFrames.Peek();
            var handle = local.Slot.Category == SlotCategory.Handle ? frame.Handles[local.Slot.Index] : 0;
            if (handle != 0)
            {
                _heap.GetArray(handle).SetItem(index, value);
                return Value.Void;
            }
        }
        else if (val.Arg0 is { Op: SsaOp.LoadGlobal, Aux: GlobalVariableSymbol gv })
        {
            var desc = _globalSlots[gv];
            var handle = desc.Category == SlotCategory.Handle ? _globalHandles[desc.Index] : 0;
            if (handle != 0)
            {
                _heap.GetArray(handle).SetItem(index, value);
                return Value.Void;
            }
        }
        container.SetIndex(index, value);
        return Value.Void;
    }

    private Value ExecuteSlice(SsaValue val)
    {
        var target = _valueCache[val.Arg0!.Id];
        var start = _valueCache[val.Arg1!.Id].AsInt();
        var endVal = _valueCache[val.ExtraArgs![0].Id];
        var endIdx = endVal.Type == ScriptType.Int ? endVal.AsInt() : target.Length;
        if (start >= target.Length || endIdx > target.Length || start > endIdx)
            throw new Exception("数组下标越界");
        return target[start..endIdx];
    }

    private Value ExecuteConcat(SsaValue val)
    {
        var left = _valueCache[val.Arg0!.Id];
        var right = _valueCache[val.Arg1!.Id];
        // 字符串拼接：任一侧为 string 时，双侧 ToString 后拼接（与 Value.operator & 行为一致）
        if (left.Type.Equals(ScriptType.String) || right.Type.Equals(ScriptType.String))
            return Value.FromString(left.ToString() + right.ToString());
        return left.Concat(right);
    }

    private Value ExecuteDeepCopy(SsaValue val, EvalFrame frame)
    {
        var src = _valueCache[val.Arg0!.Id];
        if (src.Type.Equals(ScriptType.String))
            return Value.FromString(src.AsString());
        return src;
    }

    private Value ExecuteLoadField(SsaValue val)
    {
        var target = _valueCache[val.Arg0!.Id];
        var instance = target.AsStruct();
        var field = (EcsFieldDef)val.Aux!;

        if (field.FieldType is ArrayType arrType)
        {
            var items = new Value[arrType.Count];
            for (int i = 0; i < arrType.Count; i++)
                items[i] = RawToValue(instance.GetFieldElement(field, i), arrType.ElementType);
            return Value.CreateArray(arrType.ElementType, items);
        }
        if (field.FieldType is StructType)
            return Value.FromStruct(instance.GetNested(field));
        return RawToValue(instance.GetField(field), field.FieldType);
    }

    private Value ExecuteStoreField(SsaValue val)
    {
        var target = _valueCache[val.Arg0!.Id];
        var value = _valueCache[val.Arg1!.Id];
        var instance = target.AsStruct();
        var field = (EcsFieldDef)val.Aux!;
        instance.SetField(field, ValueToRaw(value, field.FieldType));
        return Value.Void;
    }

    private Value ExecuteLoadFieldIndex(SsaValue val)
    {
        var target = _valueCache[val.Arg0!.Id];
        var index = _valueCache[val.Arg1!.Id].AsInt();
        var instance = target.AsStruct();
        var field = (EcsFieldDef)val.Aux!;
        var elemType = val.Type;

        if (elemType is StructType)
            return Value.FromStruct(instance.GetNested(field, index));
        return RawToValue(instance.GetFieldElement(field, index), elemType);
    }

    private Value ExecuteStoreFieldIndex(SsaValue val)
    {
        var target = _valueCache[val.Arg0!.Id];
        var index = _valueCache[val.Arg1!.Id].AsInt();
        var value = _valueCache[val.ExtraArgs![0].Id];
        var instance = target.AsStruct();
        var field = (EcsFieldDef)val.Aux!;
        instance.SetFieldElement(field, index, ValueToRaw(value, TypeLayout.GetElementType(field.FieldType)));
        return Value.Void;
    }

    private Value ExecuteKeyAction(SsaValue val)
    {
        var key = ((GamePadKeySymbol)val.Aux!).Key;
        if (val.Const.GetBool())
            GamePad?.ReleaseButtons(key);
        else
            GamePad?.PressButtons(key);
        return Value.Void;
    }

    private Value ExecuteKeyPress(SsaValue val)
    {
        var key = ((GamePadKeySymbol)val.Aux!).Key;
        var dur = _valueCache[val.Arg0!.Id].AsInt();
        GamePad?.ClickButtons(key, dur, _token);
        return Value.Void;
    }

    private Value ExecuteStickAction(SsaValue val)
    {
        var key = ((GamePadKeySymbol)val.Aux!).Key;
        var packed = val.Const.GetInt();
        GamePad?.SetStick(key, (byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF));
        return Value.Void;
    }

    private Value ExecuteStickPress(SsaValue val)
    {
        var key = ((GamePadKeySymbol)val.Aux!).Key;
        var dur = _valueCache[val.Arg0!.Id].AsInt();
        var packed = val.Const.GetInt();
        GamePad?.ClickStick(key, (byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), dur, _token);
        return Value.Void;
    }

    private Value ExecuteWait(SsaValue val)
    {
        var dur = _valueCache[val.Arg0!.Id].AsInt();
        CustomDelay.Delay(dur, _token);
        return Value.Void;
    }

    private Value ExecuteCapture(SsaValue val)
    {
        var x = V0i(val);
        var y = V1i(val);
        var extras = val.ExtraArgs!;
        var w = _valueCache[extras[0].Id].AsInt();
        var h = _valueCache[extras[1].Id].AsInt();
        var result = Frame?.Invoke(x, y, w, h);
        return Value.FromString(result ?? "ERR!!FRAME NOT SUPPORT");
    }

    private Value ExecuteOcr(SsaValue val)
    {
        var x = V0i(val);
        var y = V1i(val);
        var extras = val.ExtraArgs!;
        var w = _valueCache[extras[0].Id].AsInt();
        var h = _valueCache[extras[1].Id].AsInt();
        var lang = _valueCache[extras[2].Id].AsString();
        var result = Ocr?.Invoke(x, y, w, h, lang);
        return Value.FromString(result ?? "ERR!!OCR NOT SUPPORT");
    }

    private Value ExecuteRoi(SsaValue val)
    {
        var image = _valueCache[val.Arg0!.Id].AsString();
        var x = V1i(val);
        var extras = val.ExtraArgs!;
        var y = _valueCache[extras[0].Id].AsInt();
        var w = _valueCache[extras[1].Id].AsInt();
        var h = _valueCache[extras[2].Id].AsInt();
        var result = Roi?.Invoke(image, x, y, w, h);
        return Value.FromString(result ?? "ERR!!ROI NOT SUPPORT");
    }

    private Value ExecuteRuntimeValue(SsaValue val)
    {
        var name = ((RuntimeValueNameSymbol)val.Aux!).Name;
        if (_runtimeValueGetters.TryGetValue(name, out var getter))
            return getter();
        throw new Exception($"找不到运行时变量 \"{name}\"");
    }

    private Value ExecuteImageLabel(SsaValue val)
    {
        var name = ((RuntimeValueNameSymbol)val.Aux!).Name;
        if (LabelMatch is not { } matcher)
            throw new Exception("图像标签匹配器未初始化");
        return matcher(name);
    }

    // ============ 槽位读写（复用现有逻辑） ============

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

    // Handle 类型的局部读取需要通过堆
    private Value ReadLocalHandle(SlotDesc desc, EvalFrame frame, ScriptType type)
    {
        return _heap.Deref(frame.Handles[desc.Index], type);
    }

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

    private void WriteToSlot(SsaValue val, Value value, EvalFrame frame)
    {
        // Phi 节点的结果需要写入对应的槽位
        // 使用 val 的 Type 来确定槽位类别
        var desc = val.Slot;
        if (desc.Index < 0)
        {
            // 尚未分配槽位（如短路求值的临时Phi），跳过写入
            return;
        }
        var cat = GetSlotCategory(val.Type);
        WriteSlot(desc, frame, val.Type, value);
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

    private static object ValueToRaw(Value value, ScriptType type)
    {
        if (type.Equals(ScriptType.Byte)) return value.AsByte();
        if (type.Equals(ScriptType.Int)) return value.AsInt();
        if (type.Equals(ScriptType.Bool)) return value.AsBool();
        if (type.Equals(ScriptType.UInt)) return value.AsUInt();
        if (type.Equals(ScriptType.UInt64)) return value.AsUInt64();
        if (type.Equals(ScriptType.Ptr)) return new IntPtr(value.AsPtr());
        if (type.Equals(ScriptType.Double)) return value.AsDouble();
        if (type.Equals(ScriptType.String)) return value.AsString();
        if (type is StructType) return value.AsStruct();
        return value.AsInt();
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

    private static int RoundDiv(int a, int b) => (a + b / 2) / b;

    // ============ 函数调用 ============

    public Value EvaluateFunctionBody(FunctionSymbol function)
    {
        if (!_functions.TryGetValue(function, out var func))
            throw new InvalidOperationException($"未找到函数: {function.Name}");
        PushFrame(function);
        try { return EvaluateFunction(func); }
        finally { PopFrame(); }
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

    // ============ 尾递归优化 ============

    private Value EvaluateFunctionWithTailRecursion(FunctionSymbol function, ReadOnlySpan<Value> args, CancellationToken token)
    {
        if (!_functions.TryGetValue(function, out var func))
            throw new InvalidOperationException($"未找到函数: {function.Name}");

        var layout = function.Layout;
        var frame = new EvalFrame(layout.IntSlots, layout.LongSlots, layout.DoubleSlots, layout.HandleSlots);

        for (int i = 0; i < args.Length; i++)
        {
            var param = function.Parameters[i];
            WriteSlot(param.Slot, frame, param.Type, args[i]);
        }

        var callerCache = _valueCache;
        var funcCache = new Value[callerCache.Length];
        _valueCache = funcCache;

        try
        {
            while (true)
            {
                if (token.IsCancellationRequested)
                    throw new OperationCanceledException(token);

                _tailCallRequested = false;
                _localFrames.Push(frame);
                Value result;
                try
                {
                    result = EvaluateFunction(func);
                }
                finally
                {
                    _localFrames.Pop();
                }

                if (!_tailCallRequested)
                    return result;
                // 释放本次迭代中分配的 handles，防止尾递归内存泄漏
                _heap.FreeAll(frame.Handles);
                Array.Clear(frame.Handles);
                // 写入尾调用的新参数（FreeAll 之后再写入，避免 handle 被释放）
                var parameters = function.Parameters;
                for (int i = 0; i < parameters.Length; i++)
                    WriteSlot(parameters[i].Slot, frame, parameters[i].Type, _tailCallArgs[i]);
                // 创建新 cache 避免残留
                funcCache = new Value[callerCache.Length];
                _valueCache = funcCache;
            }
        }
        finally
        {
            _valueCache = callerCache;
        }
    }

    // ============ 全局变量收集 ============

    private static void CollectGlobalVars(SsaFunction func, List<VariableSymbol> list, HashSet<VariableSymbol> seen)
    {
        foreach (var block in func.Blocks)
        {
            foreach (var inst in block.Instructions)
            {
                if (inst.Aux is GlobalVariableSymbol gv && seen.Add(gv))
                    list.Add(gv);
            }
        }
    }

    public void Dispose()
    {
        _heap.FreeAll(_globalHandles);
        foreach (var frame in _localFrames)
            _heap.FreeAll(frame.Handles);
        _localFrames.Clear();
        _heap.FreeAll();
    }
}