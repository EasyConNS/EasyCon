using EasyCon.Script.Runtime;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EasyCon.Script.Bytecode;

/// <summary>每个模块的常量池（编码期积累，链接期合并去重，LoadK 的 Bx 由链接器重写）。</summary>
public sealed class ModulePool
{
    public List<EcsConst> Consts { get; } = new();
    Dictionary<(byte Tag, long Bits, string? Str), int> _index = new();

    public int Add(EcsConst c)
    {
        var bits = c.Tag == EcsTag.Double ? BitConverter.DoubleToInt64Bits(c.Float64) : c.Int64;
        var key = (c.Tag, bits, c.Str);
        if (_index.TryGetValue(key, out var existing))
            return existing;
        _index[key] = Consts.Count;
        Consts.Add(c);
        return Consts.Count - 1;
    }

    public int AddString(string s) => Add(EcsConst.FromString(s));
}

/// <summary>
/// 模块编码上下文：函数 ID、全局槽、原生表、导入表均为**模块局部**
/// （docs/EcmEcxFormat.md §4.1——链接期统一重写为镜像全局索引）。
/// </summary>
public sealed class ModuleEncodeContext
{
    public required SsaProgram Program;
    public required string ModuleName;
    /// <summary>本模块函数 → 模块局部 fid（值相等键，A-07）。</summary>
    public required Dictionary<FunctionSymbol, int> LocalFuncIds;
    public required List<EcsImport> Imports;
    public required Dictionary<(string Name, int NParams), int> ImportIds;
    /// <summary>模块私有全局（首现序 = 模块局部槽）。</summary>
    public required List<EcsGlobal> Globals;
    public required Dictionary<GlobalVariableSymbol, int> GlobalSlots;
    public required List<EcsNative> Natives;
    public required Dictionary<string, int> NativeIds;
    /// <summary>程序级类型表（结构体 sid 全程序一致）。</summary>
    public required Dictionary<string, int> StructIds;
    public required ModulePool Pool;
    /// <summary>
    /// 模块编译模式（ModuleSystem.md M5）：依赖接口的函数符号（值相等键）。
    /// 命中 → Call ext = 0x80000000|importIdx；不在其中的非本模块符号仍走 CallN 原生。
    /// v1 合并编译路径不设置（null），行为不变。
    /// </summary>
    public IReadOnlySet<FunctionSymbol>? ExternalFunctions;

    public int ImportId(string name, int nparams)
    {
        var key = (name, nparams);
        if (!ImportIds.TryGetValue(key, out var id))
        {
            id = Imports.Count;
            ImportIds[key] = id;
            Imports.Add(new EcsImport { Name = name, NParams = nparams });
        }
        return id;
    }

    public int NativeId(string name)
    {
        if (!NativeIds.TryGetValue(name, out var id))
        {
            id = Natives.Count;
            NativeIds[name] = id;
            Natives.Add(new EcsNative { Name = name });
        }
        return id;
    }

    public int GlobalSlot(GlobalVariableSymbol g)
    {
        if (!GlobalSlots.TryGetValue(g, out var slot))
        {
            slot = Globals.Count;
            GlobalSlots[g] = slot;
            Globals.Add(new EcsGlobal { Name = g.Name, Module = ModuleName, Type = BytecodeEncoder.ToTypeCode(g.Type) });
        }
        return slot;
    }
}

/// <summary>
/// SSA → VM2 字节码编码器（docs/VM2.md §5）。
/// </summary>
public static partial class BytecodeEncoder
{
    public static EcsFunction Encode(
        SsaFunction fn, FunctionSymbol symbol, int localFid, ModuleEncodeContext ctx)
    {
        var enc = new Encoder(fn, symbol, localFid, ctx);
        enc.Run();
        return enc.Result;
    }

    sealed partial class Encoder
    {
        readonly SsaFunction _fn;
        readonly FunctionSymbol _symbol;
        readonly int _fid;
        readonly ModuleEncodeContext _ctx;
        readonly List<uint> _code = new();
        readonly Dictionary<SsaValue, int> _slots = new();
        readonly Dictionary<SsaBlock, BlockUseInfo> _useInfo = new();
        readonly HashSet<SsaValue> _pooled = new();
        readonly Dictionary<SsaValue, int> _totalReads = new();
        readonly Dictionary<SsaBlock, int> _blockStart = new();
        readonly List<Fixup> _fixups = new();
        readonly List<int> _labels = new();
        readonly List<int> _poolFree = new();
        int _next;
        int _poolNext;
        int _scratch = -1;
        int _stagingBase;
        int _maxArity;
        int _receiveSlot;
        int _neqTemp = -1;   // Neq = Eq+Not 的共享中间槽（发射原子，不嵌套）

        public EcsFunction Result = null!;

        public Encoder(SsaFunction fn, FunctionSymbol symbol, int localFid, ModuleEncodeContext ctx)
        {
            _fn = fn; _symbol = symbol; _fid = localFid; _ctx = ctx;
            _module = ctx.ModuleName;
            _pool = ctx.Pool;
        }

        readonly string _module;
        readonly ModulePool _pool;

        // ---- 槽位与发射工具 ----

        int Slot(SsaValue v) => _slots[v];

        static int SymSlot(LocalVariableSymbol s)
        {
            var idx = s.Slot.Index;
            if (idx < 0) throw new BytecodeException(new[] { new BytecodeDiagnostic($"符号 {s.Name} 未分配槽位（SSA 不变量破坏）", null, 0) });
            return idx;
        }

        void Emit(uint word) => _code.Add(word);

        static uint Word(EcsOpcode op, int a, int b, int c)
            => (uint)op | (uint)(a & 0xFF) << 8 | (uint)(b & 0xFF) << 16 | (uint)(c & 0xFF) << 24;

        void EmitIabc(EcsOpcode op, int a, int b, int c)
        {
            if (EcsFormat.Get(op) != EcsInsFormat.Iabc)
                throw Fail($"{op} 登记格式为 {EcsFormat.Get(op)}，不能按 Iabc 发射（与 EcsFormat 表不一致）");
            Emit(Word(op, a, b, c));
        }

        void EmitAbx(EcsOpcode op, int a, int bx)
        {
            if (EcsFormat.Get(op) != EcsInsFormat.ABx)
                throw Fail($"{op} 登记格式为 {EcsFormat.Get(op)}，不能按 ABx 发射（与 EcsFormat 表不一致）");
            if (bx < 0 || bx > 0xFFFF)
                throw Fail($"{op} 的 Bx 越界: {bx}");
            Emit(Word(op, a, bx & 0xFF, (bx >> 8) & 0xFF));
        }

        void EmitAsBx(EcsOpcode op, int a, int sbx)
        {
            if (EcsFormat.Get(op) != EcsInsFormat.AsBx)
                throw Fail($"{op} 登记格式为 {EcsFormat.Get(op)}，不能按 AsBx 发射（与 EcsFormat 表不一致）");
            if (sbx < short.MinValue || sbx > short.MaxValue)
                throw Fail($"{op} 的 sBx 越界: {sbx}");
            Emit(Word(op, a, sbx & 0xFF, (sbx >> 8) & 0xFF));
        }

        // EXT 后随字是数据（唯一权威集合见 EcsFormat 表）：发射即登记，扫描侧经 ExtWords 步进。
        void EmitExt(EcsOpcode op, int a, int b, int c, uint ext)
        {
            if (EcsFormat.ExtWords(op) != 1)
                throw Fail($"{op} 非 EXT 指令，不能携带后随数据字（与 EcsFormat 表不一致）");
            Emit(Word(op, a, b, c));
            Emit(ext);
        }

        BytecodeException Fail(string message)
            => new(new[] { new BytecodeDiagnostic(message, _symbol.Name, _code.Count) });

        // ---- 标签与跳转修复 ----

        const int FixJmpBlock = 0;   // IsJ s24 → 目标块
        const int FixJptBlock = 1;   // AsBx s16 → 目标块
        const int FixJpfBlock = 2;
        const int FixJmpLabel = 3;   // IsJ s24 → 标签
        const int FixJptLabel = 4;   // AsBx s16 → 标签
        const int FixJpfLabel = 5;

        int NewLabel()
        {
            _labels.Add(-1);
            return _labels.Count - 1;
        }

        void MarkLabel(int label) => _labels[label] = _code.Count;

        void EmitJmpToBlock(SsaBlock target) { _fixups.Add(new Fixup(_code.Count, FixJmpBlock, target)); Emit(Word(EcsOpcode.Jmp, 0, 0, 0)); }
        void EmitJptToBlock(int cond, SsaBlock target) { _fixups.Add(new Fixup(_code.Count, FixJptBlock, target)); Emit(Word(EcsOpcode.Jpt, cond, 0, 0)); }
        void EmitJpfToBlock(int cond, SsaBlock target) { _fixups.Add(new Fixup(_code.Count, FixJpfBlock, target)); Emit(Word(EcsOpcode.Jpf, cond, 0, 0)); }
        void EmitJmpToLabel(int label) { _fixups.Add(new Fixup(_code.Count, FixJmpLabel, label)); Emit(Word(EcsOpcode.Jmp, 0, 0, 0)); }

        // ---- 主流程 ----

        public void Run()
        {
            AssignSlots();
            EmitBlocks();
            Patch();

            if (_poolNext > 255)
                throw Fail($"帧槽位超出 255 上限: {_poolNext}");
            Result = new EcsFunction
            {
                Name = _symbol.Name,
                Module = _module,
                NParams = _symbol.Parameters.Length,
                NSlots = _poolNext,
                HasReturn = !_symbol.ReturnType.Equals(ScriptType.Void),
                Code = _code,
            };
        }

        // ---- 块发射 ----

        void EmitBlocks()
        {
            foreach (var block in _fn.Blocks)
            {
                _blockStart[block] = _code.Count;
                var info = Info(block);

                // 块首惰性物化本块引用的常量（含 phi 臂读取）——每块独立池槽
                foreach (var c in info.Consts)
                {
                    _slots[c] = PoolAlloc();
                    EmitLoadConst(c);
                }

                foreach (var inst in block.Instructions)
                {
                    switch (inst.Op)
                    {
                        case SsaOp.Phi:
                        case SsaOp.CondBranch:
                        case SsaOp.Branch:
                        case SsaOp.Return:
                        case SsaOp.Nop:
                            continue;   // 终结符/标记，由 EmitTerminator 处理
                        case SsaOp.ConstBool:
                        case SsaOp.ConstByte:
                        case SsaOp.ConstInt:
                        case SsaOp.ConstUInt:
                        case SsaOp.ConstUInt64:
                        case SsaOp.ConstDouble:
                        case SsaOp.ConstString:
                        case SsaOp.ConstPtr:
                            continue;   // 常量在块首按需物化（这里物理占位的常量指令不发射）
                        default:
                            // 先结算操作数末次读取（槽位可被结果复用），再为块内结果取槽
                            ReleaseOperands(block, inst);
                            if (_pooled.Contains(inst))
                                _slots[inst] = PoolAlloc();
                            EmitInst(inst);
                            break;
                    }
                }
                EmitTerminator(block);
            }
#if DEBUG
            foreach (var kv in _useInfo)
                foreach (var uc in kv.Value.UseCount)
                    if (uc.Value != 0 && _pooled.Contains(uc.Key))
                        Console.Error.WriteLine($"[slots] 未结算: 块{kv.Key.Id} v{uc.Key.Id} {uc.Key.Op} remain={uc.Value} pooled={_pooled.Contains(uc.Key)}");
#endif
        }

        void EmitTerminator(SsaBlock block)
        {
            if (block.IsReturn)
            {
                SsaValue? retVal = null;
                foreach (var inst in block.Instructions)
                    if (inst.Op == SsaOp.Return) { retVal = inst.Arg0; break; }
                if (retVal != null)
                {
                    EmitIabc(EcsOpcode.Ret, Slot(retVal), 0, 0);
                    ReleaseDying(block, retVal);
                }
                else
                    EmitIabc(EcsOpcode.Ret0, 0, 0, 0);
                return;
            }

            if (block.BranchCondition != null)
            {
                var cond = Slot(block.BranchCondition);
                var t = block.TrueSuccessor!;
                var f = block.FalseSuccessor!;
                bool tPhi = t.Phis.Count > 0 && t.Predecessors.Contains(block);
                bool fPhi = f.Phis.Count > 0 && f.Predecessors.Contains(block);

                if (t == f)
                {
                    if (tPhi) EmitEdgeCopies(t, block);   // 防御：只发一份（A-03）
                    EmitJmpToBlock(t);
                }
                else if (!tPhi && !fPhi)
                {
                    EmitJptToBlock(cond, t);
                    EmitJpfToBlock(cond, f);
                }
                else if (tPhi && !fPhi)
                {
                    int skip = NewLabel();
                    EmitFixJpf(cond, skip);
                    EmitEdgeCopies(t, block);
                    EmitJmpToBlock(t);
                    MarkLabel(skip);
                    EmitJmpToBlock(f);
                }
                else if (!tPhi && fPhi)
                {
                    int skip = NewLabel();
                    EmitFixJpt(cond, skip);
                    EmitEdgeCopies(f, block);
                    EmitJmpToBlock(f);
                    MarkLabel(skip);
                    EmitJmpToBlock(t);
                }
                else
                {
                    int skip = NewLabel();
                    EmitFixJpf(cond, skip);
                    EmitEdgeCopies(t, block);
                    EmitJmpToBlock(t);
                    MarkLabel(skip);
                    EmitEdgeCopies(f, block);
                    EmitJmpToBlock(f);
                }
                ReleaseDying(block, block.BranchCondition);   // 条件已被终止符全部读取
                return;
            }

            if (block.JumpTarget != null)
            {
                var j = block.JumpTarget;
                if (j.Phis.Count > 0 && j.Predecessors.Contains(block))
                    EmitEdgeCopies(j, block);
                EmitJmpToBlock(j);
                return;
            }

            throw Fail("块无终止指令（SSA 不变量破坏，A-01）");
        }

        // 条件跳转到标签：Kind 必须用 Label 变体（Patch 按 Kind 区分块/标签转型）
        void EmitFixJpf(int cond, int label) { _fixups.Add(new Fixup(_code.Count, FixJpfLabel, label)); Emit(Word(EcsOpcode.Jpf, cond, 0, 0)); }
        void EmitFixJpt(int cond, int label) { _fixups.Add(new Fixup(_code.Count, FixJptLabel, label)); Emit(Word(EcsOpcode.Jpt, cond, 0, 0)); }

        void EmitEdgeCopies(SsaBlock successor, SsaBlock from)
        {
            int armIdx = successor.Predecessors.IndexOf(from);
            if (armIdx < 0)
                throw Fail("phi 前驱缺失（SSA 不变量破坏，A-01）");

            var moves = new List<(int Dst, int Src)>();
            foreach (var phi in successor.Phis)
            {
                var arm = phi.ExtraArgs![armIdx];
                if (!_slots.ContainsKey(phi))
                {
                    ReleaseDying(from, arm);   // 死 φ（防线 1）：副本不发射，臂读取记账照常结算（块内槽池不变量不受影响）
                    continue;
                }
                moves.Add((Slot(phi), Slot(arm)));
            }
            EmitParallelCopy(moves);
            // 副本已发射 = 臂读取完成，结算块内槽（t==f 防御路径只发一份，也只结算一次）
            foreach (var phi in successor.Phis)
                ReleaseDying(from, phi.ExtraArgs![armIdx]);
        }

        /// <summary>并行副本（Sessa 算法；交换环经 scratch 槽打破，docs/VM2.md §5.3）。</summary>
        void EmitParallelCopy(List<(int Dst, int Src)> moves)
        {
            var pending = new List<(int Dst, int Src)>();
            foreach (var m in moves)
                if (m.Dst != m.Src)
                    pending.Add(m);

            while (pending.Count > 0)
            {
                int picked = -1;
                for (int i = 0; i < pending.Count; i++)
                {
                    bool dstIsSrc = false;
                    foreach (var other in pending)
                        if (other.Src == pending[i].Dst) { dstIsSrc = true; break; }
                    if (!dstIsSrc) { picked = i; break; }
                }

                if (picked >= 0)
                {
                    var m = pending[picked];
                    EmitIabc(EcsOpcode.Move, m.Dst, m.Src, 0);
                    pending.RemoveAt(picked);
                }
                else
                {
                    if (_scratch < 0)
                        throw Fail("需要 scratch 槽但函数无 phi（内部错误）");
                    var m0 = pending[0];
                    EmitIabc(EcsOpcode.Move, _scratch, m0.Dst, 0);
                    for (int i = 0; i < pending.Count; i++)
                        if (pending[i].Src == m0.Dst)
                            pending[i] = (pending[i].Dst, _scratch);
                }
            }
        }

        void Patch()
        {
            foreach (var fixup in _fixups)
            {
                int target = fixup.Kind switch
                {
                    FixJmpBlock or FixJptBlock or FixJpfBlock => _blockStart[(SsaBlock)fixup.Target],
                    _ => _labels[(int)fixup.Target],
                };
                int offset = target - (fixup.WordIdx + 1);
                uint word = _code[fixup.WordIdx];

                switch (fixup.Kind)
                {
                    case FixJmpBlock or FixJmpLabel:
                        {
                            if (offset < -8388608 || offset > 8388607)
                                throw Fail("Jmp 偏移超出 s24（函数过大）");
                            _code[fixup.WordIdx] = (word & 0xFF) | (uint)(offset & 0xFFFFFF) << 8;
                            break;
                        }
                    case FixJptBlock or FixJpfBlock or FixJptLabel or FixJpfLabel:
                        {
                            if (offset < short.MinValue || offset > short.MaxValue)
                                throw Fail("条件跳转偏移超出 s16（函数过大，A-04）");
                            _code[fixup.WordIdx] = (word & 0xFFFF) | (uint)(offset & 0xFFFF) << 16;
                            break;
                        }
                }
            }
        }

        sealed class Fixup
        {
            public readonly int WordIdx;
            public readonly int Kind;
            public readonly object Target;

            public Fixup(int wordIdx, int kind, object target)
            {
                WordIdx = wordIdx; Kind = kind; Target = target;
            }
        }

    }

    /// <summary>把 ScriptType 映射为 ECX 类型码（序列化共用）。</summary>
    public static EcsTypeCode ToTypeCode(ScriptType type)
    {
        if (type.Equals(ScriptType.Void)) return EcsTypeCode.Void;
        if (type.Equals(ScriptType.Bool)) return EcsTypeCode.Bool;
        if (type.Equals(ScriptType.Byte)) return EcsTypeCode.Byte;
        if (type.Equals(ScriptType.Int)) return EcsTypeCode.Int;
        if (type.Equals(ScriptType.UInt)) return EcsTypeCode.UInt;
        if (type.Equals(ScriptType.UInt64)) return EcsTypeCode.UInt64;
        if (type.Equals(ScriptType.Double)) return EcsTypeCode.Double;
        if (type.Equals(ScriptType.String)) return EcsTypeCode.String;
        if (type.Equals(ScriptType.Ptr)) return EcsTypeCode.Ptr;
        if (type is StructType) return EcsTypeCode.Struct;
        return EcsTypeCode.Any;
    }

    /// <summary>
    /// extern 符号的原生表键：非 internal 库 → "库!导出名"（FFI 按名分发）；
    /// 内建等 → 声明名。镜像 Natives 表与桌面/单片机宿主共用此命名。
    /// </summary>
    public static string BuildNativeName(FunctionSymbol sym)
    {
        if (!string.Equals(sym.LibraryName, "internal", StringComparison.Ordinal))
            return $"{sym.LibraryName}!{sym.ExternalName}";
        return sym.Name;
    }
}