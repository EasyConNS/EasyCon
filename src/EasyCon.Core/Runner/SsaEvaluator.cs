using EasyCon.Script;
using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EasyCon.Core.Runner;

/// <summary>
/// 消费 SSA IR 的执行器。
/// 直接遍历 SsaBlock + SsaValue，无需 Bound 树的递归表达式求值。
/// </summary>
public sealed class SsaEvaluator : IEvalContext, IDisposable
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

    // 自递归函数集合：构造时预计算，递归调用需要缓存隔离
    private readonly HashSet<FunctionSymbol> _recursiveFunctions = [];

    // 函数返回值暂存（HandleReturnToCache 写入，GetReturnValue 读取）
    private Value _returnValue;

    // 类型化全局存储
    private int[] _globalInts = [];
    private long[] _globalLongs = [];
    private double[] _globalDoubles = [];
    private int[] _globalHandles = [];
    private readonly Dictionary<VariableSymbol, SlotDesc> _globalSlots = [];

    // 类型化 SSA 值缓存：按 SsaValue.ScriptType 分类存储，消除 Value 结构体开销
    // Int/UInt/Bool/Byte → _intCache, UInt64/Ptr → _longCache, Double → _doubleCache, String/Array/Struct → _objCache
    private int[] _intCache = [];
    private long[] _longCache = [];
    private double[] _doubleCache = [];
    private object?[] _objCache = [];

    // 预计算的常量值（同样按类型分拆，构造时一次性计算，每次创建新 cache 时拷贝进去）
    private int[] _constInt = [];
    private long[] _constLong = [];
    private double[] _constDouble = [];
    private object?[] _constObj = [];

    private readonly long _TIME = DateTime.Now.Ticks;
    private readonly Random _rand = new();
    private bool _cancelLineBreak = false;
    private CancellationToken _token;
    private int _yieldCounter;

    // IEvalContext
    public IIoAdapter? IoAdapter { get; set; }
    public ICGamePad? GamePad { get; set; }
    public OcrDelegate? Ocr { get; set; }
    public FrameDelegate? Frame { get; set; }
    public RoiDelegate? Roi { get; set; }
    public LabelMatchDelegate? LabelMatch { get; set; }
    public OcrInitDelegate? OcrInit { get; set; }
    public Func<int> OcrConf { get; set; } = () => 0;

    ICGamePad? IEvalContext.GamePad => GamePad;
    IIoAdapter? IEvalContext.IoAdapter => IoAdapter;
    OcrDelegate? IEvalContext.Ocr => Ocr;
    FrameDelegate? IEvalContext.Frame => Frame;
    RoiDelegate? IEvalContext.Roi => Roi;
    LabelMatchDelegate? IEvalContext.LabelMatch => LabelMatch;
    OcrInitDelegate? IEvalContext.OcrInit => OcrInit;
    Func<int> IEvalContext.OcrConf => OcrConf;
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

        // 预计算自递归函数集合：递归调用需要缓存隔离
        foreach (var (sym, func) in _functions)
            foreach (var block in func.Blocks)
                foreach (var inst in block.Instructions)
                    if (inst.Op is SsaOp.Call or SsaOp.StaticCall && inst.Aux is FunctionSymbol called && called == sym)
                    {
                        _recursiveFunctions.Add(sym);
                        goto nextFunc;
                    }
    nextFunc:;

        // 预分配类型化缓存，预计算所有常量
        int maxId = 0;
        foreach (var func in _functions.Values)
            foreach (var block in func.Blocks)
                foreach (var val in block.Instructions.Concat(block.Phis))
                    if (val.Id > maxId) maxId = val.Id;

#if DEBUG
        // 验证 SSA ID 全局唯一性：零拷贝共享缓存方案的前提条件
        {
            var seenIds = new HashSet<int>();
            foreach (var func in _functions.Values)
                foreach (var block in func.Blocks)
                    foreach (var val in block.Instructions.Concat(block.Phis))
                        Debug.Assert(seenIds.Add(val.Id), $"SSA ID 冲突: {val.Id}，零拷贝缓存方案不安全");
        }
#endif

        int cacheSize = maxId + 1;
        _intCache = new int[cacheSize];
        _longCache = new long[cacheSize];
        _doubleCache = new double[cacheSize];
        _objCache = new object?[cacheSize];

        // 预计算常量：SSA 常量不依赖控制流，总是可用
        _constInt = new int[cacheSize];
        _constLong = new long[cacheSize];
        _constDouble = new double[cacheSize];
        _constObj = new object?[cacheSize];
        foreach (var func in _functions.Values)
            foreach (var block in func.Blocks)
                foreach (var val in block.Instructions)
                    if (val.IsConstant)
                        PrecomputeConstant(val);

        // 预计算全常量 ArrayInit：跳过运行时 PackToValue 开销
        foreach (var func in _functions.Values)
            foreach (var block in func.Blocks)
                foreach (var val in block.Instructions)
                    if (val.Op == SsaOp.ArrayInit && IsAllConstantArgs(val))
                        PrecomputeArrayInit(val);

        ResetCaches();
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
        _runtimeValueGetters["__APP__"] = () => Value.FromString(AppDomain.CurrentDomain.BaseDirectory);
    }

    public Value Evaluate()
    {
        var func = _program.MainFunction;
        if (func == null) return Value.Void;

        PushFrame(func.Symbol);
        try
        {
            EvaluateFunction(func);
            return GetReturnValue();
        }
        finally
        {
            PopFrame();
        }
    }

    // ============ 主执行循环 ============

    private void EvaluateFunction(SsaFunction func)
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
                    int srcId = phi.ExtraArgs[armIdx].Id;
                    CopyToSlot(phi, srcId, frame);
                    CopyToCache(phi.Id, srcId, phi.Type);
                }

                // 2. 执行普通指令
                int instCount = current.Instructions.Count;
                for (int i = 0; i < instCount; i++)
                {
                    var inst = current.Instructions[i];

                    // 尾调用前截：Call/StaticCall 紧跟 Return，且目标是当前函数 → 跳过实际调用，直接更新帧
                    if (inst.Op is SsaOp.Call or SsaOp.StaticCall && inst.Aux is FunctionSymbol called
                        && called == func.Symbol
                        && i + 1 < instCount
                        && current.Instructions[i + 1].Op == SsaOp.Return
                        && current.Instructions[i + 1].Arg0 == inst)
                    {
                        // 收集参数值（打包为 Value[]，由 EvaluateFunctionWithTailRecursion 负责释放旧帧再写入）
                        var argCount = (inst.Arg0 != null ? 1 : 0) + (inst.ExtraArgs?.Count ?? 0);
                        if (argCount > 0)
                        {
                            if (_tailCallArgs.Length < argCount) _tailCallArgs = new Value[argCount];
                            int ai = 0;
                            if (inst.Arg0 != null) _tailCallArgs[ai++] = PackToValue(inst.Arg0);
                            if (inst.ExtraArgs != null)
                                foreach (var ea in inst.ExtraArgs)
                                    _tailCallArgs[ai++] = PackToValue(ea);
                        }
                        _tailCallRequested = true;
                        return;
                    }

                    ExecuteInstruction(inst, frame);
                }

                // 3. 跳转
                lastBlock = current;

                if (current.IsReturn)
                    return;

                if (current.BranchCondition != null)
                {
                    current = _intCache[current.BranchCondition.Id] != 0
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
        if (++_yieldCounter >= 10000)
        {
            _yieldCounter = 0;
            Thread.Yield();
        }
    }

    // ============ 指令执行 ============

    private void ExecuteInstruction(SsaValue val, EvalFrame frame)
    {
        switch (val.Op)
        {
            // ---- 常量（已在构造时预计算到常量缓存，此处不应执行） ----
            case SsaOp.ConstBool:
            case SsaOp.ConstByte:
            case SsaOp.ConstInt:
            case SsaOp.ConstUInt:
            case SsaOp.ConstUInt64:
            case SsaOp.ConstDouble:
            case SsaOp.ConstString:
            case SsaOp.ConstPtr:
                break; // 常量已在缓存初始化时写入

            // ---- 加载/存储 ----
            case SsaOp.LoadLocal:
                LoadLocalToCache(val, frame);
                break;
            case SsaOp.StoreLocal:
                StoreLocalFromCache(val, frame);
                break;
            case SsaOp.LoadGlobal:
                LoadGlobalToCache(val);
                break;
            case SsaOp.StoreGlobal:
                StoreGlobalFromCache(val);
                break;

            // ---- 算术 (int) ----
            case SsaOp.AddInt:
                _intCache[val.Id] = _intCache[val.Arg0!.Id] + _intCache[val.Arg1!.Id];
                break;
            case SsaOp.SubInt:
                _intCache[val.Id] = _intCache[val.Arg0!.Id] - _intCache[val.Arg1!.Id];
                break;
            case SsaOp.MulInt:
                _intCache[val.Id] = _intCache[val.Arg0!.Id] * _intCache[val.Arg1!.Id];
                break;
            case SsaOp.DivInt:
                _intCache[val.Id] = _intCache[val.Arg0!.Id] / _intCache[val.Arg1!.Id];
                break;
            case SsaOp.ModInt:
                _intCache[val.Id] = _intCache[val.Arg0!.Id] % _intCache[val.Arg1!.Id];
                break;
            case SsaOp.RoundDivInt:
                _intCache[val.Id] = RoundDiv(_intCache[val.Arg0!.Id], _intCache[val.Arg1!.Id]);
                break;

            // ---- 算术 (uint) ----
            case SsaOp.AddUInt:
                _intCache[val.Id] = unchecked((int)((long)(uint)_intCache[val.Arg0!.Id] + (uint)_intCache[val.Arg1!.Id]));
                break;
            case SsaOp.SubUInt:
                _intCache[val.Id] = unchecked((int)((long)(uint)_intCache[val.Arg0!.Id] - (uint)_intCache[val.Arg1!.Id]));
                break;
            case SsaOp.MulUInt:
                _intCache[val.Id] = unchecked((int)((long)(uint)_intCache[val.Arg0!.Id] * (uint)_intCache[val.Arg1!.Id]));
                break;
            case SsaOp.DivUInt:
                _intCache[val.Id] = unchecked((int)((uint)_intCache[val.Arg0!.Id] / (uint)_intCache[val.Arg1!.Id]));
                break;
            case SsaOp.ModUInt:
                _intCache[val.Id] = unchecked((int)((uint)_intCache[val.Arg0!.Id] % (uint)_intCache[val.Arg1!.Id]));
                break;

            // ---- 算术 (double) ----
            case SsaOp.AddDouble:
                _doubleCache[val.Id] = _doubleCache[val.Arg0!.Id] + _doubleCache[val.Arg1!.Id];
                break;
            case SsaOp.SubDouble:
                _doubleCache[val.Id] = _doubleCache[val.Arg0!.Id] - _doubleCache[val.Arg1!.Id];
                break;
            case SsaOp.MulDouble:
                _doubleCache[val.Id] = _doubleCache[val.Arg0!.Id] * _doubleCache[val.Arg1!.Id];
                break;
            case SsaOp.DivDouble:
                _doubleCache[val.Id] = _doubleCache[val.Arg0!.Id] / _doubleCache[val.Arg1!.Id];
                break;

            // ---- 算术 (uint64) ----
            case SsaOp.AddUInt64:
                _longCache[val.Id] = unchecked((long)((ulong)_longCache[val.Arg0!.Id] + (ulong)_longCache[val.Arg1!.Id]));
                break;
            case SsaOp.SubUInt64:
                _longCache[val.Id] = unchecked((long)((ulong)_longCache[val.Arg0!.Id] - (ulong)_longCache[val.Arg1!.Id]));
                break;
            case SsaOp.MulUInt64:
                _longCache[val.Id] = unchecked((long)((ulong)_longCache[val.Arg0!.Id] * (ulong)_longCache[val.Arg1!.Id]));
                break;
            case SsaOp.DivUInt64:
                _longCache[val.Id] = unchecked((long)((ulong)_longCache[val.Arg0!.Id] / (ulong)_longCache[val.Arg1!.Id]));
                break;
            case SsaOp.ModUInt64:
                _longCache[val.Id] = unchecked((long)((ulong)_longCache[val.Arg0!.Id] % (ulong)_longCache[val.Arg1!.Id]));
                break;

            // ---- 位运算 ----
            case SsaOp.AndInt:
                _intCache[val.Id] = _intCache[val.Arg0!.Id] & _intCache[val.Arg1!.Id];
                break;
            case SsaOp.OrInt:
                _intCache[val.Id] = _intCache[val.Arg0!.Id] | _intCache[val.Arg1!.Id];
                break;
            case SsaOp.XorInt:
                _intCache[val.Id] = _intCache[val.Arg0!.Id] ^ _intCache[val.Arg1!.Id];
                break;
            case SsaOp.ShlInt:
                _intCache[val.Id] = _intCache[val.Arg0!.Id] << _intCache[val.Arg1!.Id];
                break;
            case SsaOp.ShrInt:
                _intCache[val.Id] = _intCache[val.Arg0!.Id] >> _intCache[val.Arg1!.Id];
                break;
            case SsaOp.NotInt:
                _intCache[val.Id] = ~_intCache[val.Arg0!.Id];
                break;

            // ---- 比较 (int) → 结果是 bool，存入 _intCache ----
            case SsaOp.EqInt: _intCache[val.Id] = _intCache[val.Arg0!.Id] == _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.NeqInt: _intCache[val.Id] = _intCache[val.Arg0!.Id] != _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.LtInt: _intCache[val.Id] = _intCache[val.Arg0!.Id] < _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.LeqInt: _intCache[val.Id] = _intCache[val.Arg0!.Id] <= _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.GtInt: _intCache[val.Id] = _intCache[val.Arg0!.Id] > _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.GeqInt: _intCache[val.Id] = _intCache[val.Arg0!.Id] >= _intCache[val.Arg1!.Id] ? 1 : 0; break;

            // ---- 比较 (uint) ----
            case SsaOp.EqUInt: _intCache[val.Id] = (uint)_intCache[val.Arg0!.Id] == (uint)_intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.NeqUInt: _intCache[val.Id] = (uint)_intCache[val.Arg0!.Id] != (uint)_intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.LtUInt: _intCache[val.Id] = (uint)_intCache[val.Arg0!.Id] < (uint)_intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.LeqUInt: _intCache[val.Id] = (uint)_intCache[val.Arg0!.Id] <= (uint)_intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.GtUInt: _intCache[val.Id] = (uint)_intCache[val.Arg0!.Id] > (uint)_intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.GeqUInt: _intCache[val.Id] = (uint)_intCache[val.Arg0!.Id] >= (uint)_intCache[val.Arg1!.Id] ? 1 : 0; break;

            // ---- 比较 (double) ----
            case SsaOp.EqDouble: _intCache[val.Id] = _doubleCache[val.Arg0!.Id] == _doubleCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.NeqDouble: _intCache[val.Id] = _doubleCache[val.Arg0!.Id] != _doubleCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.LtDouble: _intCache[val.Id] = _doubleCache[val.Arg0!.Id] < _doubleCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.LeqDouble: _intCache[val.Id] = _doubleCache[val.Arg0!.Id] <= _doubleCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.GtDouble: _intCache[val.Id] = _doubleCache[val.Arg0!.Id] > _doubleCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.GeqDouble: _intCache[val.Id] = _doubleCache[val.Arg0!.Id] >= _doubleCache[val.Arg1!.Id] ? 1 : 0; break;

            // ---- 比较 (uint64) ----
            case SsaOp.EqUInt64: _intCache[val.Id] = (ulong)_longCache[val.Arg0!.Id] == (ulong)_longCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.NeqUInt64: _intCache[val.Id] = (ulong)_longCache[val.Arg0!.Id] != (ulong)_longCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.LtUInt64: _intCache[val.Id] = (ulong)_longCache[val.Arg0!.Id] < (ulong)_longCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.LeqUInt64: _intCache[val.Id] = (ulong)_longCache[val.Arg0!.Id] <= (ulong)_longCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.GtUInt64: _intCache[val.Id] = (ulong)_longCache[val.Arg0!.Id] > (ulong)_longCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.GeqUInt64: _intCache[val.Id] = (ulong)_longCache[val.Arg0!.Id] >= (ulong)_longCache[val.Arg1!.Id] ? 1 : 0; break;

            // ---- 比较 (bool/string/ptr/byte) ----
            case SsaOp.EqBool: _intCache[val.Id] = _intCache[val.Arg0!.Id] == _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.NeqBool: _intCache[val.Id] = _intCache[val.Arg0!.Id] != _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.EqString: _intCache[val.Id] = string.Equals((string)_objCache[val.Arg0!.Id]!, (string)_objCache[val.Arg1!.Id]!, StringComparison.Ordinal) ? 1 : 0; break;
            case SsaOp.NeqString: _intCache[val.Id] = !string.Equals((string)_objCache[val.Arg0!.Id]!, (string)_objCache[val.Arg1!.Id]!, StringComparison.Ordinal) ? 1 : 0; break;
            case SsaOp.EqPtr: _intCache[val.Id] = _longCache[val.Arg0!.Id] == _longCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.NeqPtr: _intCache[val.Id] = _longCache[val.Arg0!.Id] != _longCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.EqByte: _intCache[val.Id] = _intCache[val.Arg0!.Id] == _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.NeqByte: _intCache[val.Id] = _intCache[val.Arg0!.Id] != _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.LtByte: _intCache[val.Id] = _intCache[val.Arg0!.Id] < _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.LeqByte: _intCache[val.Id] = _intCache[val.Arg0!.Id] <= _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.GtByte: _intCache[val.Id] = _intCache[val.Arg0!.Id] > _intCache[val.Arg1!.Id] ? 1 : 0; break;
            case SsaOp.GeqByte: _intCache[val.Id] = _intCache[val.Arg0!.Id] >= _intCache[val.Arg1!.Id] ? 1 : 0; break;

            // ---- 逻辑 ----
            case SsaOp.LogicNot:
                _intCache[val.Id] = _intCache[val.Arg0!.Id] == 0 ? 1 : 0;
                break;

            // ---- 类型转换 ----
            case SsaOp.ConvBoolToInt: _intCache[val.Id] = _intCache[val.Arg0!.Id]; break;       // bool(0/1) → int
            case SsaOp.ConvByteToInt: _intCache[val.Id] = _intCache[val.Arg0!.Id]; break;       // byte → int
            case SsaOp.ConvIntToUInt: _intCache[val.Id] = _intCache[val.Arg0!.Id]; break;       // bit-preserving
            case SsaOp.ConvIntToUInt64: _longCache[val.Id] = _intCache[val.Arg0!.Id]; break;
            case SsaOp.ConvIntToDouble: _doubleCache[val.Id] = _intCache[val.Arg0!.Id]; break;
            case SsaOp.ConvIntToByte: _intCache[val.Id] = (byte)_intCache[val.Arg0!.Id]; break;
            case SsaOp.ConvUIntToUInt64: _longCache[val.Id] = (uint)_intCache[val.Arg0!.Id]; break;
            case SsaOp.ConvUInt64ToPtr: _longCache[val.Id] = _longCache[val.Arg0!.Id]; break;     // bit-preserving
            case SsaOp.ConvPtrToInt: _intCache[val.Id] = (int)_longCache[val.Arg0!.Id]; break;
            case SsaOp.ConvIntToPtr: _longCache[val.Id] = _intCache[val.Arg0!.Id]; break;
            case SsaOp.ConvDoubleToInt: _intCache[val.Id] = (int)_doubleCache[val.Arg0!.Id]; break;
            case SsaOp.ConvUInt64ToInt: _intCache[val.Id] = unchecked((int)(ulong)_longCache[val.Arg0!.Id]); break;
            case SsaOp.ConvToString:
                _objCache[val.Id] = ConvToString(val.Arg0!);
                break;
            case SsaOp.ConvToInt:
                _intCache[val.Id] = ConvToInt(val.Arg0!);
                break;
            case SsaOp.ArrayAppend:
                _objCache[val.Id] = ((ScriptArray)_objCache[val.Arg0!.Id]!).Append(PackToValue(val.Arg1!));
                break;

            // ---- 控制流（Phi 在主循环中处理） ----
            case SsaOp.Phi:
            case SsaOp.CondBranch:
            case SsaOp.Branch:
                break;
            case SsaOp.Return:
                HandleReturnToCache(val);
                break;

            // ---- 调用（边界处使用 Value） ----
            case SsaOp.Call:
            case SsaOp.StaticCall:
                ExecuteCallToCache(val);
                break;

            // ---- 复合数据 ----
            case SsaOp.ArrayInit:
                ExecuteArrayInitToCache(val);
                break;
            case SsaOp.LoadIndex:
                ExecuteLoadIndexToCache(val);
                break;
            case SsaOp.StoreIndex:
                ExecuteStoreIndexFromCache(val);
                break;
            case SsaOp.Slice:
                ExecuteSliceToCache(val);
                break;
            case SsaOp.ArrayLen:
                {
                    var obj = _objCache[val.Arg0!.Id];
                    _intCache[val.Id] = obj is ScriptArray arr ? arr.Length
                        : obj is string s ? s.Length
                        : 0;
                    break;
                }
            case SsaOp.Contains:
                _intCache[val.Id] = PackToValue(val.Arg1!).Contains(PackToValue(val.Arg0!)) ? 1 : 0;
                break;
            case SsaOp.Concat:
                ExecuteConcatToCache(val);
                break;
            case SsaOp.DeepCopy:
                ExecuteDeepCopyToCache(val);
                break;

            // ---- 结构体 ----
            case SsaOp.StructInit:
                _objCache[val.Id] = new EcsStruct(((StructType)val.Type).Definition);
                break;
            case SsaOp.LoadField:
                ExecuteLoadFieldToCache(val);
                break;
            case SsaOp.StoreField:
                ExecuteStoreFieldFromCache(val);
                break;
            case SsaOp.LoadFieldIndex:
                ExecuteLoadFieldIndexToCache(val);
                break;
            case SsaOp.StoreFieldIndex:
                ExecuteStoreFieldIndexFromCache(val);
                break;

            // ---- 领域操作 ----
            case SsaOp.KeyAction:
                ExecuteKeyAction(val);
                break;
            case SsaOp.KeyPress:
                ExecuteKeyPress(val);
                break;
            case SsaOp.StickAction:
                ExecuteStickAction(val);
                break;
            case SsaOp.StickPress:
                ExecuteStickPress(val);
                break;
            case SsaOp.Wait:
                ExecuteWait(val);
                break;
            case SsaOp.Rand:
                _intCache[val.Id] = _rand.Next(_intCache[val.Arg0!.Id]);
                break;

            // ---- OCR 引擎初始化 ----
            case SsaOp.OcrInit:
                ExecuteOcrInitToCache(val);
                break;

            // ---- 采集卡打洞函数 ----
            case SsaOp.Capture:
                ExecuteCaptureToCache(val);
                break;
            case SsaOp.Ocr:
                ExecuteOcrToCache(val);
                break;
            case SsaOp.Roi:
                ExecuteRoiToCache(val);
                break;

            // ---- 运行时（保留 Value 转换，这些路径极冷） ----
            case SsaOp.RuntimeValue:
                ExecuteRuntimeValueToCache(val);
                break;
            case SsaOp.ImageLabel:
                ExecuteImageLabelToCache(val);
                break;

            case SsaOp.Nop:
                break;

            default:
                throw new InvalidOperationException($"未实现的 SsaOp: {val.Op}");
        }
    }

    // ============ 常量预计算（写入类型化常量缓存） ============

    private void PrecomputeConstant(SsaValue val)
    {
        switch (val.Op)
        {
            case SsaOp.ConstBool: _constInt[val.Id] = val.Const.GetBool() ? 1 : 0; break;
            case SsaOp.ConstByte: _constInt[val.Id] = val.Const.GetByte(); break;
            case SsaOp.ConstInt: _constInt[val.Id] = val.Const.GetInt(); break;
            case SsaOp.ConstUInt: _constInt[val.Id] = val.Const.GetInt(); break; // bit-preserving
            case SsaOp.ConstUInt64: _constLong[val.Id] = unchecked((long)val.Const.GetUInt64()); break;
            case SsaOp.ConstDouble: _constDouble[val.Id] = val.Const.GetDouble(); break;
            case SsaOp.ConstString: _constObj[val.Id] = val.ConstString ?? ""; break;
            case SsaOp.ConstPtr: _constLong[val.Id] = val.Const.GetPtr(); break;
        }
    }

    /// <summary>检查 ArrayInit 的所有元素是否都是编译期常量</summary>
    private static bool IsAllConstantArgs(SsaValue val)
    {
        if (val.Arg0 != null && !val.Arg0.IsConstant) return false;
        if (val.ExtraArgs != null)
            foreach (var arg in val.ExtraArgs)
                if (!arg.IsConstant) return false;
        return true;
    }

    /// <summary>预计算全常量 ArrayInit：直接从常量缓存构建类型化数组</summary>
    private void PrecomputeArrayInit(SsaValue val)
    {
        var elemType = ((ArrayType)val.Type).ElementType;
        int count = (val.Arg0 != null ? 1 : 0) + (val.ExtraArgs?.Count ?? 0);

        if (count == 0)
        {
            _constObj[val.Id] = ScriptArray.Create(elemType, Array.Empty<Value>());
            return;
        }

        var cat = GetSlotCategory(elemType);

        if (cat == SlotCategory.Int)
        {
            var data = new int[count];
            int idx = 0;
            if (val.Arg0 != null) data[idx++] = _constInt[val.Arg0.Id];
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) data[idx++] = _constInt[a.Id];
            _constObj[val.Id] = elemType.Equals(ScriptType.Byte)
                ? ScriptArray.CreateDirect(ScriptType.Byte, ToByteArray(data))
                : elemType.Equals(ScriptType.Bool)
                    ? ScriptArray.CreateDirect(ScriptType.Bool, data)
                    : elemType.Equals(ScriptType.UInt)
                        ? ScriptArray.CreateDirect(ScriptType.UInt, ToUIntArray(data))
                        : new IntArray(data, elemType);
        }
        else if (cat == SlotCategory.Long)
        {
            var data = new long[count];
            int idx = 0;
            if (val.Arg0 != null) data[idx++] = _constLong[val.Arg0.Id];
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) data[idx++] = _constLong[a.Id];
            _constObj[val.Id] = elemType.Equals(ScriptType.UInt64)
                ? ScriptArray.CreateDirect(ScriptType.UInt64, ToULongArray(data))
                : new LongArray(data, elemType);
        }
        else if (cat == SlotCategory.Double)
        {
            var data = new double[count];
            int idx = 0;
            if (val.Arg0 != null) data[idx++] = _constDouble[val.Arg0.Id];
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) data[idx++] = _constDouble[a.Id];
            _constObj[val.Id] = new DoubleArray(data, elemType);
        }
        else // Handle: string 等
        {
            var items = new Value[count];
            int idx = 0;
            if (val.Arg0 != null) items[idx++] = PackConstToValue(val.Arg0);
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) items[idx++] = PackConstToValue(a);
            _constObj[val.Id] = ScriptArray.Create(elemType, items);
        }
    }

    private static byte[] ToByteArray(int[] src)
    {
        var r = new byte[src.Length];
        for (int i = 0; i < src.Length; i++) r[i] = (byte)src[i];
        return r;
    }

    private static uint[] ToUIntArray(int[] src)
    {
        var r = new uint[src.Length];
        for (int i = 0; i < src.Length; i++) r[i] = unchecked((uint)src[i]);
        return r;
    }

    private static ulong[] ToULongArray(long[] src)
    {
        var r = new ulong[src.Length];
        for (int i = 0; i < src.Length; i++) r[i] = unchecked((ulong)src[i]);
        return r;
    }

    /// <summary>从常量缓存打包为 Value（仅用于 PrecomputeArrayInit 的 fallback 路径）</summary>
    private Value PackConstToValue(SsaValue v)
    {
        return GetSlotCategory(v.Type) switch
        {
            SlotCategory.Int => PackIntToValue(_constInt[v.Id], v.Type),
            SlotCategory.Long => v.Type.Equals(ScriptType.UInt64)
                ? Value.FromUInt64((ulong)_constLong[v.Id])
                : Value.FromPtr(_constLong[v.Id]),
            SlotCategory.Double => Value.FromDouble(_constDouble[v.Id]),
            SlotCategory.Handle => PackObjToValue(_constObj[v.Id], v.Type),
            _ => Value.Void
        };
    }

    // ============ 类型化缓存工具方法 ============

    /// <summary>重置执行缓存，拷贝常量到活跃缓存</summary>
    private void ResetCaches()
    {
        Array.Copy(_constInt, _intCache, _constInt.Length);
        Array.Copy(_constLong, _longCache, _constLong.Length);
        Array.Copy(_constDouble, _doubleCache, _constDouble.Length);
        Array.Copy(_constObj, _objCache, _constObj.Length);
    }

    /// <summary>将 SSA 值从类型化缓存打包为 Value（仅用于函数调用边界）</summary>
    private Value PackToValue(SsaValue v)
    {
        return GetSlotCategory(v.Type) switch
        {
            SlotCategory.Int => PackIntToValue(_intCache[v.Id], v.Type),
            SlotCategory.Long => v.Type.Equals(ScriptType.UInt64)
                ? Value.FromUInt64((ulong)_longCache[v.Id])
                : Value.FromPtr(_longCache[v.Id]),
            SlotCategory.Double => Value.FromDouble(_doubleCache[v.Id]),
            SlotCategory.Handle => PackObjToValue(_objCache[v.Id], v.Type),
            _ => Value.Void
        };
    }

    /// <summary>将 SSA 值从类型化缓存打包为 Value（按指定类型，用于 Phi 等跨类型场景）</summary>
    private Value PackToValue(SsaValue v, ScriptType type)
    {
        return GetSlotCategory(type) switch
        {
            SlotCategory.Int => PackIntToValue(_intCache[v.Id], type),
            SlotCategory.Long => type.Equals(ScriptType.UInt64)
                ? Value.FromUInt64((ulong)_longCache[v.Id])
                : Value.FromPtr(_longCache[v.Id]),
            SlotCategory.Double => Value.FromDouble(_doubleCache[v.Id]),
            SlotCategory.Handle => PackObjToValue(_objCache[v.Id], type),
            _ => Value.Void
        };
    }

    private static Value PackIntToValue(int val, ScriptType type)
    {
        if (type.Equals(ScriptType.Bool)) return Value.FromBool(val != 0);
        if (type.Equals(ScriptType.Byte)) return Value.FromByte((byte)val);
        if (type.Equals(ScriptType.UInt)) return Value.FromUInt(unchecked((uint)val));
        return Value.FromInt(val);
    }

    private static Value PackObjToValue(object? obj, ScriptType type)
    {
        if (obj is string s) return Value.FromString(s);
        if (obj is ScriptArray arr) return Value.FromArray(arr, ((ArrayType)type).ElementType);
        if (obj is EcsStruct es) return Value.FromStruct(es);
        return Value.Void;
    }

    /// <summary>将 Value 解包到类型化缓存（仅用于函数调用返回值边界）</summary>
    private void UnpackToCache(int id, ScriptType type, Value value)
    {
        switch (GetSlotCategory(type))
        {
            case SlotCategory.Int:
                _intCache[id] = WriteIntSlot(type, value);
                break;
            case SlotCategory.Long:
                _longCache[id] = type.Equals(ScriptType.UInt64)
                    ? (long)value.AsUInt64()
                    : value.AsPtr();
                break;
            case SlotCategory.Double:
                _doubleCache[id] = value.AsDouble();
                break;
            case SlotCategory.Handle:
                _objCache[id] = ExtractHandleObj(type, value);
                break;
        }
    }

    private static object? ExtractHandleObj(ScriptType type, Value value)
    {
        if (type.Equals(ScriptType.String)) return value.AsString();
        if (type is ArrayType) return value.AsArray();
        if (type is StructType) return value.AsStruct();
        return null;
    }

    /// <summary>从缓存中读取原始值（用于 ConvToString 等需要通用读取的场景）</summary>
    private object? ReadRaw(SsaValue v)
    {
        return GetSlotCategory(v.Type) switch
        {
            SlotCategory.Int => v.Type.Equals(ScriptType.Bool) ? (_intCache[v.Id] != 0) : _intCache[v.Id],
            SlotCategory.Long => _longCache[v.Id],
            SlotCategory.Double => _doubleCache[v.Id],
            SlotCategory.Handle => _objCache[v.Id],
            _ => null
        };
    }

    /// <summary>通用 int 转换（用于 ConvToInt）</summary>
    private int ConvToInt(SsaValue v)
    {
        return GetSlotCategory(v.Type) switch
        {
            SlotCategory.Int => _intCache[v.Id],
            SlotCategory.Double => (int)_doubleCache[v.Id],
            _ => throw new InvalidCastException($"无法从 {v.Type} 转换为 int")
        };
    }

    /// <summary>通用 string 转换（用于 ConvToString）— 通过 Value.ToString() 确保数组等类型正确格式化</summary>
    private string ConvToString(SsaValue v)
    {
        var cat = GetSlotCategory(v.Type);
        return cat switch
        {
            SlotCategory.Int => v.Type.Equals(ScriptType.Bool)
                ? (_intCache[v.Id] != 0 ? "true" : "false")
                : _intCache[v.Id].ToString(),
            SlotCategory.Long => v.Type.Equals(ScriptType.UInt64)
                ? ((ulong)_longCache[v.Id]).ToString()
                : _longCache[v.Id].ToString(),
            SlotCategory.Double => _doubleCache[v.Id].ToString(),
            SlotCategory.Handle => PackToValue(v).ToString(),
            _ => ""
        };
    }

    /// <summary>将缓存中的值拷贝到目标 SSA 值的缓存槽位（Phi 使用）</summary>
    private void CopyToCache(int dstId, int srcId, ScriptType type)
    {
        switch (GetSlotCategory(type))
        {
            case SlotCategory.Int: _intCache[dstId] = _intCache[srcId]; break;
            case SlotCategory.Long: _longCache[dstId] = _longCache[srcId]; break;
            case SlotCategory.Double: _doubleCache[dstId] = _doubleCache[srcId]; break;
            case SlotCategory.Handle: _objCache[dstId] = _objCache[srcId]; break;
        }
    }

    /// <summary>将缓存中的值写入帧槽位（Phi/StoreLocal 使用）</summary>
    private void CopyToSlot(SsaValue phi, int srcId, EvalFrame frame)
    {
        var desc = phi.Slot;
        if (desc.Index < 0) return;
        switch (desc.Category)
        {
            case SlotCategory.Int: frame.Ints[desc.Index] = _intCache[srcId]; break;
            case SlotCategory.Long: frame.Longs[desc.Index] = _longCache[srcId]; break;
            case SlotCategory.Double: frame.Doubles[desc.Index] = _doubleCache[srcId]; break;
            case SlotCategory.Handle:
                if (frame.Handles[desc.Index] != 0) _heap.Free(frame.Handles[desc.Index]);
                frame.Handles[desc.Index] = StoreHandleObj(phi.Type, _objCache[srcId]);
                break;
        }
    }

    /// <summary>获取函数返回值（由 HandleReturnToCache 暂存）</summary>
    private Value GetReturnValue()
    {
        return _returnValue;
    }

    // ============ 缓存访问器（用于需要通用 Value 的边界场景） ============

    /// <summary>从对象缓存读取字符串</summary>
    private string CoerceString(int id) => _objCache[id] as string ?? "";
    /// <summary>从对象缓存读取数组</summary>
    private ScriptArray CoerceArray(int id) => (ScriptArray)_objCache[id]!;
    /// <summary>从对象缓存读取结构体</summary>
    private EcsStruct CoerceStruct(int id) => (EcsStruct)_objCache[id]!;

    // ============ 辅助执行方法（类型化缓存版本） ============

    private void LoadLocalToCache(SsaValue val, EvalFrame frame)
    {
        var desc = ((LocalVariableSymbol)val.Aux!).Slot;
        switch (desc.Category)
        {
            case SlotCategory.Int: _intCache[val.Id] = frame.Ints[desc.Index]; break;
            case SlotCategory.Long: _longCache[val.Id] = frame.Longs[desc.Index]; break;
            case SlotCategory.Double: _doubleCache[val.Id] = frame.Doubles[desc.Index]; break;
            case SlotCategory.Handle: _objCache[val.Id] = _heap.DerefObject(frame.Handles[desc.Index], val.Type); break;
        }
    }

    private void StoreLocalFromCache(SsaValue val, EvalFrame frame)
    {
        var variable = (LocalVariableSymbol)val.Aux!;
        var desc = variable.Slot;
        switch (desc.Category)
        {
            case SlotCategory.Int: frame.Ints[desc.Index] = _intCache[val.Arg0!.Id]; break;
            case SlotCategory.Long: frame.Longs[desc.Index] = _longCache[val.Arg0!.Id]; break;
            case SlotCategory.Double: frame.Doubles[desc.Index] = _doubleCache[val.Arg0!.Id]; break;
            case SlotCategory.Handle:
                if (frame.Handles[desc.Index] != 0) _heap.Free(frame.Handles[desc.Index]);
                frame.Handles[desc.Index] = StoreHandleObj(variable.Type, _objCache[val.Arg0!.Id]);
                break;
        }
    }

    private void LoadGlobalToCache(SsaValue val)
    {
        var desc = _globalSlots[(VariableSymbol)val.Aux!];
        switch (desc.Category)
        {
            case SlotCategory.Int: _intCache[val.Id] = _globalInts[desc.Index]; break;
            case SlotCategory.Long: _longCache[val.Id] = _globalLongs[desc.Index]; break;
            case SlotCategory.Double: _doubleCache[val.Id] = _globalDoubles[desc.Index]; break;
            case SlotCategory.Handle: _objCache[val.Id] = _heap.DerefObject(_globalHandles[desc.Index], val.Type); break;
        }
    }

    private void StoreGlobalFromCache(SsaValue val)
    {
        var variable = (VariableSymbol)val.Aux!;
        var desc = _globalSlots[variable];
        var srcArg = val.Arg0;
        switch (desc.Category)
        {
            case SlotCategory.Int: _globalInts[desc.Index] = srcArg != null ? _intCache[srcArg.Id] : 0; break;
            case SlotCategory.Long: _globalLongs[desc.Index] = srcArg != null ? _longCache[srcArg.Id] : 0; break;
            case SlotCategory.Double: _globalDoubles[desc.Index] = srcArg != null ? _doubleCache[srcArg.Id] : 0; break;
            case SlotCategory.Handle:
                if (_globalHandles[desc.Index] != 0) _heap.Free(_globalHandles[desc.Index]);
                _globalHandles[desc.Index] = srcArg != null ? StoreHandleObj(variable.Type, _objCache[srcArg.Id]) : 0;
                break;
        }
    }

    private void HandleReturnToCache(SsaValue val)
    {
        // 尾调用检测：块末尾的 RETURN f(...) 且 f == 当前函数
        if (_currentFunc != null && val.Arg0 is { Op: SsaOp.Call, Aux: FunctionSymbol called }
            && called == _currentFunc.Symbol)
        {
            var block = val.Block;
            var insts = block.Instructions;
            int retIdx = insts.Count - 1;
            if (retIdx >= 1 && insts[retIdx] == val && insts[retIdx - 1] == val.Arg0)
            {
                var frame = _localFrames.Peek();
                var callVal = val.Arg0;
                if (callVal.Arg0 != null)
                {
                    var p0 = called.Parameters[0];
                    WriteCacheToSlot(p0.Slot, frame, p0.Type, callVal.Arg0);
                }
                if (callVal.ExtraArgs != null)
                {
                    for (int i = 0; i < callVal.ExtraArgs.Count; i++)
                    {
                        var p = called.Parameters[i + 1];
                        WriteCacheToSlot(p.Slot, frame, p.Type, callVal.ExtraArgs[i]);
                    }
                }
                _tailCallRequested = true;
                return;
            }
        }

        // 非尾调用：将返回值打包暂存
        _returnValue = val.Arg0 != null ? PackToValue(val.Arg0) : Value.Void;
    }

    private void ExecuteCallToCache(SsaValue val)
    {
        var function = (FunctionSymbol)val.Aux!;
        var argCount = (val.Arg0 != null ? 1 : 0) + (val.ExtraArgs?.Count ?? 0);

        Value result;
        if (argCount == 0)
        {
            // 用户函数快速路径：跳过 _callables 字典查找和委托虚分派
            result = _functions.ContainsKey(function)
                ? EvaluateFunctionWithTailRecursion(function, ReadOnlySpan<Value>.Empty, _token)
                : _callables[function].Invoke(ReadOnlySpan<Value>.Empty, this, _token);
        }
        else
        {
            Value[]? rented = null;
            var args = argCount <= 8
                ? (rented = ArrayPool<Value>.Shared.Rent(argCount))
                : new Value[argCount];
            try
            {
                if (val.Arg0 != null) args[0] = PackToValue(val.Arg0);
                if (val.ExtraArgs != null)
                    for (int i = 0; i < val.ExtraArgs.Count; i++)
                        args[i + 1] = PackToValue(val.ExtraArgs[i]);
                var span = args.AsSpan(0, argCount);
                // 用户函数快速路径：跳过 _callables 字典查找和委托虚分派
                result = _functions.ContainsKey(function)
                    ? EvaluateFunctionWithTailRecursion(function, span, _token)
                    : _callables[function].Invoke(span, this, _token);
            }
            finally
            {
                if (rented != null) ArrayPool<Value>.Shared.Return(rented);
            }
        }

        // 将返回值解包到类型化缓存
        if (function.ReturnType.Equals(ScriptType.Void)) return;
        UnpackToCache(val.Id, function.ReturnType, result);
    }

    private void ExecuteArrayInitToCache(SsaValue val)
    {
        // 全常量 ArrayInit 已在构造时预计算到 _constObj，ResetCaches 拷贝到 _objCache
        if (_objCache[val.Id] != null) return;

        var items = new List<Value>();
        if (val.Arg0 != null)
        {
            items.Add(PackToValue(val.Arg0));
            if (val.ExtraArgs != null)
                foreach (var arg in val.ExtraArgs)
                    items.Add(PackToValue(arg));
        }
        var elemType = ((ArrayType)val.Type).ElementType;
        _objCache[val.Id] = ScriptArray.Create(elemType, items);
    }

    private void ExecuteLoadIndexToCache(SsaValue val)
    {
        var obj = _objCache[val.Arg0!.Id];
        var index = _intCache[val.Arg1!.Id];

        if (obj is string s)
        {
            if (index < 0 || index >= s.Length)
            {
                // 临时追踪：记录越界前的上下文
                var ctx = index > 0 && index <= s.Length ? s[index - 1].ToString() : "?";
                var ctx2 = index > 1 && index <= s.Length ? s[index - 2].ToString() : "?";
                throw new Exception($"数组下标越界: 字符串长度={s.Length}, index={index}, 前5=[{s[..Math.Min(5, s.Length)]}], s[{index - 2}]='{ctx2}', s[{index - 1}]='{ctx}', val.Id={val.Id}, Arg0.Id={val.Arg0!.Id}, Arg0.Op={val.Arg0!.Op}");
            }
            _objCache[val.Id] = s[index].ToString();
            return;
        }

        var container = (ScriptArray)obj!;
        if (index < 0 || index >= container.Length) throw new Exception($"数组下标越界: 数组长度={container.Length}, index={index}");
        var elemValue = container[index];
        // 将 Value 元素解包到类型化缓存
        var elemType = ((ArrayType)val.Arg0!.Type).ElementType;
        UnpackToCache(val.Id, elemType, elemValue);
    }

    private void ExecuteStoreIndexFromCache(SsaValue val)
    {
        var container = CoerceArray(val.Arg0!.Id);
        var index = _intCache[val.Arg1!.Id];
        var elemType = ((ArrayType)val.Arg0!.Type).ElementType;
        var value = PackToValue(val.ExtraArgs![0], elemType);

        // 原地修改
        if (val.Arg0 is { Op: SsaOp.LoadLocal, Aux: LocalVariableSymbol local })
        {
            var frame = _localFrames.Peek();
            var handle = local.Slot.Category == SlotCategory.Handle ? frame.Handles[local.Slot.Index] : 0;
            if (handle != 0)
            {
                _heap.GetArray(handle).SetItem(index, value);
                return;
            }
        }
        else if (val.Arg0 is { Op: SsaOp.LoadGlobal, Aux: GlobalVariableSymbol gv })
        {
            var desc = _globalSlots[gv];
            var handle = desc.Category == SlotCategory.Handle ? _globalHandles[desc.Index] : 0;
            if (handle != 0)
            {
                _heap.GetArray(handle).SetItem(index, value);
                return;
            }
        }
        container.SetItem(index, value);
    }

    private void ExecuteSliceToCache(SsaValue val)
    {
        var obj = _objCache[val.Arg0!.Id];
        var start = _intCache[val.Arg1!.Id];
        // end 参数：如果有 ExtraArgs 且类型为 Int，读取缓存；否则使用容器长度
        int endIdx;
        if (val.ExtraArgs != null && val.ExtraArgs[0].Type.Equals(ScriptType.Int))
            endIdx = _intCache[val.ExtraArgs[0].Id];
        else if (obj is ScriptArray arr)
            endIdx = arr.Length;
        else if (obj is string s)
            endIdx = s.Length;
        else
            endIdx = 0;

        if (obj is string str)
        {
            if (start > str.Length || endIdx > str.Length || start > endIdx)
                throw new Exception($"数组下标越界: 字符串长度={str.Length}, start={start}, end={endIdx}");
            _objCache[val.Id] = str[new Range(start, endIdx)];
        }
        else if (obj is ScriptArray container)
        {
            if (start > container.Length || endIdx > container.Length || start > endIdx)
                throw new Exception($"数组下标越界: 数组长度={container.Length}, start={start}, end={endIdx}");
            _objCache[val.Id] = container.GetRange(start, endIdx - start);
        }
        else
        {
            throw new Exception("不支持切片的类型");
        }
    }

    private void ExecuteConcatToCache(SsaValue val)
    {
        var leftType = val.Arg0!.Type;
        var rightType = val.Arg1!.Type;
        // 字符串拼接：任一侧为 string 时，双侧 ToString 后拼接
        if (leftType.Equals(ScriptType.String) || rightType.Equals(ScriptType.String))
        {
            var left = leftType.Equals(ScriptType.String) ? CoerceString(val.Arg0!.Id) : _intCache[val.Arg0!.Id].ToString();
            var right = rightType.Equals(ScriptType.String) ? CoerceString(val.Arg1!.Id) : _intCache[val.Arg1!.Id].ToString();
            _objCache[val.Id] = string.Concat(left, right);
        }
        else
        {
            var left = PackToValue(val.Arg0!);
            var right = PackToValue(val.Arg1!);
            var result = left.Concat(right); // returns Value
            // 从返回的 Value 中提取原始对象
            if (result.Type.Equals(ScriptType.String))
                _objCache[val.Id] = result.AsString();
            else if (result.AsArray() is ScriptArray arr)
                _objCache[val.Id] = arr;
            else
                _objCache[val.Id] = result;
        }
    }

    private void ExecuteDeepCopyToCache(SsaValue val)
    {
        var cat = GetSlotCategory(val.Arg0!.Type);
        if (cat == SlotCategory.Handle && val.Arg0!.Type.Equals(ScriptType.String))
        {
            // string 不可变，直接复制引用
            _objCache[val.Id] = _objCache[val.Arg0!.Id];
        }
        else
        {
            // 其他类型直接拷贝到同类型缓存
            CopyToCache(val.Id, val.Arg0!.Id, val.Arg0!.Type);
        }
    }

    private void ExecuteLoadFieldToCache(SsaValue val)
    {
        var instance = CoerceStruct(val.Arg0!.Id);
        var field = (EcsFieldDef)val.Aux!;

        if (field.FieldType is ArrayType arrType)
        {
            var items = new Value[arrType.Count];
            for (int i = 0; i < arrType.Count; i++)
                items[i] = RawToValue(instance.GetFieldElement(field, i), arrType.ElementType);
            _objCache[val.Id] = ScriptArray.Create(arrType.ElementType, items);
            return;
        }
        if (field.FieldType is StructType)
        {
            _objCache[val.Id] = instance.GetNested(field);
            return;
        }
        // 基本类型字段：写入对应的类型化缓存
        var raw = instance.GetField(field);
        WriteRawToCache(val.Id, field.FieldType, raw);
    }

    private void ExecuteStoreFieldFromCache(SsaValue val)
    {
        var instance = CoerceStruct(val.Arg0!.Id);
        var field = (EcsFieldDef)val.Aux!;
        var raw = ReadRawForType(val.Arg1!, field.FieldType);
        instance.SetField(field, raw);
    }

    private void ExecuteLoadFieldIndexToCache(SsaValue val)
    {
        var instance = CoerceStruct(val.Arg0!.Id);
        var index = _intCache[val.Arg1!.Id];
        var field = (EcsFieldDef)val.Aux!;
        var elemType = val.Type;

        if (elemType is StructType)
        {
            _objCache[val.Id] = instance.GetNested(field, index);
            return;
        }
        var raw = instance.GetFieldElement(field, index);
        WriteRawToCache(val.Id, elemType, raw);
    }

    private void ExecuteStoreFieldIndexFromCache(SsaValue val)
    {
        var instance = CoerceStruct(val.Arg0!.Id);
        var index = _intCache[val.Arg1!.Id];
        var field = (EcsFieldDef)val.Aux!;
        var elemType = TypeLayout.GetElementType(field.FieldType);
        var raw = ReadRawForType(val.ExtraArgs![0], elemType);
        instance.SetFieldElement(field, index, raw);
    }

    private void ExecuteKeyAction(SsaValue val)
    {
        var key = ((GamePadKeySymbol)val.Aux!).Key;
        if (val.Const.GetBool())
            GamePad?.ReleaseButtons(key);
        else
            GamePad?.PressButtons(key);
    }

    private void ExecuteKeyPress(SsaValue val)
    {
        var key = ((GamePadKeySymbol)val.Aux!).Key;
        var dur = _intCache[val.Arg0!.Id];
        GamePad?.ClickButtons(key, dur, _token);
    }

    private void ExecuteStickAction(SsaValue val)
    {
        var key = ((GamePadKeySymbol)val.Aux!).Key;
        var packed = val.Const.GetInt();
        GamePad?.SetStick(key, (byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF));
    }

    private void ExecuteStickPress(SsaValue val)
    {
        var key = ((GamePadKeySymbol)val.Aux!).Key;
        var dur = _intCache[val.Arg0!.Id];
        var packed = val.Const.GetInt();
        GamePad?.ClickStick(key, (byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), dur, _token);
    }

    private void ExecuteWait(SsaValue val)
    {
        var dur = _intCache[val.Arg0!.Id];
        CustomDelay.Delay(dur, _token);
    }

    private void ExecuteCaptureToCache(SsaValue val)
    {
        var x = _intCache[val.Arg0!.Id];
        var y = _intCache[val.Arg1!.Id];
        var extras = val.ExtraArgs!;
        var w = _intCache[extras[0].Id];
        var h = _intCache[extras[1].Id];
        var result = Frame?.Invoke(x, y, w, h);
        _objCache[val.Id] = result ?? "ERR!!FRAME NOT SUPPORT";
    }

    private void ExecuteOcrInitToCache(SsaValue val)
    {
        var lang = CoerceString(val.Arg0!.Id);
        var dataPath = CoerceString(val.Arg1!.Id);
        var extras = val.ExtraArgs!;
        var engineMode = CoerceString(extras[0].Id);
        var psmode = CoerceString(extras[1].Id);
        var result = OcrInit?.Invoke(lang, dataPath, engineMode, psmode);
        _intCache[val.Id] = result == true ? 1 : 0;
    }

    private void ExecuteOcrToCache(SsaValue val)
    {
        var x = _intCache[val.Arg0!.Id];
        var y = _intCache[val.Arg1!.Id];
        var extras = val.ExtraArgs!;
        var w = _intCache[extras[0].Id];
        var h = _intCache[extras[1].Id];
        var lang = CoerceString(extras[2].Id);
        var result = Ocr?.Invoke(x, y, w, h, lang);
        _objCache[val.Id] = result ?? "ERR!!OCR NOT SUPPORT";
    }

    private void ExecuteRoiToCache(SsaValue val)
    {
        var image = CoerceString(val.Arg0!.Id);
        var x = _intCache[val.Arg1!.Id];
        var extras = val.ExtraArgs!;
        var y = _intCache[extras[0].Id];
        var w = _intCache[extras[1].Id];
        var h = _intCache[extras[2].Id];
        var result = Roi?.Invoke(image, x, y, w, h);
        _objCache[val.Id] = result ?? "ERR!!ROI NOT SUPPORT";
    }

    private void ExecuteRuntimeValueToCache(SsaValue val)
    {
        var name = ((RuntimeValueNameSymbol)val.Aux!).Name;
        if (_runtimeValueGetters.TryGetValue(name, out var getter))
        {
            var v = getter(); // returns Value
            UnpackToCache(val.Id, val.Type, v);
            return;
        }
        throw new Exception($"找不到运行时变量 \"{name}\"");
    }

    private void ExecuteImageLabelToCache(SsaValue val)
    {
        var name = ((RuntimeValueNameSymbol)val.Aux!).Name;
        if (LabelMatch is not { } matcher)
            throw new Exception("图像标签匹配器未初始化");
        _intCache[val.Id] = matcher(name); // LabelMatchDelegate returns int
    }

    // ============ 槽位读写 ============

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

    /// <summary>将类型化缓存中的值写入帧槽位</summary>
    private void WriteCacheToSlot(SlotDesc desc, EvalFrame frame, ScriptType type, SsaValue src)
    {
        switch (desc.Category)
        {
            case SlotCategory.Int:
                frame.Ints[desc.Index] = _intCache[src.Id];
                break;
            case SlotCategory.Long:
                frame.Longs[desc.Index] = _longCache[src.Id];
                break;
            case SlotCategory.Double:
                frame.Doubles[desc.Index] = _doubleCache[src.Id];
                break;
            case SlotCategory.Handle:
                if (frame.Handles[desc.Index] != 0) _heap.Free(frame.Handles[desc.Index]);
                frame.Handles[desc.Index] = StoreHandleObj(type, _objCache[src.Id]);
                break;
        }
    }

    /// <summary>将 object 存入堆 handle</summary>
    private int StoreHandleObj(ScriptType type, object? obj)
    {
        if (type.Equals(ScriptType.String))
            return _heap.StoreString(obj as string ?? "");
        if (type is ArrayType)
            return _heap.StoreArray(((ScriptArray)obj!).Clone());
        if (type is StructType)
        {
            var src = (EcsStruct)obj!;
            return _heap.StoreStruct(new EcsStruct(src.Definition, src.NativePtr));
        }
        throw new InvalidOperationException($"不支持 handle 存储: {type}");
    }

    /// <summary>从缓存读取原始值用于结构体字段写入</summary>
    private object ReadRawForType(SsaValue v, ScriptType type)
    {
        if (type.Equals(ScriptType.Byte)) return (byte)_intCache[v.Id];
        if (type.Equals(ScriptType.Int)) return _intCache[v.Id];
        if (type.Equals(ScriptType.Bool)) return _intCache[v.Id] != 0;
        if (type.Equals(ScriptType.UInt)) return unchecked((uint)_intCache[v.Id]);
        if (type.Equals(ScriptType.UInt64)) return (ulong)_longCache[v.Id];
        if (type.Equals(ScriptType.Ptr)) return new IntPtr(_longCache[v.Id]);
        if (type.Equals(ScriptType.Double)) return _doubleCache[v.Id];
        if (type.Equals(ScriptType.String)) return CoerceString(v.Id);
        if (type is StructType) return CoerceStruct(v.Id);
        return _intCache[v.Id];
    }

    /// <summary>将结构体原始字段值写入类型化缓存</summary>
    private void WriteRawToCache(int id, ScriptType type, object raw)
    {
        if (type.Equals(ScriptType.Byte)) _intCache[id] = (byte)raw;
        else if (type.Equals(ScriptType.Int)) _intCache[id] = (int)raw;
        else if (type.Equals(ScriptType.Bool)) _intCache[id] = (bool)raw ? 1 : 0;
        else if (type.Equals(ScriptType.UInt)) _intCache[id] = unchecked((int)(uint)raw);
        else if (type.Equals(ScriptType.UInt64)) _longCache[id] = unchecked((long)(ulong)raw);
        else if (type.Equals(ScriptType.Ptr)) _longCache[id] = ((IntPtr)raw).ToInt64();
        else if (type.Equals(ScriptType.Double)) _doubleCache[id] = (double)raw;
        else if (type.Equals(ScriptType.String)) _objCache[id] = (string)raw;
        else if (type is StructType) _objCache[id] = (EcsStruct)raw;
    }

    private static int WriteIntSlot(ScriptType type, Value value)
    {
        if (type.Equals(ScriptType.Bool)) return value.AsBool() ? 1 : 0;
        if (type.Equals(ScriptType.Byte)) return value.AsByte();
        if (type.Equals(ScriptType.UInt)) return unchecked((int)value.AsUInt());
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
        try
        {
            EvaluateFunction(func);
            return GetReturnValue();
        }
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
            UnpackToSlot(param.Slot, frame, param.Type, args[i]);
        }

        // 自递归函数需要缓存隔离（同一函数多次入栈，SSA ID 重叠）
        if (_recursiveFunctions.Contains(function))
        {
            var callerInt = _intCache;
            var callerLong = _longCache;
            var callerDouble = _doubleCache;
            var callerObj = _objCache;
            var cacheLen = callerInt.Length;
            _intCache = new int[cacheLen];
            _longCache = new long[cacheLen];
            _doubleCache = new double[cacheLen];
            _objCache = new object?[cacheLen];
            ResetCaches();

            try
            {
                while (true)
                {
                    if (token.IsCancellationRequested)
                        throw new OperationCanceledException(token);

                    _tailCallRequested = false;
                    _localFrames.Push(frame);
                    try { EvaluateFunction(func); }
                    finally { _localFrames.Pop(); }

                    if (!_tailCallRequested)
                        return GetReturnValue();

                    _heap.FreeAll(frame.Handles);
                    Array.Clear(frame.Handles);
                    var parameters = function.Parameters;
                    for (int i = 0; i < parameters.Length; i++)
                        UnpackToSlot(parameters[i].Slot, frame, parameters[i].Type, _tailCallArgs[i]);
                    _intCache = new int[cacheLen];
                    _longCache = new long[cacheLen];
                    _doubleCache = new double[cacheLen];
                    _objCache = new object?[cacheLen];
                    ResetCaches();
                }
            }
            finally
            {
                _intCache = callerInt;
                _longCache = callerLong;
                _doubleCache = callerDouble;
                _objCache = callerObj;
            }
        }

        // 非递归函数：零拷贝路径，SSA ID 全局唯一，缓存可安全共享
        while (true)
        {
            if (token.IsCancellationRequested)
                throw new OperationCanceledException(token);

            _tailCallRequested = false;
            _localFrames.Push(frame);
            try { EvaluateFunction(func); }
            finally { _localFrames.Pop(); }

            if (!_tailCallRequested)
                return GetReturnValue();

            _heap.FreeAll(frame.Handles);
            Array.Clear(frame.Handles);
            var parameters = function.Parameters;
            for (int i = 0; i < parameters.Length; i++)
                UnpackToSlot(parameters[i].Slot, frame, parameters[i].Type, _tailCallArgs[i]);
        }
    }

    /// <summary>将 Value 解包到帧槽位（用于函数参数传入）</summary>
    private void UnpackToSlot(SlotDesc desc, EvalFrame frame, ScriptType type, Value value)
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
                if (frame.Handles[desc.Index] != 0) _heap.Free(frame.Handles[desc.Index]);
                frame.Handles[desc.Index] = StoreHandleObj(type, ExtractHandleObj(type, value));
                break;
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