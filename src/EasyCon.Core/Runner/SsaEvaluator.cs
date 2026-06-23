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
    private readonly Dictionary<string, Func<Value>> _runtimeValueGetters = [];

    // 函数返回值暂存（HandleReturnToCache 写入，GetReturnValue 读取）
    private Value _returnValue;

    // 全局变量：统一 TaggedValue 数组（按 _globalIndex[variable] 索引）
    private TaggedValue[] _globals = [];
    private readonly Dictionary<VariableSymbol, int> _globalIndex = [];

    // 统一 SSA 值缓存：按 SSA 值 ID 索引，替代 4 路类型分裂数组
    // 类型信息在 SSA 操作码（AddInt/AddDouble 等已特化），存储层无需类型分裂
    private TaggedValue[] _cache = [];

    // 预计算的常量值（构造时一次性计算，ResetCaches 时拷贝进 _cache）
    private TaggedValue[] _constCache = [];

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

    /// <summary>启用 JIT 编译执行（默认 false，使用解释器）。</summary>
    public bool UseJit { get; set; }

    // ---- JIT 桥接公开接口 ----
    /// <summary>供 JIT 混合模式访问的统一 SSA 值缓存。</summary>
    public TaggedValue[] Cache => _cache;
    /// <summary>供 JIT 桥接：访问全局变量索引映射。</summary>
    public Dictionary<VariableSymbol, int> GlobalIndex => _globalIndex;
    public TaggedValue[] Globals => _globals;

    private Func<int>? _jitDelegate;

    public SsaEvaluator(SsaProgram program, CancellationToken token)
    {
        _program = program;
        _token = token;
        _localFrames.Push(new EvalFrame(0));

        foreach (var kv in _program.Functions)
            _functions.Add(kv.Key, kv.Value);

        // 收集全局变量
        var globalVarList = new List<VariableSymbol>();
        var seen = new HashSet<VariableSymbol>();
        foreach (var func in _functions.Values)
            CollectGlobalVars(func, globalVarList, seen);

        // 扁平全局槽位分配：单一计数器（类型信息在 SSA 操作码，不在槽位）
        int globalIdx = 0;
        foreach (var gv in globalVarList)
            _globalIndex[gv] = globalIdx++;
        _globals = new TaggedValue[globalIdx];

        RegisterCallables();
        RegisterRuntimeValueGetters();

        // 预分配统一缓存，预计算所有常量
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
        _cache = new TaggedValue[cacheSize];
        _constCache = new TaggedValue[cacheSize];

        // 预计算常量：SSA 常量不依赖控制流，总是可用
        foreach (var func in _functions.Values)
            foreach (var block in func.Blocks)
                foreach (var val in block.Instructions)
                    if (val.IsConstant)
                        PrecomputeConstant(val);

        // 预计算全常量 ArrayInit：跳过运行时装箱开销
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
                    return eval.CallUserFunction(fn, args, tk);
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
        if (UseJit && _program.MainFunction != null)
        {
            try
            {
                return EvaluateJit();
            }
            catch
            {
                // JIT 失败时回退到解释器
            }
        }

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

    private Value EvaluateJit()
    {
        if (_jitDelegate == null)
        {
            var ops = BuildOpsArray();
            _jitDelegate = EasyCon.Script.Jit.SsaJitCompiler.CompileToDelegate(
                _program, this, ops);
        }
        var result = _jitDelegate();
        return Value.FromInt(result);
    }

    private SsaValue[] BuildOpsArray()
    {
        int maxId = 0;
        foreach (var (_, func) in _functions)
            foreach (var block in func.Blocks)
                foreach (var val in block.Instructions.Concat(block.Phis))
                    if (val.Id > maxId) maxId = val.Id;
        if (_program.MainFunction != null)
            foreach (var block in _program.MainFunction.Blocks)
                foreach (var val in block.Instructions.Concat(block.Phis))
                    if (val.Id > maxId) maxId = val.Id;

        var ops = new SsaValue[maxId + 1];
        void Register(SsaFunction func)
        {
            foreach (var block in func.Blocks)
            {
                foreach (var phi in block.Phis) ops[phi.Id] = phi;
                foreach (var inst in block.Instructions) ops[inst.Id] = inst;
            }
        }
        foreach (var (_, func) in _functions) Register(func);
        if (_program.MainFunction != null) Register(_program.MainFunction);
        return ops;
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
                // 1. 执行 Phi 节点：一次 struct copy，无类型 dispatch
                foreach (var phi in current.Phis)
                {
                    int armIdx = FindPredecessorIndex(current, lastBlock);
                    Debug.Assert(phi.ExtraArgs != null && armIdx < phi.ExtraArgs.Count);
                    int srcId = phi.ExtraArgs[armIdx].Id;
                    _cache[phi.Id] = _cache[srcId];
                    var desc = phi.Slot;
                    if (desc.Index >= 0)
                        WriteCacheToSlot(desc, frame, phi);
                }

                // 2. 执行普通指令
                int instCount = current.Instructions.Count;
                for (int i = 0; i < instCount; i++)
                {
                    var inst = current.Instructions[i];

                    ExecuteInstruction(inst, frame);
                }

                // 3. 跳转
                lastBlock = current;

                if (current.IsReturn)
                    return;

                if (current.BranchCondition != null)
                {
                    current = _cache[current.BranchCondition.Id].AsBool()
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

            // ---- 加载/存储：扁平帧，一次 struct copy 无分支 ----
            case SsaOp.LoadLocal:
            {
                var slotIdx = ((LocalVariableSymbol)val.Aux!).Slot.Index;
                _cache[val.Id] = frame.Locals[slotIdx];
                break;
            }
            case SsaOp.StoreLocal:
            {
                var slotIdx = ((LocalVariableSymbol)val.Aux!).Slot.Index;
                StoreLocalSlot(slotIdx, val.Type, val.Arg0!);
                break;
            }
            case SsaOp.LoadGlobal:
                _cache[val.Id] = _globals[_globalIndex[(VariableSymbol)val.Aux!]];
                break;
            case SsaOp.StoreGlobal:
            {
                var gidx = _globalIndex[(VariableSymbol)val.Aux!];
                StoreGlobalSlot(gidx, val.Type, val.Arg0!);
                break;
            }

            // ---- 算术 (int)：直接读 .I32 字段，SSA 操作码已保证类型正确 ----
            case SsaOp.AddInt:
                _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32 + _cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.SubInt:
                _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32 - _cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.MulInt:
                _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32 * _cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.DivInt:
                _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32 / _cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.ModInt:
                _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32 % _cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.RoundDivInt:
                _cache[val.Id] = TaggedValue.FromInt(RoundDiv(_cache[val.Arg0!.Id].I32, _cache[val.Arg1!.Id].I32));
                break;

            // ---- 算术 (uint) ----
            case SsaOp.AddUInt:
                _cache[val.Id] = TaggedValue.FromUInt((uint)_cache[val.Arg0!.Id].I32 + (uint)_cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.SubUInt:
                _cache[val.Id] = TaggedValue.FromUInt((uint)_cache[val.Arg0!.Id].I32 - (uint)_cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.MulUInt:
                _cache[val.Id] = TaggedValue.FromUInt((uint)_cache[val.Arg0!.Id].I32 * (uint)_cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.DivUInt:
                _cache[val.Id] = TaggedValue.FromUInt((uint)_cache[val.Arg0!.Id].I32 / (uint)_cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.ModUInt:
                _cache[val.Id] = TaggedValue.FromUInt((uint)_cache[val.Arg0!.Id].I32 % (uint)_cache[val.Arg1!.Id].I32);
                break;

            // ---- 算术 (double) ----
            case SsaOp.AddDouble:
                _cache[val.Id] = TaggedValue.FromDouble(_cache[val.Arg0!.Id].F64 + _cache[val.Arg1!.Id].F64);
                break;
            case SsaOp.SubDouble:
                _cache[val.Id] = TaggedValue.FromDouble(_cache[val.Arg0!.Id].F64 - _cache[val.Arg1!.Id].F64);
                break;
            case SsaOp.MulDouble:
                _cache[val.Id] = TaggedValue.FromDouble(_cache[val.Arg0!.Id].F64 * _cache[val.Arg1!.Id].F64);
                break;
            case SsaOp.DivDouble:
                _cache[val.Id] = TaggedValue.FromDouble(_cache[val.Arg0!.Id].F64 / _cache[val.Arg1!.Id].F64);
                break;

            // ---- 算术 (uint64) ----
            case SsaOp.AddUInt64:
                _cache[val.Id] = TaggedValue.FromUInt64(_cache[val.Arg0!.Id].AsUInt64() + _cache[val.Arg1!.Id].AsUInt64());
                break;
            case SsaOp.SubUInt64:
                _cache[val.Id] = TaggedValue.FromUInt64(_cache[val.Arg0!.Id].AsUInt64() - _cache[val.Arg1!.Id].AsUInt64());
                break;
            case SsaOp.MulUInt64:
                _cache[val.Id] = TaggedValue.FromUInt64(_cache[val.Arg0!.Id].AsUInt64() * _cache[val.Arg1!.Id].AsUInt64());
                break;
            case SsaOp.DivUInt64:
                _cache[val.Id] = TaggedValue.FromUInt64(_cache[val.Arg0!.Id].AsUInt64() / _cache[val.Arg1!.Id].AsUInt64());
                break;
            case SsaOp.ModUInt64:
                _cache[val.Id] = TaggedValue.FromUInt64(_cache[val.Arg0!.Id].AsUInt64() % _cache[val.Arg1!.Id].AsUInt64());
                break;

            // ---- 位运算 ----
            case SsaOp.AndInt:
                _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32 & _cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.OrInt:
                _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32 | _cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.XorInt:
                _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32 ^ _cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.ShlInt:
                _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32 << _cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.ShrInt:
                _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32 >> _cache[val.Arg1!.Id].I32);
                break;
            case SsaOp.NotInt:
                _cache[val.Id] = TaggedValue.FromInt(~_cache[val.Arg0!.Id].I32);
                break;

            // ---- 比较 (int) → 结果是 bool ----
            case SsaOp.EqInt:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 == _cache[val.Arg1!.Id].I32); break;
            case SsaOp.NeqInt: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 != _cache[val.Arg1!.Id].I32); break;
            case SsaOp.LtInt:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 <  _cache[val.Arg1!.Id].I32); break;
            case SsaOp.LeqInt: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 <= _cache[val.Arg1!.Id].I32); break;
            case SsaOp.GtInt:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 >  _cache[val.Arg1!.Id].I32); break;
            case SsaOp.GeqInt: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 >= _cache[val.Arg1!.Id].I32); break;

            // ---- 比较 (uint) ----
            case SsaOp.EqUInt:  _cache[val.Id] = TaggedValue.FromBool((uint)_cache[val.Arg0!.Id].I32 == (uint)_cache[val.Arg1!.Id].I32); break;
            case SsaOp.NeqUInt: _cache[val.Id] = TaggedValue.FromBool((uint)_cache[val.Arg0!.Id].I32 != (uint)_cache[val.Arg1!.Id].I32); break;
            case SsaOp.LtUInt:  _cache[val.Id] = TaggedValue.FromBool((uint)_cache[val.Arg0!.Id].I32 <  (uint)_cache[val.Arg1!.Id].I32); break;
            case SsaOp.LeqUInt: _cache[val.Id] = TaggedValue.FromBool((uint)_cache[val.Arg0!.Id].I32 <= (uint)_cache[val.Arg1!.Id].I32); break;
            case SsaOp.GtUInt:  _cache[val.Id] = TaggedValue.FromBool((uint)_cache[val.Arg0!.Id].I32 >  (uint)_cache[val.Arg1!.Id].I32); break;
            case SsaOp.GeqUInt: _cache[val.Id] = TaggedValue.FromBool((uint)_cache[val.Arg0!.Id].I32 >= (uint)_cache[val.Arg1!.Id].I32); break;

            // ---- 比较 (double) ----
            case SsaOp.EqDouble:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].F64 == _cache[val.Arg1!.Id].F64); break;
            case SsaOp.NeqDouble: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].F64 != _cache[val.Arg1!.Id].F64); break;
            case SsaOp.LtDouble:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].F64 <  _cache[val.Arg1!.Id].F64); break;
            case SsaOp.LeqDouble: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].F64 <= _cache[val.Arg1!.Id].F64); break;
            case SsaOp.GtDouble:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].F64 >  _cache[val.Arg1!.Id].F64); break;
            case SsaOp.GeqDouble: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].F64 >= _cache[val.Arg1!.Id].F64); break;

            // ---- 比较 (uint64) ----
            case SsaOp.EqUInt64:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].AsUInt64() == _cache[val.Arg1!.Id].AsUInt64()); break;
            case SsaOp.NeqUInt64: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].AsUInt64() != _cache[val.Arg1!.Id].AsUInt64()); break;
            case SsaOp.LtUInt64:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].AsUInt64() <  _cache[val.Arg1!.Id].AsUInt64()); break;
            case SsaOp.LeqUInt64: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].AsUInt64() <= _cache[val.Arg1!.Id].AsUInt64()); break;
            case SsaOp.GtUInt64:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].AsUInt64() >  _cache[val.Arg1!.Id].AsUInt64()); break;
            case SsaOp.GeqUInt64: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].AsUInt64() >= _cache[val.Arg1!.Id].AsUInt64()); break;

            // ---- 比较 (bool/string/ptr/byte) ----
            case SsaOp.EqBool:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 == _cache[val.Arg1!.Id].I32); break;
            case SsaOp.NeqBool: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 != _cache[val.Arg1!.Id].I32); break;
            case SsaOp.EqString:
                _cache[val.Id] = TaggedValue.FromBool(string.Equals(CoerceString(val.Arg0!.Id), CoerceString(val.Arg1!.Id), StringComparison.Ordinal));
                break;
            case SsaOp.NeqString:
                _cache[val.Id] = TaggedValue.FromBool(!string.Equals(CoerceString(val.Arg0!.Id), CoerceString(val.Arg1!.Id), StringComparison.Ordinal));
                break;
            case SsaOp.EqPtr:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I64 == _cache[val.Arg1!.Id].I64); break;
            case SsaOp.NeqPtr: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I64 != _cache[val.Arg1!.Id].I64); break;
            case SsaOp.EqByte:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 == _cache[val.Arg1!.Id].I32); break;
            case SsaOp.NeqByte: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 != _cache[val.Arg1!.Id].I32); break;
            case SsaOp.LtByte:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 <  _cache[val.Arg1!.Id].I32); break;
            case SsaOp.LeqByte: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 <= _cache[val.Arg1!.Id].I32); break;
            case SsaOp.GtByte:  _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 >  _cache[val.Arg1!.Id].I32); break;
            case SsaOp.GeqByte: _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 >= _cache[val.Arg1!.Id].I32); break;

            // ---- 逻辑 ----
            case SsaOp.LogicNot:
                _cache[val.Id] = TaggedValue.FromBool(_cache[val.Arg0!.Id].I32 == 0);
                break;

            // ---- 类型转换 ----
            case SsaOp.ConvBoolToInt:   _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32); break;
            case SsaOp.ConvByteToInt:   _cache[val.Id] = TaggedValue.FromInt(_cache[val.Arg0!.Id].I32); break;
            case SsaOp.ConvIntToUInt:   _cache[val.Id] = TaggedValue.FromUInt(unchecked((uint)_cache[val.Arg0!.Id].I32)); break;
            case SsaOp.ConvIntToUInt64: _cache[val.Id] = TaggedValue.FromUInt64((ulong)_cache[val.Arg0!.Id].I32); break;
            case SsaOp.ConvIntToDouble: _cache[val.Id] = TaggedValue.FromDouble(_cache[val.Arg0!.Id].I32); break;
            case SsaOp.ConvIntToByte:   _cache[val.Id] = TaggedValue.FromByte((byte)_cache[val.Arg0!.Id].I32); break;
            case SsaOp.ConvUIntToUInt64:_cache[val.Id] = TaggedValue.FromUInt64((uint)_cache[val.Arg0!.Id].I32); break;
            case SsaOp.ConvUInt64ToPtr: _cache[val.Id] = TaggedValue.FromPtr(_cache[val.Arg0!.Id].I64); break;
            case SsaOp.ConvPtrToInt:    _cache[val.Id] = TaggedValue.FromInt((int)_cache[val.Arg0!.Id].I64); break;
            case SsaOp.ConvIntToPtr:    _cache[val.Id] = TaggedValue.FromPtr(_cache[val.Arg0!.Id].I32); break;
            case SsaOp.ConvDoubleToInt: _cache[val.Id] = TaggedValue.FromInt((int)_cache[val.Arg0!.Id].F64); break;
            case SsaOp.ConvUInt64ToInt: _cache[val.Id] = TaggedValue.FromInt(unchecked((int)_cache[val.Arg0!.Id].AsUInt64())); break;
            case SsaOp.ConvToString:
                _cache[val.Id] = TaggedValue.FromStringHandle(StoreString(ConvToString(val.Arg0!)));
                break;
            case SsaOp.ConvToInt:
                _cache[val.Id] = TaggedValue.FromInt(ConvToInt(val.Arg0!));
                break;
            case SsaOp.ArrayAppend:
            {
                var arr = CoerceArray(val.Arg0!.Id);
                _cache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(arr.Append(CacheToValue(val.Arg1!))));
                break;
            }

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
                var obj = DerefHandle(val.Arg0!);
                _cache[val.Id] = TaggedValue.FromInt(obj is ScriptArray arr ? arr.Length : obj is string s ? s.Length : 0);
                break;
            }
            case SsaOp.Contains:
                _cache[val.Id] = TaggedValue.FromBool(CacheToValue(val.Arg1!).Contains(CacheToValue(val.Arg0!)));
                break;
            case SsaOp.Concat:
                ExecuteConcatToCache(val);
                break;
            case SsaOp.DeepCopy:
                ExecuteDeepCopyToCache(val);
                break;

            // ---- 结构体 ----
            case SsaOp.StructInit:
                _cache[val.Id] = TaggedValue.FromStructHandle(StoreStruct(new EcsStruct(((StructType)val.Type).Definition)));
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
                _cache[val.Id] = TaggedValue.FromInt(_rand.Next(_cache[val.Arg0!.Id].I32));
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

    // ============ 常量预计算（写入统一常量缓存） ============

    private void PrecomputeConstant(SsaValue val)
    {
        switch (val.Op)
        {
            case SsaOp.ConstBool:   _constCache[val.Id] = TaggedValue.FromBool(val.Const.GetBool()); break;
            case SsaOp.ConstByte:   _constCache[val.Id] = TaggedValue.FromByte(val.Const.GetByte()); break;
            case SsaOp.ConstInt:    _constCache[val.Id] = TaggedValue.FromInt(val.Const.GetInt()); break;
            case SsaOp.ConstUInt:   _constCache[val.Id] = TaggedValue.FromUInt(val.Const.GetUInt()); break;
            case SsaOp.ConstUInt64: _constCache[val.Id] = TaggedValue.FromUInt64(val.Const.GetUInt64()); break;
            case SsaOp.ConstDouble: _constCache[val.Id] = TaggedValue.FromDouble(val.Const.GetDouble()); break;
            case SsaOp.ConstString: _constCache[val.Id] = TaggedValue.FromStringHandle(StoreString(val.ConstString ?? "")); break;
            case SsaOp.ConstPtr:    _constCache[val.Id] = TaggedValue.FromPtr(val.Const.GetPtr()); break;
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
            _constCache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(ScriptArray.Create(elemType, Array.Empty<Value>(), _heap)));
            return;
        }

        // 按元素类型构建：编译期已知，分支预测稳定
        if (elemType.Equals(ScriptType.Byte))
        {
            var data = new byte[count];
            int idx = 0;
            if (val.Arg0 != null) data[idx++] = (byte)_constCache[val.Arg0.Id].I32;
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) data[idx++] = (byte)_constCache[a.Id].I32;
            _constCache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(ScriptArray.CreateDirect(ScriptType.Byte, data)));
        }
        else if (elemType.Equals(ScriptType.Bool))
        {
            var data = new int[count];
            int idx = 0;
            if (val.Arg0 != null) data[idx++] = _constCache[val.Arg0.Id].I32;
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) data[idx++] = _constCache[a.Id].I32;
            _constCache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(ScriptArray.CreateDirect(ScriptType.Bool, data)));
        }
        else if (elemType.Equals(ScriptType.UInt))
        {
            var data = new uint[count];
            int idx = 0;
            if (val.Arg0 != null) data[idx++] = unchecked((uint)_constCache[val.Arg0.Id].I32);
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) data[idx++] = unchecked((uint)_constCache[a.Id].I32);
            _constCache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(ScriptArray.CreateDirect(ScriptType.UInt, data)));
        }
        else if (elemType.Equals(ScriptType.Int))
        {
            var data = new int[count];
            int idx = 0;
            if (val.Arg0 != null) data[idx++] = _constCache[val.Arg0.Id].I32;
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) data[idx++] = _constCache[a.Id].I32;
            _constCache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(new IntArray(data, elemType)));
        }
        else if (elemType.Equals(ScriptType.UInt64))
        {
            var data = new ulong[count];
            int idx = 0;
            if (val.Arg0 != null) data[idx++] = _constCache[val.Arg0.Id].AsUInt64();
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) data[idx++] = _constCache[a.Id].AsUInt64();
            _constCache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(ScriptArray.CreateDirect(ScriptType.UInt64, data)));
        }
        else if (elemType.Equals(ScriptType.Ptr))
        {
            var data = new long[count];
            int idx = 0;
            if (val.Arg0 != null) data[idx++] = _constCache[val.Arg0.Id].I64;
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) data[idx++] = _constCache[a.Id].I64;
            _constCache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(new LongArray(data, elemType)));
        }
        else if (elemType.Equals(ScriptType.Double))
        {
            var data = new double[count];
            int idx = 0;
            if (val.Arg0 != null) data[idx++] = _constCache[val.Arg0.Id].F64;
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) data[idx++] = _constCache[a.Id].F64;
            _constCache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(new DoubleArray(data, elemType)));
        }
        else // string / 其他引用类型
        {
            var items = new Value[count];
            int idx = 0;
            if (val.Arg0 != null) items[idx++] = ConstToValue(val.Arg0);
            if (val.ExtraArgs != null) foreach (var a in val.ExtraArgs) items[idx++] = ConstToValue(a);
            _constCache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(ScriptArray.Create(elemType, items, _heap)));
        }
    }

    /// <summary>从常量缓存读出为 Value（用于 PrecomputeArrayInit 的引用类型 fallback）。
    /// 注意：此方法在构造期间被调用，必须从 _constCache 读取，而非 _cache。</summary>
    private Value ConstToValue(SsaValue v)
    {
        var tv = _constCache[v.Id];
        if (!tv.IsHandle) return tv.ToValue();
        // 引用类型：从 _constCache 中的 handle 解引用还原为 Value
        var obj = tv.Tag switch
        {
            TaggedValue.STRING => (object?)_heap.GetString(tv.Handle),
            TaggedValue.ARRAY  => _heap.GetArray(tv.Handle),
            TaggedValue.STRUCT => _heap.GetStruct(tv.Handle),
            _ => null
        };
        if (obj is string s) return Value.FromString(s);
        if (obj is ScriptArray arr) return Value.FromArray(arr, ((ArrayType)v.Type).ElementType);
        if (obj is EcsStruct es) return Value.FromStruct(es);
        return Value.Void;
    }

    // ============ 统一缓存工具方法 ============

    /// <summary>重置执行缓存，拷贝常量到活跃缓存。</summary>
    private void ResetCaches()
    {
        Array.Copy(_constCache, _cache, _constCache.Length);
    }

    /// <summary>从缓存读出 TaggedValue 并按 SSA 类型还原为 Value（用于函数调用边界、数组元素打包等）。</summary>
    private Value CacheToValue(SsaValue v)
    {
        var tv = _cache[v.Id];
        if (tv.IsHandle)
        {
            var obj = DerefHandle(v);
            if (obj is string s) return Value.FromString(s);
            if (obj is ScriptArray arr) return Value.FromArray(arr, ((ArrayType)v.Type).ElementType);
            if (obj is EcsStruct es) return Value.FromStruct(es);
            return Value.Void;
        }
        return tv.ToValue();
    }

    /// <summary>将 Value 解包到缓存（用于函数返回值、数组元素解包等边界）。</summary>
    private void ValueToCache(int id, ScriptType type, Value value)
    {
        if (type.Equals(ScriptType.Int))    _cache[id] = TaggedValue.FromInt(value.AsInt());
        else if (type.Equals(ScriptType.Bool))   _cache[id] = TaggedValue.FromBool(value.AsBool());
        else if (type.Equals(ScriptType.Byte))   _cache[id] = TaggedValue.FromByte(value.AsByte());
        else if (type.Equals(ScriptType.UInt))   _cache[id] = TaggedValue.FromUInt(value.AsUInt());
        else if (type.Equals(ScriptType.Double)) _cache[id] = TaggedValue.FromDouble(value.AsDouble());
        else if (type.Equals(ScriptType.UInt64)) _cache[id] = TaggedValue.FromUInt64(value.AsUInt64());
        else if (type.Equals(ScriptType.Ptr))    _cache[id] = TaggedValue.FromPtr(value.AsPtr());
        else if (type.Equals(ScriptType.String)) _cache[id] = TaggedValue.FromStringHandle(StoreString(value.AsString()));
        else if (type is ArrayType)              _cache[id] = TaggedValue.FromArrayHandle(StoreArray(value.AsArray().Clone()));
        else if (type is StructType)             _cache[id] = TaggedValue.FromStructHandle(StoreStruct(new EcsStruct(value.AsStruct().Definition, value.AsStruct().NativePtr)));
    }

    /// <summary>通用 int 转换（用于 ConvToInt）。</summary>
    private int ConvToInt(SsaValue v)
    {
        var tv = _cache[v.Id];
        if (tv.IsIntegerCategory) return tv.I32;
        if (tv.IsDoubleCategory)  return (int)tv.F64;
        throw new InvalidCastException($"无法从 {v.Type} 转换为 int");
    }

    /// <summary>通用 string 转换（用于 ConvToString）— 通过 Value.ToString() 确保数组等类型正确格式化。</summary>
    private string ConvToString(SsaValue v)
    {
        var tv = _cache[v.Id];
        if (v.Type.Equals(ScriptType.Bool))   return tv.I32 != 0 ? "true" : "false";
        if (tv.IsIntegerCategory)             return tv.I32.ToString();
        if (v.Type.Equals(ScriptType.UInt64)) return tv.AsUInt64().ToString();
        if (v.Type.Equals(ScriptType.Ptr))    return tv.I64.ToString();
        if (tv.IsDoubleCategory)              return tv.F64.ToString();
        if (tv.IsHandle)                      return CacheToValue(v).ToString();
        return "";
    }

    /// <summary>获取函数返回值（由 HandleReturnToCache 暂存）。</summary>
    private Value GetReturnValue()
    {
        return _returnValue;
    }

    // ============ 缓存访问器（统一缓存版本） ============

    /// <summary>从缓存读出字符串（解引用 handle）。</summary>
    private string CoerceString(int id) => _cache[id].IsString ? _heap.GetString(_cache[id].Handle) : "";
    /// <summary>从缓存读出数组（解引用 handle）。</summary>
    private ScriptArray CoerceArray(int id) => _heap.GetArray(_cache[id].Handle);
    /// <summary>从缓存读出结构体（解引用 handle）。</summary>
    private EcsStruct CoerceStruct(int id) => _heap.GetStruct(_cache[id].Handle);

    /// <summary>从缓存读出引用对象（按 SSA 值类型解引用 handle）。</summary>
    private object? DerefHandle(SsaValue v)
    {
        var tv = _cache[v.Id];
        if (!tv.IsHandle) return null;
        return tv.Tag switch
        {
            TaggedValue.STRING => _heap.GetString(tv.Handle),
            TaggedValue.ARRAY  => _heap.GetArray(tv.Handle),
            TaggedValue.STRUCT => _heap.GetStruct(tv.Handle),
            _ => null
        };
    }

    /// <summary>把对象存入 heap 返回 handle（string 直接共享引用，array 深拷贝保持值语义）。</summary>
    private int StoreString(string s) => _heap.StoreString(s);
    private int StoreArray(ScriptArray arr) => _heap.StoreArray(arr);
    private int StoreStruct(EcsStruct s) => _heap.StoreStruct(s);

    /// <summary>从缓存中读出 TaggedValue 的 src 值，按 type 深拷贝并写入本地槽位（Phi/StoreLocal 使用）。</summary>
    private void StoreLocalSlot(int slotIdx, ScriptType type, SsaValue src)
    {
        var frame = _localFrames.Peek();
        // 引用类型：释放旧 handle，重新存入（保持值语义）
        if (_cache[src.Id].IsHandle)
            frame.Locals[slotIdx] = CopyHandleForStore(type, _cache[src.Id]);
        else
            frame.Locals[slotIdx] = _cache[src.Id];
    }

    private void StoreGlobalSlot(int gidx, ScriptType type, SsaValue src)
    {
        if (_cache[src.Id].IsHandle)
            _globals[gidx] = CopyHandleForStore(type, _cache[src.Id]);
        else
            _globals[gidx] = _cache[src.Id];
    }

    /// <summary>引用类型的 TaggedValue 复制为新的 handle（string 共享，array/struct 深拷贝）。</summary>
    private TaggedValue CopyHandleForStore(ScriptType type, TaggedValue tv)
    {
        if (!tv.IsHandle) return tv;
        return tv.Tag switch
        {
            TaggedValue.STRING => TaggedValue.FromStringHandle(_heap.DeepCopyHandle(tv.Handle)),
            TaggedValue.ARRAY  => TaggedValue.FromArrayHandle(_heap.DeepCopyHandle(tv.Handle)),
            TaggedValue.STRUCT => TaggedValue.FromStructHandle(_heap.DeepCopyHandle(tv.Handle)),
            _ => tv
        };
    }

    /// <summary>把 src 的 TaggedValue 值（已存的）写入 dst 槽位（不深拷贝，仅用于 phi/帧内复制）。</summary>
    private void WriteCacheToSlot(SlotDesc desc, EvalFrame frame, SsaValue src)
    {
        frame.Locals[desc.Index] = _cache[src.Id];
    }

    private void HandleReturnToCache(SsaValue val)
    {
        _returnValue = val.Arg0 != null ? CacheToValue(val.Arg0) : Value.Void;
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
                ? CallUserFunction(function, ReadOnlySpan<Value>.Empty, _token)
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
                if (val.Arg0 != null) args[0] = CacheToValue(val.Arg0);
                if (val.ExtraArgs != null)
                    for (int i = 0; i < val.ExtraArgs.Count; i++)
                        args[i + 1] = CacheToValue(val.ExtraArgs[i]);
                var span = args.AsSpan(0, argCount);
                // 用户函数快速路径：跳过 _callables 字典查找和委托虚分派
                result = _functions.ContainsKey(function)
                    ? CallUserFunction(function, span, _token)
                    : _callables[function].Invoke(span, this, _token);
            }
            finally
            {
                if (rented != null) ArrayPool<Value>.Shared.Return(rented);
            }
        }

        // 将返回值解包到统一缓存
        if (function.ReturnType.Equals(ScriptType.Void)) return;
        ValueToCache(val.Id, function.ReturnType, result);
    }

    private void ExecuteArrayInitToCache(SsaValue val)
    {
        // 全常量 ArrayInit 已在构造时预计算到 _constCache，ResetCaches 拷贝到 _cache
        if (_cache[val.Id].Tag != 0) return;

        var items = new List<Value>();
        if (val.Arg0 != null)
        {
            items.Add(CacheToValue(val.Arg0));
            if (val.ExtraArgs != null)
                foreach (var arg in val.ExtraArgs)
                    items.Add(CacheToValue(arg));
        }
        var elemType = ((ArrayType)val.Type).ElementType;
        _cache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(ScriptArray.Create(elemType, items, _heap)));
    }

    private void ExecuteLoadIndexToCache(SsaValue val)
    {
        var obj = DerefHandle(val.Arg0!);
        var index = _cache[val.Arg1!.Id].I32;

        if (obj is string s)
        {
            if (index < 0 || index >= s.Length)
            {
                var ctx = index > 0 && index <= s.Length ? s[index - 1].ToString() : "?";
                var ctx2 = index > 1 && index <= s.Length ? s[index - 2].ToString() : "?";
                throw new Exception($"数组下标越界: 字符串长度={s.Length}, index={index}, 前5=[{s[..Math.Min(5, s.Length)]}], s[{index - 2}]='{ctx2}', s[{index - 1}]='{ctx}', val.Id={val.Id}, Arg0.Id={val.Arg0!.Id}, Arg0.Op={val.Arg0!.Op}");
            }
            _cache[val.Id] = TaggedValue.FromStringHandle(StoreString(s[index].ToString()));
            return;
        }

        var container = (ScriptArray)obj!;
        if (index < 0 || index >= container.Length) throw new Exception($"数组下标越界: 数组长度={container.Length}, index={index}");
        var elemValue = container[index];
        var elemType = ((ArrayType)val.Arg0!.Type).ElementType;
        ValueToCache(val.Id, elemType, elemValue);
    }

    private void ExecuteStoreIndexFromCache(SsaValue val)
    {
        var container = CoerceArray(val.Arg0!.Id);
        var index = _cache[val.Arg1!.Id].I32;
        var elemType = ((ArrayType)val.Arg0!.Type).ElementType;
        var value = CacheToValue(val.ExtraArgs![0]);

        // 原地修改：若容器是本地/全局变量持有的 handle，直接 SetItem
        if (val.Arg0 is { Op: SsaOp.LoadLocal, Aux: LocalVariableSymbol local })
        {
            var frame = _localFrames.Peek();
            ref var slot = ref frame.Locals[local.Slot.Index];
            if (slot.IsArray)
            {
                _heap.GetArray(slot.Handle).SetItem(index, value);
                return;
            }
        }
        else if (val.Arg0 is { Op: SsaOp.LoadGlobal, Aux: GlobalVariableSymbol gv })
        {
            ref var gslot = ref _globals[_globalIndex[gv]];
            if (gslot.IsArray)
            {
                _heap.GetArray(gslot.Handle).SetItem(index, value);
                return;
            }
        }
        container.SetItem(index, value);
    }

    private void ExecuteSliceToCache(SsaValue val)
    {
        var obj = DerefHandle(val.Arg0!);
        var start = _cache[val.Arg1!.Id].I32;
        int endIdx;
        if (val.ExtraArgs != null && val.ExtraArgs[0].Type.Equals(ScriptType.Int))
            endIdx = _cache[val.ExtraArgs[0].Id].I32;
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
            _cache[val.Id] = TaggedValue.FromStringHandle(StoreString(str[new Range(start, endIdx)]));
        }
        else if (obj is ScriptArray container)
        {
            if (start > container.Length || endIdx > container.Length || start > endIdx)
                throw new Exception($"数组下标越界: 数组长度={container.Length}, start={start}, end={endIdx}");
            _cache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(container.GetRange(start, endIdx - start)));
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
            var left = leftType.Equals(ScriptType.String) ? CoerceString(val.Arg0!.Id) : _cache[val.Arg0!.Id].I32.ToString();
            var right = rightType.Equals(ScriptType.String) ? CoerceString(val.Arg1!.Id) : _cache[val.Arg1!.Id].I32.ToString();
            _cache[val.Id] = TaggedValue.FromStringHandle(StoreString(string.Concat(left, right)));
        }
        else
        {
            var left = CacheToValue(val.Arg0!);
            var right = CacheToValue(val.Arg1!);
            var result = left.Concat(right); // returns Value
            if (result.Type.Equals(ScriptType.String))
                _cache[val.Id] = TaggedValue.FromStringHandle(StoreString(result.AsString()));
            else if (result.AsArray() is ScriptArray arr)
                _cache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(arr));
        }
    }

    private void ExecuteDeepCopyToCache(SsaValue val)
    {
        // 统一缓存下：DeepCopy 仅对引用类型需要深拷贝，其余直接 struct copy
        var src = _cache[val.Arg0!.Id];
        if (src.IsHandle)
            _cache[val.Id] = CopyHandleForStore(val.Arg0!.Type, src);
        else
            _cache[val.Id] = src;
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
            _cache[val.Id] = TaggedValue.FromArrayHandle(StoreArray(ScriptArray.Create(arrType.ElementType, items, _heap)));
            return;
        }
        if (field.FieldType is StructType)
        {
            _cache[val.Id] = TaggedValue.FromStructHandle(StoreStruct(instance.GetNested(field)));
            return;
        }
        // 基本类型字段：写入统一缓存
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
        var index = _cache[val.Arg1!.Id].I32;
        var field = (EcsFieldDef)val.Aux!;
        var elemType = val.Type;

        if (elemType is StructType)
        {
            _cache[val.Id] = TaggedValue.FromStructHandle(StoreStruct(instance.GetNested(field, index)));
            return;
        }
        var raw = instance.GetFieldElement(field, index);
        WriteRawToCache(val.Id, elemType, raw);
    }

    private void ExecuteStoreFieldIndexFromCache(SsaValue val)
    {
        var instance = CoerceStruct(val.Arg0!.Id);
        var index = _cache[val.Arg1!.Id].I32;
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
        var dur = _cache[val.Arg0!.Id].I32;
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
        var dur = _cache[val.Arg0!.Id].I32;
        var packed = val.Const.GetInt();
        GamePad?.ClickStick(key, (byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), dur, _token);
    }

    private void ExecuteWait(SsaValue val)
    {
        var dur = _cache[val.Arg0!.Id].I32;
        CustomDelay.Delay(dur, _token);
    }

    private void ExecuteCaptureToCache(SsaValue val)
    {
        var x = _cache[val.Arg0!.Id].I32;
        var y = _cache[val.Arg1!.Id].I32;
        var extras = val.ExtraArgs!;
        var w = _cache[extras[0].Id].I32;
        var h = _cache[extras[1].Id].I32;
        var result = Frame?.Invoke(x, y, w, h);
        _cache[val.Id] = TaggedValue.FromStringHandle(StoreString(result ?? "ERR!!FRAME NOT SUPPORT"));
    }

    private void ExecuteOcrInitToCache(SsaValue val)
    {
        var lang = CoerceString(val.Arg0!.Id);
        var dataPath = CoerceString(val.Arg1!.Id);
        var extras = val.ExtraArgs!;
        var engineMode = CoerceString(extras[0].Id);
        var psmode = CoerceString(extras[1].Id);
        var result = OcrInit?.Invoke(lang, dataPath, engineMode, psmode);
        _cache[val.Id] = TaggedValue.FromBool(result == true);
    }

    private void ExecuteOcrToCache(SsaValue val)
    {
        var x = _cache[val.Arg0!.Id].I32;
        var y = _cache[val.Arg1!.Id].I32;
        var extras = val.ExtraArgs!;
        var w = _cache[extras[0].Id].I32;
        var h = _cache[extras[1].Id].I32;
        var lang = CoerceString(extras[2].Id);
        var result = Ocr?.Invoke(x, y, w, h, lang);
        _cache[val.Id] = TaggedValue.FromStringHandle(StoreString(result ?? "ERR!!OCR NOT SUPPORT"));
    }

    private void ExecuteRoiToCache(SsaValue val)
    {
        var image = CoerceString(val.Arg0!.Id);
        var x = _cache[val.Arg1!.Id].I32;
        var extras = val.ExtraArgs!;
        var y = _cache[extras[0].Id].I32;
        var w = _cache[extras[1].Id].I32;
        var h = _cache[extras[2].Id].I32;
        var result = Roi?.Invoke(image, x, y, w, h);
        _cache[val.Id] = TaggedValue.FromStringHandle(StoreString(result ?? "ERR!!ROI NOT SUPPORT"));
    }

    private void ExecuteRuntimeValueToCache(SsaValue val)
    {
        var name = ((RuntimeValueNameSymbol)val.Aux!).Name;
        if (_runtimeValueGetters.TryGetValue(name, out var getter))
        {
            var v = getter(); // returns Value
            ValueToCache(val.Id, val.Type, v);
            return;
        }
        throw new Exception($"找不到运行时变量 \"{name}\"");
    }

    private void ExecuteImageLabelToCache(SsaValue val)
    {
        var name = ((RuntimeValueNameSymbol)val.Aux!).Name;
        if (LabelMatch is not { } matcher)
            throw new Exception("图像标签匹配器未初始化");
        _cache[val.Id] = TaggedValue.FromInt(matcher(name)); // LabelMatchDelegate returns int
    }

    // ============ 原始值读写（结构体字段中转） ============

    /// <summary>从缓存读出原始值用于结构体字段写入（按 ScriptType）。</summary>
    private object ReadRawForType(SsaValue v, ScriptType type)
    {
        var tv = _cache[v.Id];
        if (type.Equals(ScriptType.Byte))   return (byte)tv.I32;
        if (type.Equals(ScriptType.Int))    return tv.I32;
        if (type.Equals(ScriptType.Bool))   return tv.I32 != 0;
        if (type.Equals(ScriptType.UInt))   return unchecked((uint)tv.I32);
        if (type.Equals(ScriptType.UInt64)) return tv.AsUInt64();
        if (type.Equals(ScriptType.Ptr))    return new IntPtr(tv.I64);
        if (type.Equals(ScriptType.Double)) return tv.F64;
        if (type.Equals(ScriptType.String)) return CoerceString(v.Id);
        if (type is StructType)             return CoerceStruct(v.Id);
        return tv.I32;
    }

    /// <summary>将结构体原始字段值写入统一缓存（按 ScriptType）。</summary>
    private void WriteRawToCache(int id, ScriptType type, object raw)
    {
        if (type.Equals(ScriptType.Byte))         _cache[id] = TaggedValue.FromByte((byte)raw);
        else if (type.Equals(ScriptType.Int))     _cache[id] = TaggedValue.FromInt((int)raw);
        else if (type.Equals(ScriptType.Bool))    _cache[id] = TaggedValue.FromBool((bool)raw);
        else if (type.Equals(ScriptType.UInt))    _cache[id] = TaggedValue.FromUInt((uint)raw);
        else if (type.Equals(ScriptType.UInt64))  _cache[id] = TaggedValue.FromUInt64((ulong)raw);
        else if (type.Equals(ScriptType.Ptr))     _cache[id] = TaggedValue.FromPtr(((IntPtr)raw).ToInt64());
        else if (type.Equals(ScriptType.Double))  _cache[id] = TaggedValue.FromDouble((double)raw);
        else if (type.Equals(ScriptType.String))  _cache[id] = TaggedValue.FromStringHandle(StoreString((string)raw));
        else if (type is StructType)              _cache[id] = TaggedValue.FromStructHandle(StoreStruct((EcsStruct)raw));
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
        _localFrames.Push(new EvalFrame(layout.SlotCount));
    }

    private void PopFrame()
    {
        var frame = _localFrames.Pop();
        // 释放该帧持有的所有 handle
        var locals = frame.Locals;
        for (int i = 0; i < locals.Length; i++)
            if (locals[i].IsHandle) _heap.Free(locals[i].Handle);
    }

    // ============ 函数调用入口 ============

    /// <summary>
    /// 调用用户函数。若为自递归调用（function == _currentFunc），
    /// 需要缓存隔离：同一函数的 SSA ID 在嵌套调用中会互相覆盖。
    /// </summary>
    private Value CallUserFunction(FunctionSymbol function, ReadOnlySpan<Value> args, CancellationToken token)
    {
        if (!_functions.TryGetValue(function, out var func))
            throw new InvalidOperationException($"未找到函数: {function.Name}");

        if (token.IsCancellationRequested)
            throw new OperationCanceledException(token);

        var layout = function.Layout;
        var frame = new EvalFrame(layout.SlotCount);

        for (int i = 0; i < args.Length; i++)
        {
            var param = function.Parameters[i];
            ValueToSlot(frame, param.Slot.Index, param.Type, args[i]);
        }

        // 自递归调用：同一函数的 SSA ID 在嵌套帧中会互相覆盖，需要缓存隔离
        var isRecursive = _currentFunc != null && function == _currentFunc.Symbol;
        if (isRecursive)
        {
            var savedCache = _cache;
            _cache = new TaggedValue[savedCache.Length];
            ResetCaches();

            _localFrames.Push(frame);
            try { EvaluateFunction(func); }
            finally { _localFrames.Pop(); }

            _cache = savedCache;
        }
        else
        {
            _localFrames.Push(frame);
            try { EvaluateFunction(func); }
            finally { _localFrames.Pop(); }
        }

        return GetReturnValue();
    }

    /// <summary>将 Value 解包到帧槽位（用于函数参数传入）。</summary>
    private void ValueToSlot(EvalFrame frame, int slotIdx, ScriptType type, Value value)
    {
        if (type.Equals(ScriptType.Int))         frame.Locals[slotIdx] = TaggedValue.FromInt(value.AsInt());
        else if (type.Equals(ScriptType.Bool))   frame.Locals[slotIdx] = TaggedValue.FromBool(value.AsBool());
        else if (type.Equals(ScriptType.Byte))   frame.Locals[slotIdx] = TaggedValue.FromByte(value.AsByte());
        else if (type.Equals(ScriptType.UInt))   frame.Locals[slotIdx] = TaggedValue.FromUInt(value.AsUInt());
        else if (type.Equals(ScriptType.UInt64)) frame.Locals[slotIdx] = TaggedValue.FromUInt64(value.AsUInt64());
        else if (type.Equals(ScriptType.Ptr))    frame.Locals[slotIdx] = TaggedValue.FromPtr(value.AsPtr());
        else if (type.Equals(ScriptType.Double)) frame.Locals[slotIdx] = TaggedValue.FromDouble(value.AsDouble());
        else if (type.Equals(ScriptType.String)) frame.Locals[slotIdx] = TaggedValue.FromStringHandle(StoreString(value.AsString()));
        else if (type is ArrayType)              frame.Locals[slotIdx] = TaggedValue.FromArrayHandle(StoreArray(value.AsArray().Clone()));
        else if (type is StructType)
        {
            var src = value.AsStruct();
            frame.Locals[slotIdx] = TaggedValue.FromStructHandle(StoreStruct(new EcsStruct(src.Definition, src.NativePtr)));
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
        // 释放全局槽位中的所有 handle
        for (int i = 0; i < _globals.Length; i++)
            if (_globals[i].IsHandle) _heap.Free(_globals[i].Handle);
        // 释放所有帧中的 handle
        foreach (var frame in _localFrames)
            for (int i = 0; i < frame.Locals.Length; i++)
                if (frame.Locals[i].IsHandle) _heap.Free(frame.Locals[i].Handle);
        _localFrames.Clear();
        _heap.FreeAll();
    }

    // ============ JIT 桥接公开接口 ============

    /// <summary>供 JIT 混合模式调用：执行单条指令。</summary>
    public void ExecuteInstructionPublic(SsaValue val)
    {
        var frame = _localFrames.Count > 0 ? _localFrames.Peek() : new EvalFrame(0);
        ExecuteInstruction(val, frame);
    }

    /// <summary>供 JIT 混合模式调用：重置缓存。</summary>
    public void ResetCachesPublic()
    {
        ResetCaches();
    }

    /// <summary>供 JIT 桥接：按函数符号查找并调用用户函数。</summary>
    public Value CallFunction(FunctionSymbol symbol, ReadOnlySpan<Value> args)
    {
        return CallUserFunction(symbol, args, _token);
    }

    /// <summary>供 JIT 桥接：判断是否为用户定义函数。</summary>
    public bool IsUserFunction(FunctionSymbol symbol) => _functions.ContainsKey(symbol);

    /// <summary>供 JIT 桥接：调用外部函数（ICallable）。</summary>
    public Value CallExternal(FunctionSymbol symbol, ReadOnlySpan<Value> args)
    {
        return _callables[symbol].Invoke(args, this, _token);
    }
}