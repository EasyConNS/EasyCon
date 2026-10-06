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
    public required Dictionary<(string? OriginModule, string Name, int NParams), int> ImportIds;
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
    /// <summary>Call 的无接收槽哨兵（varint 直接编码，不再有 255 双含义）。</summary>
    public const int NoSlot = EcsInstruction.NoSlot;

    readonly Dictionary<string, int> _hiddenGlobalSlots = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _hiddenGlobalOwners = new(StringComparer.Ordinal);

    /// <summary>模板全局的注册结果：模板数组槽 + 构建守卫槽（均模块局部全局槽）。</summary>
    public readonly record struct ArrayTemplateSlots(int Template, int Guard);

    /// <summary>全常量数组字面量 → 常量模板全局（内容键去重，模块级共享；见 BytecodeEncoder.ArrayTemplate）。</summary>
    public readonly Dictionary<string, ArrayTemplateSlots> ArrayTemplates = new(StringComparer.Ordinal);

    /// <summary>铸造模块私有隐藏全局（用户标识符不含 '#'，无碰撞面）。同名幂等。</summary>
    public int MintGlobal(string name, EcsTypeCode type)
    {
        if (_hiddenGlobalSlots.TryGetValue(name, out var slot))
            return slot;
        slot = Globals.Count;
        _hiddenGlobalSlots[name] = slot;
        Globals.Add(new EcsGlobal { Name = name, Module = ModuleName, Type = type });
        return slot;
    }

    /// <summary>
    /// 取（或建）内容键对应的常量模板：模板数组全局 + 守卫全局（构建一次标志）。
    /// 命名 = "#tpl" + FNV-1a64(内容键)；FNV 碰撞时以 -n 后缀消歧（_hiddenGlobalOwners 校验内容归属）。
    /// </summary>
    public ArrayTemplateSlots ArrayTemplate(string contentKey)
    {
        if (ArrayTemplates.TryGetValue(contentKey, out var hit))
            return hit;
        var baseName = $"#tpl{Fnv1a64(contentKey):x16}";
        var name = baseName;
        var suffix = 0;
        while (_hiddenGlobalOwners.TryGetValue(name, out var owner) && owner != contentKey)
            name = $"{baseName}-{++suffix}";
        var slots = new ArrayTemplateSlots(
            MintGlobal(name, EcsTypeCode.Array),
            MintGlobal(name + "!ok", EcsTypeCode.Int));
        ArrayTemplates[contentKey] = slots;
        _hiddenGlobalOwners[name] = contentKey;
        _hiddenGlobalOwners[name + "!ok"] = contentKey;
        return slots;
    }

    static ulong Fnv1a64(string s)
    {
        const ulong Prime = 0x100000001B3;
        ulong hash = 0xCBF29CE484222325;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= Prime;
        }
        return hash;
    }

    public int ImportId(string name, int nparams) => ImportId(null, name, nparams);

    public int ImportId(string? originModule, string name, int nparams)
    {
        var key = (originModule, name, nparams);
        if (!ImportIds.TryGetValue(key, out var id))
        {
            id = Imports.Count;
            ImportIds[key] = id;
            // 导入名模块限定（"模块!函数"）：层级命名下跨模块同名导入在导入表中可区分；
            // 链接器按限定名解析到目标模块导出。无来源（历史路径）保持裸名。
            Imports.Add(new EcsImport { Name = originModule != null ? $"{originModule}!{name}" : name, NParams = nparams });
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
        /// <summary>唯一指令流（解码形态；线格式只是序列化投影——SingleStreamFormat §2）。</summary>
        readonly List<EcsInstruction> _code = new();
        readonly Dictionary<SsaValue, int> _slots = new();
        /// <summary>存活局部槽位重编号（旧帧槽 → 压缩槽；AssignSlots 步骤 0 构建，SymSlot 消费）。</summary>
        readonly Dictionary<int, int> _localSlotRemap = new();
        readonly Dictionary<SsaBlock, BlockUseInfo> _useInfo = new();
        readonly HashSet<SsaValue> _pooled = new();
        readonly Dictionary<SsaValue, int> _totalReads = new();
        /// <summary>结算计划：直接发射的指令 → 待结算操作数（登记遍历同点推导，发射只回放）。</summary>
        readonly Dictionary<SsaValue, SsaValue[]> _instReleases = new();
        /// <summary>结算计划：块终结符 → 待结算值（返回值/分支条件/出边 φ 臂，含死 φ 臂）。</summary>
        readonly Dictionary<SsaBlock, SsaValue[]> _terminatorReleases = new();
        /// <summary>循环不变量常量外提：preheader 块 → 外提常量（HoistLoopInvariantConsts 构建）。</summary>
        readonly Dictionary<SsaBlock, List<SsaValue>> _hoistedConsts = new();
        /// <summary>已外提常量全集（专属槽，不池化、不逐块重物化）。</summary>
        readonly HashSet<SsaValue> _hoisted = new();
        /// <summary>行号表构建缓冲（稀疏：仅行变化处记录，发射点由 EmitInst 驱动；单位 = 指令下标）。</summary>
        readonly List<int> _linePcs = new();
        readonly List<int> _lineLines = new();
        int _currentEmitLine;
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
        int _tplGuard = -1;  // 常量模板构建暂存槽（守卫标志/模板数组/当前分块；发射原子，不嵌套）
        int _tplArr = -1;
        int _tplChunk = -1;
        int _tplChunkSize;   // 模板分块构建的单块元素数（staging 槽预算内取满）

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

        int SymSlot(LocalVariableSymbol s)
        {
            var idx = s.Slot.Index;
            if (idx < 0) throw new BytecodeException(new[] { new BytecodeDiagnostic($"符号 {s.Name} 未分配槽位（SSA 不变量破坏）", null, 0) });
            if (!_localSlotRemap.TryGetValue(idx, out var mapped))
                throw Fail($"符号 {s.Name} 的帧槽 {idx} 不在存活局部表（登记/发射遍历漂移）");
            return mapped;
        }

        void Emit(in EcsInstruction ins) => _code.Add(ins);

        // 格式感知发射：仍以 EcsFormat 表自检「操作码与发射形态匹配」（漏登 fail-fast 原样保留），
        // 只是写出的目标从打包字变成解码形态结构体（SingleStreamFormat §2）。
        void EmitIabc(EcsOpcode op, int a, int b, int c)
        {
            if (EcsFormat.Get(op) != EcsInsFormat.Iabc)
                throw Fail($"{op} 登记格式为 {EcsFormat.Get(op)}，不能按 Iabc 发射（与 EcsFormat 表不一致）");
            CheckIabcOperands(op, a, b, c);
            Emit(new EcsInstruction(op, a, b, c));
        }

        void EmitAbx(EcsOpcode op, int a, int bx)
        {
            if (EcsFormat.Get(op) != EcsInsFormat.ABx)
                throw Fail($"{op} 登记格式为 {EcsFormat.Get(op)}，不能按 ABx 发射（与 EcsFormat 表不一致）");
            if (bx < 0 || bx > 0xFFFF)
                throw Fail($"{op} 的 Bx 越界: {bx}");   // 常量池/类型表 16 位上限是语义约束（pinned 句柄编码前提）
            Emit(new EcsInstruction(op, a, bx));
        }

        void EmitAsBx(EcsOpcode op, int a, int sbx)
        {
            if (EcsFormat.Get(op) != EcsInsFormat.AsBx)
                throw Fail($"{op} 登记格式为 {EcsFormat.Get(op)}，不能按 AsBx 发射（与 EcsFormat 表不一致）");
            if (sbx < short.MinValue || sbx > short.MaxValue)
                throw Fail($"{op} 的 sBx 越界: {sbx}（v3 定长 AsBx=s16）");
            Emit(new EcsInstruction(op, a, sbx));
        }

        // EXT 数据字是指令的一部分（解码形态字段），不再是独立后随字；发射即登记（HasExt 由 EcsFormat 表驱动）。
        void EmitExt(EcsOpcode op, int a, int b, int c, uint ext)
        {
            if (EcsFormat.ExtWords(op) != 1)
                throw Fail($"{op} 非数据字指令，不能携带数据字（与 EcsFormat 表不一致）");
            CheckIabcOperands(op, a, b, c);
            Emit(new EcsInstruction(op, a, b, c, ext));
        }

        /// <summary>Iabc 头部操作数字节域守卫（v3 定长）：槽位 u8 ≤254（255 = 无接收哨兵）；
        /// StickSet/StickP 的 B/C 是摇杆坐标（满 u8 0..255），不按槽位收紧。</summary>
        static void CheckIabcOperands(EcsOpcode op, int a, int b, int c)
        {
            if (a is < 0 or > 254) ThrowOp(op, $"A 槽位越界: {a}");
            bool coord = op is EcsOpcode.StickSet or EcsOpcode.StickP;
            if (b < 0 || b > (coord ? 255 : 255)) ThrowOp(op, $"B 越界: {b}");
            if (c < -1 || c > (coord ? 255 : 254)) ThrowOp(op, $"C 越界: {c}");
        }

        static void ThrowOp(EcsOpcode op, string what)
            => throw new BytecodeException(new[] { new BytecodeDiagnostic($"{op} {what}（v3 定长编码）", null, 0) });

        BytecodeException Fail(string message)
            => new(new[] { new BytecodeDiagnostic(message, _symbol.Name, _code.Count) });

        // ---- 标签与跳转修复 ----

        const int FixJmpBlock = 0;   // → 目标块
        const int FixJptBlock = 1;
        const int FixJpfBlock = 2;
        const int FixJmpLabel = 3;   // → 标签
        const int FixJptLabel = 4;
        const int FixJpfLabel = 5;
        const int FixForStepBlock = 6;   // ForStep：C = 回边目标（TrueSuccessor）偏移
        const int FixCmpJBlock = 7;      // CmpJ：跳转目标块
        const int FixCmpJLabel = 8;      // CmpJ：跳转目标标签（skip 形态）

        int NewLabel()
        {
            _labels.Add(-1);
            return _labels.Count - 1;
        }

        void MarkLabel(int label) => _labels[label] = _code.Count;

        void EmitJmpToBlock(SsaBlock target) => EmitJump(EcsOpcode.Jmp, 0, FixJmpBlock, target);

        /// <summary>ForStep：A=当前值槽、B=上限槽、C=目标槽（写入 tmp=i+1）、Jump=出口（end）偏移；
        /// tmp &gt; limit → 跳出口（$i 保持 ==upper）；否则落入后继（arm 副本 + 回边）。</summary>
        void EmitFixForStep(int srcSlot, int limitSlot, int dstSlot, SsaBlock exitBlock)
        {
            _fixups.Add(new Fixup(_code.Count, FixForStepBlock, exitBlock));
            Emit(new EcsInstruction(EcsOpcode.ForStep, srcSlot, limitSlot, dstSlot));
        }

        /// <summary>CmpJ（P2′）：A/B = 比较槽，C = kind，Jump = 成立时偏移（Patch 回填）。</summary>
        void EmitCmpJToBlock(SsaValue cmp, int kind, SsaBlock target)
        {
            _fixups.Add(new Fixup(_code.Count, FixCmpJBlock, target));
            Emit(new EcsInstruction(EcsOpcode.CmpJ, Slot(cmp.Arg0!), Slot(cmp.Arg1!), kind));
        }

        void EmitCmpJToLabel(SsaValue cmp, int kind, int label)
        {
            _fixups.Add(new Fixup(_code.Count, FixCmpJLabel, label));
            Emit(new EcsInstruction(EcsOpcode.CmpJ, Slot(cmp.Arg0!), Slot(cmp.Arg1!), kind));
        }

        void EmitJptToBlock(int cond, SsaBlock target) => EmitJump(EcsOpcode.Jpt, cond, FixJptBlock, target);
        void EmitJpfToBlock(int cond, SsaBlock target) => EmitJump(EcsOpcode.Jpf, cond, FixJpfBlock, target);
        void EmitJmpToLabel(int label) => EmitJump(EcsOpcode.Jmp, 0, FixJmpLabel, label);

        void EmitJump(EcsOpcode op, int cond, int kind, object target)
        {
            _fixups.Add(new Fixup(_code.Count, kind, target));
            // 偏移占位 0，Patch 统一回填（内存单位 = 相对指令下标；字节换算在 InstructionCodec.Project）
            Emit(new EcsInstruction(op, cond, 0, 0, 0, 0));
        }

        // ---- 主流程 ----

        public void Run()
        {
            AssignSlots();
            EmitBlocks();
            Patch();

            Result = new EcsFunction
            {
                Name = _symbol.Name,
                Module = _module,
                NParams = _symbol.Parameters.Length,
                NSlots = _poolNext,
                HasReturn = !_symbol.ReturnType.Equals(ScriptType.Void),
                Instructions = _code,
            };
            var lineTable = new List<int>(_linePcs.Count * 2);
            for (int i = 0; i < _linePcs.Count; i++)
            {
                lineTable.Add(_linePcs[i]);
                lineTable.Add(_lineLines[i]);
            }
            Result.LineTable = lineTable;
        }

        /// <summary>记录发射行号（稀疏表：仅行变化时追加 (指令下标, line)；0 行未知则继承上一登记）。</summary>
        void SetEmitLine(SsaValue? v)
        {
            var line = v?.Line ?? 0;
            if (line <= 0 || line == _currentEmitLine)
                return;
            _currentEmitLine = line;
            _linePcs.Add(_code.Count);
            _lineLines.Add(line);
        }

        // ---- 块发射 ----

        /// <summary>块发射序（= _fn.Blocks 表序）：尾跳转消除的「下一块」判定依据。</summary>
        readonly Dictionary<SsaBlock, int> _blockOrder = new();

        void EmitBlocks()
        {
            for (int i = 0; i < _fn.Blocks.Count; i++)
                _blockOrder[_fn.Blocks[i]] = i;
            foreach (var block in _fn.Blocks)
            {
                _blockStart[block] = _code.Count;
                var info = Info(block);

                // 循环不变量外提常量：preheader 块首物化一次（专属槽已在 AssignSlots 定槽；
                // 支配性保证运行期先于循环内任何读取）
                if (_hoistedConsts.TryGetValue(block, out var hoisted))
                {
                    if (Environment.GetEnvironmentVariable("ECX_HOIST_TRACE") == "1")
                        Console.Error.WriteLine($"[hoist-emit-run] b{block.Id} 发射 {hoisted.Count} 条 (fn={_fn.Symbol.Name})");
                    foreach (var c in hoisted)
                        EmitLoadConst(c);
                }

                // 块首惰性物化本块引用的常量（含 phi 臂读取）——每块独立池槽
                foreach (var c in info.Consts)
                {
                    _slots[c] = PoolAlloc();
                    EmitLoadConst(c);
                }

                foreach (var inst in block.Instructions)
                {
                    if (!IsDirectlyEmitted(inst.Op))
                        continue;   // 终结符/标记由 EmitTerminator 处理；常量占位由块首按需物化
                    if (_fusedCmp.Contains(inst))
                        continue;   // P2′：比较已融合进终结符 CmpJ，不落槽发射
                    // 回放登记期推导的结算计划：先结算操作数末次读取（槽位可被结果复用），再为块内结果取槽
                    if (_instReleases.TryGetValue(inst, out var releases))
                        foreach (var v in releases)
                            ReleaseDying(block, v);
                    if (_pooled.Contains(inst))
                        _slots[inst] = PoolAlloc();
                    EmitInst(inst);
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
            EmitTerminatorShape(block);
            // 回放登记期推导的终结符结算计划：返回值/分支条件/出边 φ 臂（每条出边一次，含死 φ 臂）
            if (_terminatorReleases.TryGetValue(block, out var termReleases))
                foreach (var v in termReleases)
                    ReleaseDying(block, v);
        }

        /// <summary>目标块是否为当前块的下一发射块（P1a 尾跳转消除依据）：
        /// 直落进下一块的发射输出 = 逐条执行它的块首副本/指令，语义即跳转本身。</summary>
        bool NextIsBlock(SsaBlock block, SsaBlock target)
            => _blockOrder.TryGetValue(target, out var o) && _blockOrder[block] + 1 == o;

        void EmitTerminatorShape(SsaBlock block)
        {
            // ForStep（FOR 快速路径尾块）：终结符即指令本身——A/B 取条件操作数槽，C 由 fixup 回填回边偏移；
            // 不等分支：i==upper 落入下一布局块（FalseSuccessor=end，创建序相邻）
            if (block.BranchCondition is { Op: SsaOp.ForStep } forStep)
            {
                // ForStep src, limit, dst, exit：tmp=src+1 写入 dst；tmp>limit → 跳 end（$i 保持 ==upper）；
                // 否则落入 body 的 arm 副本（携带新值）+ 回边 Jmp。
                // end 的 arm 副本（旧值 == upper）须在 ForStep 前发射：出口路径的最后一次拷贝即正确值
                var t = block.TrueSuccessor!;
                var f = block.FalseSuccessor!;
                if (f.Phis.Count > 0 && f.Predecessors.Contains(block))
                    EmitEdgeCopies(f, block);
                EmitFixForStep(Slot(forStep.Arg0!), Slot(forStep.Arg1!), Slot(forStep), f);
                if (t.Phis.Count > 0 && t.Predecessors.Contains(block))
                    EmitEdgeCopies(t, block);
                EmitJmpToBlock(t);
                return;
            }

            if (block.IsReturn)
            {
                SsaValue? retVal = null;
                foreach (var inst in block.Instructions)
                    if (inst.Op == SsaOp.Return) { retVal = inst.Arg0; break; }
                if (retVal != null)
                    EmitIabc(EcsOpcode.Ret, Slot(retVal), 0, 0);
                else
                    EmitIabc(EcsOpcode.Ret0, 0, 0, 0);
                return;
            }

            if (block.BranchCondition != null)
            {
                var condVal = block.BranchCondition;
                bool fused = _fusedCmp.Contains(condVal);
                int kind = fused ? CmpJKind(condVal.Op)!.Value : -1;
                int cond = fused ? -1 : Slot(condVal);   // 融合比较不落槽，Slot 查表必炸
                var t = block.TrueSuccessor!;
                var f = block.FalseSuccessor!;
                bool tPhi = t.Phis.Count > 0 && t.Predecessors.Contains(block);
                bool fPhi = f.Phis.Count > 0 && f.Predecessors.Contains(block);

                if (t == f)
                {
                    if (tPhi) EmitEdgeCopies(t, block);   // 防御：只发一份（A-03）
                    if (!NextIsBlock(block, t))           // 尾跳转消除：目标即下一发射块 → 直落
                        EmitJmpToBlock(t);
                }
                else if (!tPhi && !fPhi)
                {
                    // 单跳转形态（P1a）：某后继为下一发射块时，直落承担该臂，只发另一臂的条件跳转
                    if (NextIsBlock(block, t) && !NextIsBlock(block, f))
                    {
                        if (fused) EmitCmpJToBlock(condVal, InvertCmpJKind(kind), f);   // 不成立才跳 f
                        else EmitJpfToBlock(cond, f);
                    }
                    else if (NextIsBlock(block, f) && !NextIsBlock(block, t))
                    {
                        if (fused) EmitCmpJToBlock(condVal, kind, t);
                        else EmitJptToBlock(cond, t);
                    }
                    else
                    {
                        if (fused) EmitCmpJToBlock(condVal, kind, t);
                        else EmitJptToBlock(cond, t);
                        if (fused) EmitCmpJToBlock(condVal, InvertCmpJKind(kind), f);
                        else EmitJpfToBlock(cond, f);
                    }
                }
                else if (tPhi && !fPhi)
                {
                    int skip = NewLabel();
                    if (fused) EmitCmpJToLabel(condVal, InvertCmpJKind(kind), skip);
                    else EmitFixJpf(cond, skip);
                    EmitEdgeCopies(t, block);
                    EmitJmpToBlock(t);   // mid 跳转不可消除：直落会掉进 skip 后的 f 臂
                    MarkLabel(skip);
                    if (!NextIsBlock(block, f))
                        EmitJmpToBlock(f);
                }
                else if (!tPhi && fPhi)
                {
                    int skip = NewLabel();
                    if (fused) EmitCmpJToLabel(condVal, kind, skip);
                    else EmitFixJpt(cond, skip);
                    EmitEdgeCopies(f, block);
                    EmitJmpToBlock(f);   // mid 跳转不可消除：同上
                    MarkLabel(skip);
                    if (!NextIsBlock(block, t))
                        EmitJmpToBlock(t);
                }
                else
                {
                    int skip = NewLabel();
                    if (fused) EmitCmpJToLabel(condVal, InvertCmpJKind(kind), skip);
                    else EmitFixJpf(cond, skip);
                    EmitEdgeCopies(t, block);
                    if (!NextIsBlock(block, t))
                        EmitJmpToBlock(t);
                    MarkLabel(skip);
                    EmitEdgeCopies(f, block);
                    if (!NextIsBlock(block, f))
                        EmitJmpToBlock(f);
                }
                return;
            }

            if (block.JumpTarget != null)
            {
                var j = block.JumpTarget;
                if (j.Phis.Count > 0 && j.Predecessors.Contains(block))
                    EmitEdgeCopies(j, block);
                if (!NextIsBlock(block, j))   // 尾跳转消除
                    EmitJmpToBlock(j);
                return;
            }

            throw Fail("块无终止指令（SSA 不变量破坏，A-01）");
        }

        // 条件跳转到标签：Kind 必须用 Label 变体（Patch 按 Kind 区分块/标签转型）
        void EmitFixJpf(int cond, int label) => EmitJump(EcsOpcode.Jpf, cond, FixJpfLabel, label);
        void EmitFixJpt(int cond, int label) => EmitJump(EcsOpcode.Jpt, cond, FixJptLabel, label);

        /// <summary>出边 φ 副本发射（Sessa 并行拷贝见 EmitParallelCopy）。
        /// 臂读取的结算不在本方法——统一由终结符结算计划回放（含死 φ 臂），
        /// 副本选择与记账分离，避免「发射一处推导、结算多处推导」漂移。</summary>
        void EmitEdgeCopies(SsaBlock successor, SsaBlock from)
        {
            int armIdx = successor.Predecessors.IndexOf(from);
            if (armIdx < 0)
                throw Fail("phi 前驱缺失（SSA 不变量破坏，A-01）");

            var moves = new List<(int Dst, int Src)>();
            foreach (var phi in successor.Phis)
            {
                if (!_slots.ContainsKey(phi))
                    continue;   // 死 φ（防线 1）：不占槽、不发副本，臂读取由计划照常结算
                moves.Add((Slot(phi), Slot(phi.ExtraArgs![armIdx])));
            }
            EmitParallelCopy(moves);
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
                    FixJmpBlock or FixJptBlock or FixJpfBlock or FixForStepBlock or FixCmpJBlock => _blockStart[(SsaBlock)fixup.Target],
                    _ => _labels[(int)fixup.Target],
                };
                int offset = target - (fixup.InsIdx + 1);
                var word = _code[fixup.InsIdx];

                _code[fixup.InsIdx] = fixup.Kind == FixForStepBlock
                    ? word with { Jump = offset }
                    : word with { Jump = offset };
            }
        }

        sealed class Fixup
        {
            public readonly int InsIdx;
            public readonly int Kind;
            public readonly object Target;

            public Fixup(int insIdx, int kind, object target)
            {
                InsIdx = insIdx; Kind = kind; Target = target;
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