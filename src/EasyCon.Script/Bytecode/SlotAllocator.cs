using EasyCon.Script.Runtime;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EasyCon.Script.Bytecode;

public static partial class BytecodeEncoder
{
    sealed partial class Encoder
    {
        /// <summary>块内使用信息：常量按使用块块首物化；UseCount 含终结符/phi 臂读取。</summary>
        sealed class BlockUseInfo
        {
            public readonly List<SsaValue> Consts = new();
            public readonly Dictionary<SsaValue, int> UseCount = new();
        }

        BlockUseInfo Info(SsaBlock b)
        {
            if (!_useInfo.TryGetValue(b, out var info))
                _useInfo[b] = info = new BlockUseInfo();
            return info;
        }

        static bool NeedsSlot(SsaOp op) => op switch
        {
            SsaOp.StoreLocal or SsaOp.StoreGlobal or SsaOp.StoreField or SsaOp.StoreIndex
                or SsaOp.StoreFieldIndex or SsaOp.Wait or SsaOp.KeyPress or SsaOp.KeyAction
                or SsaOp.StickAction or SsaOp.StickPress or SsaOp.Return
                or SsaOp.CondBranch or SsaOp.Branch or SsaOp.Nop => false,
            _ => true,
        };

        /// <summary>P1b phi 臂二地址合并：跨块指令 X（唯一读取是某条 phi 臂）→ 与该 phi 共槽，
        /// 边副本 dst==src 自消（EmitParallelCopy 跳过）。</summary>
        readonly Dictionary<SsaValue, SsaValue> _coalesced = new();
        /// <summary>P2′ CmpJ 融合：已标记为「比较跳转融合」的 BranchCondition（不落槽、EmitInst 跳过）。</summary>
        readonly HashSet<SsaValue> _fusedCmp = new();

        /// <summary>CmpJ kind 码 = typeBlock*6 + op；typeBlock：0=i32，1=u32，2=f64，3=i64；
        /// op：0=Eq，1=Neq，2=Lt，3=Le，4=Gt，5=Ge。非比较族返回 null。</summary>
        internal static int? CmpJKind(SsaOp op) => op switch
        {
            SsaOp.EqInt => 0,
            SsaOp.NeqInt => 1,
            SsaOp.LtInt => 2,
            SsaOp.LeqInt => 3,
            SsaOp.GtInt => 4,
            SsaOp.GeqInt => 5,
            SsaOp.EqUInt => 6,
            SsaOp.NeqUInt => 7,
            SsaOp.LtUInt => 8,
            SsaOp.LeqUInt => 9,
            SsaOp.GtUInt => 10,
            SsaOp.GeqUInt => 11,
            SsaOp.EqDouble => 12,
            SsaOp.NeqDouble => 13,
            SsaOp.LtDouble => 14,
            SsaOp.LeqDouble => 15,
            SsaOp.GtDouble => 16,
            SsaOp.GeqDouble => 17,
            SsaOp.EqUInt64 => 18,
            SsaOp.NeqUInt64 => 19,
            SsaOp.LtUInt64 => 20,
            SsaOp.LeqUInt64 => 21,
            SsaOp.GtUInt64 => 22,
            SsaOp.GeqUInt64 => 23,
            SsaOp.EqBool => 0,
            SsaOp.NeqBool => 1,                       // Bool 槽载荷 = I32 0/1，按 i32 比较
            SsaOp.EqByte => 0,
            SsaOp.NeqByte => 1,
            SsaOp.LtByte => 2,
            SsaOp.LeqByte => 3,
            SsaOp.GtByte => 4,
            SsaOp.GeqByte => 5,
            SsaOp.EqPtr => 18,
            SsaOp.NeqPtr => 19,
            SsaOp.LtPtr => 20,
            SsaOp.LeqPtr => 21,
            SsaOp.GtPtr => 22,
            SsaOp.GeqPtr => 23,                       // Ptr = I64 载荷
            _ => null,
        };

        /// <summary>CmpJ kind 极性反转（Eq↔Neq，Lt↔Ge，Le↔Gt；IEEE 下 != ≡ !(==)，f64 亦精确）。</summary>
        internal static int InvertCmpJKind(int k) => (k / 6) * 6 + (k % 6) switch { 0 => 1, 1 => 0, 2 => 5, 3 => 4, 4 => 3, _ => 2 };

        /// <summary>
        /// P1b phi 臂二地址合并：跨块指令 X 的全函数唯一读取若是某条 phi 臂（X 定义在前驱 P、
        /// 臂属 P 的终结符副本），让 X 与该 φ 共槽——P 体内 X 直接把结果写入 φ 槽，
        /// 边副本 dst==src 被 EmitParallelCopy 消除，每条循环携带值省 1 条 Move。
        /// 守卫（违一即放弃合并，保持原副本形态）：
        ///   (a) X 非常量、非 ForStep（终结符独占槽）、未参与其他合并；
        ///   (b) P 内 X 之后无指令读 φ（Arg0/Arg1/ExtraArgs）——X 覆写后旧值不得再被读；
        ///   (c) P 的 BranchCondition 与 P 各后继 phi 的其他臂不引用 φ（终结符并行副本
        ///       按 Slot 读旧值，覆写后会拿到新值）；
        ///   (d) φ 为活 φ（已定槽）。
        /// </summary>
        void TryCoalescePhiArm(SsaValue phi, SsaBlock succ)
        {
            if (phi.ExtraArgs == null)
                return;
            bool zeroPhi = Environment.GetEnvironmentVariable("ECS_NO_ZERO_PHI") != "1";
            if (!zeroPhi)
            {
                // 关闭零 φ 扩展（完整旁路）：恢复原「单臂 + 常量/ForStep 排除」保守形态（回归二分用）
                for (int i = 0; i < phi.ExtraArgs.Count && i < succ.Predecessors.Count; i++)
                {
                    var arm0 = phi.ExtraArgs[i];
                    if (arm0 == null || arm0.IsConstant || arm0.Op == SsaOp.ForStep)
                        return;
                }
            }
            for (int i = 0; i < phi.ExtraArgs.Count && i < succ.Predecessors.Count; i++)
            {
                var arm = phi.ExtraArgs[i];
                var pred = succ.Predecessors[i];
                if (arm == null || arm == phi || arm.IsConstant)
                    continue;
                // (a') ForStep 臂允许合并：ForStep 的自增目标槽改为直写 φ 槽（C=φ 槽），
                //     回边副本 dst==src 被 EmitParallelCopy 自消——Lua FORLOOP 式就地更新。
                //     守卫：forStep 的全函数数据读取恰为该臂 1 次（终结符形态不算数据读取）。
                if (arm.Op == SsaOp.ForStep)
                {
                    if (_coalesced.ContainsKey(arm) || _slots.ContainsKey(arm))
                        continue;
                    if (_totalReads.TryGetValue(arm, out var ft) && ft != 1)
                        continue;
                    if (pred.BranchCondition == arm)
                    {
                        _coalesced[arm] = phi;
                        _slots[arm] = _slots[phi];
                        ScrubPooled(arm);
                    }
                    continue;
                }
                if (_coalesced.ContainsKey(arm) || _slots.ContainsKey(arm))
                    continue;
                if (arm.Block != pred)
                    continue;
                if (_totalReads.TryGetValue(arm, out var t) && t != 1)
                    continue;
                if (pred.BranchCondition == phi)
                    continue;   // (c) 终结符条件读 φ 槽

                // (b) P 内 X 之后读 φ → 放弃
                var instrs = pred.Instructions;
                int xIdx = instrs.IndexOf(arm);
                bool unsafeRead = false;
                for (int k = xIdx + 1; k < instrs.Count && !unsafeRead; k++)
                {
                    var inst = instrs[k];
                    if (inst.Arg0 == phi || inst.Arg1 == phi)
                        unsafeRead = true;
                    else if (inst.ExtraArgs != null && inst.ExtraArgs.Contains(phi))
                        unsafeRead = true;
                }
                if (unsafeRead)
                    continue;

                // (c) P 的各后继 phi 的其他臂引用 φ → 放弃
                foreach (var s2 in pred.GetSuccessors())
                {
                    foreach (var phi2 in s2.Phis)
                    {
                        if (phi2 == phi || phi2.ExtraArgs == null)
                            continue;
                        foreach (var a2 in phi2.ExtraArgs)
                        {
                            if (a2 == phi)
                            {
                                unsafeRead = true;
                                break;
                            }
                        }
                        if (unsafeRead) break;
                    }
                    if (unsafeRead) break;
                }
                if (unsafeRead)
                    continue;

                _coalesced[arm] = phi;
                _slots[arm] = _slots[phi];
                ScrubPooled(arm);   // 臂若已按池化分类（其块先于 φ 块处理），共享 φ 独占槽后须退出池化结算
                // 零 φ 拷贝：遍历全部臂（各臂守卫独立成立；同 φ 多臂在不同前驱边执行，
                // 分时写同一槽即 φ 语义本身）。常量臂不合并——块首物化在 φ 槽上会覆写
                // 同迭代仍活跃的旧值。
            }
        }

        /// <summary>把已池化分类的值退出池化结算（合并共享 φ 槽后，其“末次读取归还”
        /// 会把 φ 的独占槽放进空闲池——寄存器筛语料实证的槽位污染源）。</summary>
        void ScrubPooled(SsaValue v)
        {
            _pooled.Remove(v);
            _releaseLeft.Remove(v);
        }

        /// <summary>
        /// 循环不变量常量外提（docs/VM2.md §4.3 块首惰性物化的循环修正）：
        /// 常量按「使用块块首」物化，落在循环头/体时每轮重发——FOR 尾融合后回边极短，
        /// 2-3 条块首 LoadI 占每轮指令数的近半。本遍把「全部使用块都被某循环 preheader
        /// 支配」的常量改挂到 preheader 块首物化一次，循环内读取零重物化。
        ///
        /// 判定 = 回边（t→h 且 h 支配 t）→ 自然循环体（从 t 逆行不经 h）→ 唯一非循环
        /// 前驱即 preheader；支配性由 Dominators 树核对（preheader 支配使用块 ⟹ 运行期
        /// 物化必先于读取，与发射顺序无关）。
        ///
        /// 槽位 = 可共享组：外提常量若各占专属槽会膨胀帧区（MCU 固定池敏感）。两常量
        /// 共享一槽的充要守卫：对每个有序对 (x,y)，从 P_y 出发不经 P_x 块首不可达 c_x 的
        /// 任何使用块（BFS 判定）——否则 y 的物化覆写后 x 的读取拿到陈值。同挂点不共享。
        /// 已被外层循环外提的常量跳过（_hoisted 去重；外层 preheader 是更优挂点）。
        /// </summary>
        void HoistLoopInvariantConsts()
        {
            if (_useInfo.Count == 0 || Environment.GetEnvironmentVariable("ECS_DISABLE_CONST_HOIST") == "1")
                return;
            var dom = Dominators.Compute(_fn);

            // 1. 收集外提对（const, preheader），快照各自使用块集合（供共享判定）
            var pairs = new List<(SsaValue Const, SsaBlock Preheader)>();
            var usesOf = new Dictionary<SsaValue, HashSet<SsaBlock>>();
            foreach (var block in _fn.Blocks)
            {
                foreach (var header in block.GetSuccessors())
                {
                    if (!dom.Dominates(header, block))
                        continue;   // 非回边

                    // 自然循环体：{header} ∪ 从 block 逆行不经 header 可达的块
                    var body = new HashSet<SsaBlock> { header };
                    var stack = new Stack<SsaBlock>();
                    if (body.Add(block))
                        stack.Push(block);
                    while (stack.Count > 0)
                        foreach (var p in stack.Pop().Predecessors)
                            if (body.Add(p))
                                stack.Push(p);

                    // preheader = header 的唯一非循环前驱（不唯一则放外提：挂点不唯一即不安全）
                    var outside = header.Predecessors.Where(p => !body.Contains(p)).ToList();
                    if (outside.Count != 1)
                        continue;
                    var preheader = outside[0];

                    // 候选 = 循环体块登记的常量；只外提「脊柱」使用（header 支配使用块且
                    // 使用块支配回边源 ⟹ 每轮必经）——稀有分支内的常量外提无每轮收益，
                    // 徒占共享槽；全部使用块都被 preheader 支配仍是大前提
                    var tail = block;
                    foreach (var b in body)
                    {
                        var info = Info(b);
                        for (int i = info.Consts.Count - 1; i >= 0; i--)
                        {
                            var c = info.Consts[i];
                            if (_hoisted.Contains(c))
                                continue;   // 已挂更外层 preheader（更优挂点）
                            var useBlocks = new HashSet<SsaBlock>();
                            bool allDominated = true, allSpine = true;
                            foreach (var kv in _useInfo)
                            {
                                if (!kv.Value.Consts.Contains(c))
                                    continue;
                                useBlocks.Add(kv.Key);
                                if (!dom.Dominates(preheader, kv.Key))
                                {
                                    allDominated = false;
                                    break;
                                }
                                if (body.Contains(kv.Key)
                                    && !(dom.Dominates(header, kv.Key) && dom.Dominates(kv.Key, tail)))
                                    allSpine = false;
                            }
                            if (!allDominated || !allSpine)
                                continue;
                            foreach (var kv in _useInfo)
                                kv.Value.Consts.Remove(c);
                            pairs.Add((c, preheader));
                            usesOf[c] = useBlocks;
                            _hoisted.Add(c);
                        }
                    }
                }
            }
            if (pairs.Count == 0)
                return;
            if (Environment.GetEnvironmentVariable("ECX_HOIST_TRACE") == "1")
                foreach (var (c, p) in pairs)
                    Console.Error.WriteLine($"[hoist] v{c.Id} -> preheader b{p.Id} (fn={_fn.Symbol.Name})");

            // 2. 可共享分组定槽（贪心：与既有组两两做双向可达性守卫）
            var groupConsts = new List<List<SsaValue>>();
            var groupPres = new List<List<SsaBlock>>();
            var groupSlots = new List<int>();
            foreach (var (c, p) in pairs)
            {
                int g = 0;
                for (; g < groupConsts.Count; g++)
                {
                    bool safe = true;
                    for (int i = 0; i < groupConsts[g].Count && safe; i++)
                        safe = SafeToShare(groupPres[g][i], groupConsts[g][i], p, usesOf)
                            && SafeToShare(p, c, groupPres[g][i], usesOf);
                    if (safe)
                        break;
                }
                if (g == groupConsts.Count)
                {
                    groupConsts.Add(new List<SsaValue>());
                    groupPres.Add(new List<SsaBlock>());
                    groupSlots.Add(_next++);   // 新组一个共享专属槽
                }
                groupConsts[g].Add(c);
                groupPres[g].Add(p);
                _slots[c] = groupSlots[g];   // 加入既有组：取组槽（非最新组槽）
                // 挂点登记：EmitBlocks 在 preheader 块首物化一次
                if (!_hoistedConsts.TryGetValue(p, out var list))
                    _hoistedConsts[p] = list = new List<SsaValue>();
                list.Add(c);
            }
        }

        /// <summary>共享守卫：从 py 块首物化后（含 py 自身指令），不经 px 块首可达 cx 的
        /// 使用块 ⟹ py 的物化覆写共享槽后 cx 读到陈值 → 不可共享。</summary>
        static bool SafeToShare(SsaBlock px, SsaValue cx, SsaBlock py,
            Dictionary<SsaValue, HashSet<SsaBlock>> usesOf)
        {
            if (px == py || usesOf[cx].Contains(py))
                return false;   // 同挂点顺序覆写 / py 自身读取 cx 在其物化之后
            var visited = new HashSet<SsaBlock>();
            var stack = new Stack<SsaBlock>();
            foreach (var s in py.GetSuccessors())
                if (s != px && visited.Add(s))
                    stack.Push(s);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (usesOf[cx].Contains(cur))
                    return false;
                foreach (var s in cur.GetSuccessors())
                    if (s != px && visited.Add(s))
                        stack.Push(s);
            }
            return true;
        }

        /// <summary>
        /// 槽位分配（docs/VM2.md §4.3）：存活局部压缩重编号 + 跨块/块内分类 + 块内槽池 + 常量块首惰性物化。
        /// 优化删除的局部不占帧槽（步骤 0 重编号）；phi 结果恒跨块（写入发生在前驱边的并行副本）；
        /// 其余值当且仅当全部读取都在定义块内池化；
        /// 常量是纯值，一律池化并按使用块块首物化（零使用的死常量不物化）。
        /// 活性依据：块内直线 defs/uses 全序，「定义分配 + 末次读取归还」即精确活性；
        /// phi 臂读取按臂↔前驱对齐计入对应前驱块（副本在前驱终结符发射）。
        ///
        /// 读取登记（UseCount）与结算计划（_instReleases/_terminatorReleases）在同一次
        /// 遍历中推导——结算点由登记点唯一确定，发射侧只回放计划，不存在第二条
        /// 手写遍历（对齐 Go regalloc computeLive / LLVM LiveIntervals 的「分析一次、
        /// 消费多处」与 regalloc2 的「访问方式在指令上声明一次」原则；Lua lcode.c
        /// 的 freeexp 同趟释放则是单遍化的极限形态，此处因发射非单遍而取计划回放）。
        /// </summary>
        void AssignSlots()
        {
            // 0. 存活局部重编号（编码期槽位回收）：局部读值全走 SSA（构建期 mem2reg），
            // StoreLocal 仅参数 store 发射（EmitInst；TRE 回边写参数帧槽，entry 循环头重读）。
            // 帧槽读取者 = 参数入口 LoadLocal（Call ABI 播种）；参数窗映射到 ARG 寄存器 R1–R7
            // （Call 深拷贝播种，见 EcsRegisters）；防御性非参数局部（若未来出现）从通用区起。
            var liveLocalSlots = new SortedSet<int>();
            foreach (var block in _fn.Blocks)
                foreach (var inst in block.Instructions)
                {
                    if (inst.Op == SsaOp.LoadLocal && inst.Aux is LocalVariableSymbol lv)
                        liveLocalSlots.Add(lv.Slot.Index);
                    else if (inst.Op == SsaOp.StoreLocal && inst.Aux is ParamSymbol ps)
                        liveLocalSlots.Add(ps.Slot.Index);
                }
            int nextLocal = EcsRegisters.AllocBase;
            foreach (var oldIdx in liveLocalSlots)
            {
                if (oldIdx < _symbol.Parameters.Length)
                    _localSlotRemap[oldIdx] = EcsRegisters.ArgBase + oldIdx;
                else
                    _localSlotRemap[oldIdx] = nextLocal++;
            }
            _next = nextLocal;

            // 1. 使用点收集 + 结算计划推导（同源）：操作数/分支条件记在使用块；phi 臂记在对应前驱
            foreach (var block in _fn.Blocks)
            {
                foreach (var phi in block.Phis)
                {
                    if (phi.ExtraArgs == null) continue;
                    for (int i = 0; i < phi.ExtraArgs.Count; i++)
                    {
                        // 臂 i 在前驱 Predecessors[i] 的终结符被并行副本读取（A-01 对齐不变量）
                        var reader = i < block.Predecessors.Count ? block.Predecessors[i] : block;
                        RecordRead(phi.ExtraArgs[i], reader);
                    }
                }

                SsaValue? returnValue = null;
                foreach (var inst in block.Instructions)
                {
                    if (inst.Op == SsaOp.Return && returnValue == null)
                        returnValue = inst.Arg0;   // 与 EmitTerminator 一致取首条 Return

                    // 常量模板降级（ArrayTemplate）：元素是编译期数据（构建段直连常量池/模板全局），
                    // 不读槽——不登记使用、不物化块首常量、不进结算计划
                    if (IsTemplateArrayInit(inst))
                        continue;

                    // 非参数 StoreLocal 不发射（EmitInst）：操作数不读槽——不登记使用、不物化块首常量、不进结算计划；
                    // 参数 store 照常发射，操作数登记/结算与普通指令一致
                    if (inst.Op == SsaOp.StoreLocal && inst.Aux is not ParamSymbol)
                        continue;

                    // 登记与计划同点判定：立即数消费不读槽 → 既不登记也不进计划
                    List<SsaValue>? releases = null;
                    if (inst.Arg0 != null && !IsImmediateOperand(inst, inst.Arg0))
                    {
                        RecordRead(inst.Arg0, block);
                        releases = [inst.Arg0];
                    }
                    if (inst.Arg1 != null)
                    {
                        RecordRead(inst.Arg1, block);
                        (releases ??= []).Add(inst.Arg1);
                    }
                    if (inst.ExtraArgs != null)
                        foreach (var a in inst.ExtraArgs)
                        {
                            RecordRead(a, block);
                            (releases ??= []).Add(a);
                        }

                    // 只有直接发射的指令在发射点回放结算；Return 的操作数归终结符计划
                    if (releases != null && IsDirectlyEmitted(inst.Op))
                        _instReleases[inst] = [.. releases];
                }

                // 终结符结算计划：返回值 + 分支条件 + 出边 φ 臂（t==f 只算一条出边，与发射一致）
                var terminatorReleases = new List<SsaValue>();
                if (block.IsReturn && returnValue != null)
                    terminatorReleases.Add(returnValue);
                if (block.BranchCondition != null)
                {
                    RecordRead(block.BranchCondition, block);
                    terminatorReleases.Add(block.BranchCondition);
                    // ForStep 终结符：src/limit 的读取随指令执行发生（EmitTerminatorShape），
                    // 归入终结符计划结算——否则池化 limit 常量的块内计数永不归零（槽泄漏）
                    if (block.BranchCondition.Op == SsaOp.ForStep)
                    {
                        if (block.BranchCondition.Arg0 != null)
                            terminatorReleases.Add(block.BranchCondition.Arg0);
                        if (block.BranchCondition.Arg1 != null)
                            terminatorReleases.Add(block.BranchCondition.Arg1);
                    }
                }
                foreach (var successor in SuccessorEdges(block))
                {
                    if (successor.Phis.Count == 0 || !successor.Predecessors.Contains(block))
                        continue;
                    var armIdx = successor.Predecessors.IndexOf(block);
                    foreach (var phi in successor.Phis)
                    {
                        if (phi.ExtraArgs == null || armIdx >= phi.ExtraArgs.Count)
                            continue;
                        terminatorReleases.Add(phi.ExtraArgs[armIdx]);   // 死 φ 的臂同样登记过，结算一次
                    }
                }
                if (terminatorReleases.Count > 0)
                    _terminatorReleases[block] = [.. terminatorReleases];
            }

            // 1.4 P2′ CmpJ 融合标记：BranchCondition 为比较族、定义在本块、唯一读取是终结符
            //     → 比较不落槽（EmitInst 跳过、不占池），由 EmitTerminatorShape 发单条 CmpJ；
            //     其操作数读取结算从指令计划移交终结符计划（CmpJ 执行时才读槽）
            foreach (var block in _fn.Blocks)
            {
                if (block.BranchCondition is not { } bc || _fusedCmp.Contains(bc))
                    continue;
                if (bc.Block != block || bc.IsConstant || bc.Arg0 == null || bc.Arg1 == null)
                    continue;
                if (CmpJKind(bc.Op) is null)
                    continue;
                if (!_totalReads.TryGetValue(bc, out var tr) || tr != 1)
                    continue;
                _fusedCmp.Add(bc);
                if (_instReleases.Remove(bc, out var rel))
                {
                    var term = _terminatorReleases.TryGetValue(block, out var t) ? t.ToList() : [];
                    term.AddRange(rel);
                    _terminatorReleases[block] = [.. term];
                }
            }

            // 1.5 循环不变量常量外提：消费步骤 1 的使用登记（_useInfo），改挂外提常量到
            //     preheader 块首（从各使用块 Consts 移除）——须在分类/发射前完成
            HoistLoopInvariantConsts();

            // 1.6 循环携带活性：值「定义块不在某自然循环内、却有读取落在该循环体内」时，
            //     末次静态读取结算不安全——循环下一轮会重读该值，而槽位可能已让给别人
            //     （线性扫描对回边活性的盲区，参考 regalloc 的 loop-nest 感知）。此类值
            //     退回独占槽（def ∈ L ⟹ 定义随循环体重执行，读取前的本轮定义先写回，池化仍安全）。
            var loopExclusive = CollectLoopCarriedValues();

            // 2. 分类：φ 结果/ForStep 目标独占槽（跨迭代被边副本/分支原地写，池化会在循环
            //    存续期间把槽位让给他人）；其余 needs-slot 值（块内与跨块统一）全部池化——
            //    生命周期由读取点结算驱动（参考线性扫描寄存器分配：值在定义发射时取槽、
            //    末次静态读取结算后归还；循环内的后续执行不会再遇到已发射完毕的读取点）。
            //    常量仍按使用块块首物化（每块独立槽 + 块内计数归还）。
            bool hasPhi = false, hasNeq = false, hasTpl = false;
            foreach (var block in _fn.Blocks)
            {
                var info = Info(block);
                foreach (var phi in block.Phis)
                {
                    if (!_totalReads.ContainsKey(phi))
                        continue;   // 死 φ（防线 1）：全函数无读取——不占槽，前驱边不产生副本（EmitEdgeCopies 跳过）
                    _slots[phi] = _next++;
                    _exclusiveSlots.Add(_slots[phi]);
                    hasPhi = true;
                    TryCoalescePhiArm(phi, block);
                }
                foreach (var inst in block.Instructions)
                {
                    // P2′ 融合比较：无结果槽（CmpJ 内联消费），跳过分类
                    if (_fusedCmp.Contains(inst))
                        continue;
                    // P1b 合并指令：槽已随 phi 定槽（共享），跳过独立分类
                    if (_coalesced.ContainsKey(inst))
                        continue;
                    // ForStep：分支内嵌的自增目标槽必须独占（每迭代原地写），恒跨块专属槽；
                    // 零 φ 拷贝合并例外：目标槽 = φ 槽（C 直写 φ 槽，回边副本自消）
                    if (inst.Op == SsaOp.ForStep)
                    {
                        _slots[inst] = _coalesced.TryGetValue(inst, out var phiSlot) ? _slots[phiSlot] : _next++;
                        _exclusiveSlots.Add(_slots[inst]);
                        continue;
                    }
                    if (inst.Op is SsaOp.NeqInt or SsaOp.NeqUInt or SsaOp.NeqDouble or SsaOp.NeqUInt64
                        or SsaOp.NeqBool or SsaOp.NeqByte or SsaOp.NeqString or SsaOp.NeqPtr)
                        hasNeq = true;
                    // 常量模板降级：元素不占槽也不进 staging（结果槽照常池化）
                    if (IsTemplateArrayInit(inst))
                        hasTpl = true;

                    if (inst.IsConstant)
                    {
                        if (!_hoisted.Contains(inst))
                            _pooled.Add(inst);        // 常量池化：Consts 已在步骤 1 登记（块首物化）
                        // 外提常量：共享组槽位已在 HoistLoopInvariantConsts 定槽，跳过
                        continue;
                    }
                    if (!NeedsSlot(inst.Op)) continue;

                    if (loopExclusive.Contains(inst))
                    {
                        _slots[inst] = _next++;   // 循环携带活性（见 1.6）：独占
                        _exclusiveSlots.Add(_slots[inst]);
                        continue;
                    }

                    // 全池化：不再区分块内/跨块——跨块槽的独占占用是 $eval 顶层脚本
                    // 寄存器高水位的根因；末次读取结算（ReleaseDying 全函数计数）回收
                    _pooled.Add(inst);
                    _releaseLeft[inst] = _totalReads.TryGetValue(inst, out var t) ? t : 0;
                }
            }

            if (hasNeq) { _neqTemp = _next++; _exclusiveSlots.Add(_neqTemp); }
            if (hasPhi) { _scratch = _next++; _exclusiveSlots.Add(_scratch); }
            if (hasTpl)
            {
                _tplGuard = _next++; _exclusiveSlots.Add(_tplGuard);
                _tplArr = _next++; _exclusiveSlots.Add(_tplArr);
                _tplChunk = _next++; _exclusiveSlots.Add(_tplChunk);
            }

            // STAGING 暂存区固定于 R8–R23（EcsRegisters，帧内全局固定位置）：调用出站实参
            // 与数组分块构建共用；与参数窗/分配区物理隔离——出站 Move 不可能覆写存活值，
            // 无重叠 Move 冒险；链接期内联按固定位置判定重映射。
            _stagingBase = EcsRegisters.StagingBase;
            _tplChunkSize = Math.Min(TemplateChunkSize, EcsRegisters.StagingCount);

            // 池区自独占区之后生长（_next 已含全部独占槽；≥ AllocBase）；帧长（NSlots）=
            // 发射结束后池区高水位（≤ MaxRegisters，Run 期校验）
            _poolNext = Math.Max(_next, EcsRegisters.AllocBase);
        }

        /// <summary>
        /// 该操作数是否被指令以立即数形式消费（KeyI/WaitI/StickP 的常量时长）。
        /// 立即数消费不读槽：不计使用（不触发块首物化，也无需归还）。
        /// </summary>
        static bool IsImmediateOperand(SsaValue inst, SsaValue operand)
        {
            if (!operand.IsConstant || operand != inst.Arg0)
                return false;
            return inst.Op switch
            {
                SsaOp.KeyPress or SsaOp.Wait
                    => operand.Const.GetInt() is >= 0 and <= 0xFFFF,
                SsaOp.StickPress => true,
                _ => false,
            };
        }

        /// <summary>
        /// 指令是否按普通指令直接发射（否则：终结符/标记由 EmitTerminator 处理，
        /// 常量占位由块首按需物化）。发射遍历与结算计划共用此谓词——指令筛选
        /// 只有一处定义，登记/计划与发射两条遍历不可能在此漂移。
        /// </summary>
        static bool IsDirectlyEmitted(SsaOp op) => op switch
        {
            SsaOp.Phi or SsaOp.CondBranch or SsaOp.Branch or SsaOp.Return or SsaOp.Nop or SsaOp.ForStep => false,
            SsaOp.ConstBool or SsaOp.ConstByte or SsaOp.ConstInt or SsaOp.ConstUInt
                or SsaOp.ConstUInt64 or SsaOp.ConstDouble or SsaOp.ConstString or SsaOp.ConstPtr => false,
            _ => true,
        };

        /// <summary>块的所有出边（t==f 去重为一条——发射侧对同一后继只发一份副本、结算一次，A-03）。</summary>
        static IEnumerable<SsaBlock> SuccessorEdges(SsaBlock block)
        {
            if (block.BranchCondition != null)
            {
                if (block.TrueSuccessor != null)
                    yield return block.TrueSuccessor;
                if (block.FalseSuccessor != null && !ReferenceEquals(block.FalseSuccessor, block.TrueSuccessor))
                    yield return block.FalseSuccessor;
            }
            else if (block.JumpTarget != null)
            {
                yield return block.JumpTarget;
            }
        }

        /// <summary>循环携带活性判定（步骤 1.6）：收集「定义块不在其读取所在自然循环体内」的
        /// 非常量值。这类值的读取会在循环的后续迭代中再次执行，而池化结算已在末次静态读取
        /// 处把槽位让给别人——线性扫描对回边活性的盲区（参考 regalloc 的 loop-nest 感知）。
        /// 定义块 ∈ 循环体 ⟹ 定义随每轮迭代重执行、先于本轮读取写回，池化仍安全。</summary>
        HashSet<SsaValue> CollectLoopCarriedValues()
        {
            var result = new HashSet<SsaValue>();
            if (_fn.Blocks.Count == 0)
                return result;

            var dom = Dominators.Compute(_fn);

            // 值 → 读取块集合（来自步骤 1 的使用登记；φ 臂读取登记在前驱块）
            var readBlocks = new Dictionary<SsaValue, List<SsaBlock>>();
            foreach (var kv in _useInfo)
                foreach (var v in kv.Value.UseCount.Keys)
                {
                    if (v.IsConstant)
                        continue;
                    if (!readBlocks.TryGetValue(v, out var list))
                        readBlocks[v] = list = new List<SsaBlock>();
                    list.Add(kv.Key);
                }

            foreach (var tail in _fn.Blocks)
            {
                foreach (var header in tail.GetSuccessors())
                {
                    if (!dom.Dominates(header, tail))
                        continue;   // 非回边

                    // 自然循环体：{header} ∪ 从 tail 逆行不经 header 可达的块
                    var body = new HashSet<SsaBlock> { header };
                    var stack = new Stack<SsaBlock>();
                    if (body.Add(tail))
                        stack.Push(tail);
                    while (stack.Count > 0)
                        foreach (var p in stack.Pop().Predecessors)
                            if (body.Add(p))
                                stack.Push(p);

                    foreach (var (v, blocks) in readBlocks)
                    {
                        if (result.Contains(v) || v.Block == null || body.Contains(v.Block))
                            continue;
                        if (blocks.Any(body.Contains))
                            result.Add(v);
                    }
                }
            }
            return result;
        }

        void RecordRead(SsaValue v, SsaBlock reader)
        {
            _totalReads[v] = _totalReads.TryGetValue(v, out var t) ? t + 1 : 1;
            var info = Info(reader);
            if (!info.UseCount.TryGetValue(v, out var c))
            {
                info.UseCount[v] = 1;
                if (v.IsConstant)
                    info.Consts.Add(v);
            }
            else
                info.UseCount[v] = c + 1;
        }

        // ---- 块内槽池 ----

        int PoolAlloc()
        {
            // 取用守卫：非常量归还槽只在「归还块支配当前发射块」时复用——归还块执行 ⟹
            // 其末次读取先于本块的写入；支配不成立则槽位留待后续受支配块（常量归还槽
            // 无守卫：块首物化自愈，维持原行为）。
            if (_poolFree.Count > 0 && _freeSlotRelease.Count > 0)
                _dom ??= Dominators.Compute(_fn);
            for (int i = _poolFree.Count - 1; i >= 0; i--)
            {
                var s = _poolFree[i];
                if (_freeSlotRelease.TryGetValue(s, out var release) && !_dom!.Dominates(release, _emittingBlock))
                    continue;
                _poolFree.RemoveAt(i);
                _freeSlotRelease.Remove(s);
#if DEBUG
                Debug.Assert(!_exclusiveSlots.Contains(s), $"池化分配撞上独占槽 {s}（合并臂结算污染）");
#endif
                return s;
            }
            var fresh = _poolNext++;
            if (fresh >= EcsRegisters.MaxRegisters)
                throw Fail($"寄存器溢出：函数 '{_symbol.Name}' 的寄存器需求超过帧上限 {EcsRegisters.MaxRegisters}（拆分函数或简化深表达式）");
            return fresh;
        }

        /// <summary>读取点结算：池化值在末次静态读取发射后归还槽位。
        /// 常量 = 块内计数按块分段（跨块常量在各使用块有独立槽与独立计数，若用全函数
        /// 单计数器，首块槽位永远等不到归零——泄漏）；非常量值 = 全函数读取计数
        /// （跨块值不再独占槽位：读取点只有发射序列中登记过的那些，末次结算后
        /// 循环再执行也不会遇到已发射完毕的读取点，归还即安全）。
        /// 只由结算计划回放调用：计划与登记同点推导，立即数消费天然无键，
        /// 不会出现「无读取的消费」抢走同块真实读取登记的失衡。</summary>
        void ReleaseDying(SsaBlock block, SsaValue? v)
        {
            if (v == null || !_pooled.Contains(v))
                return;
            if (v.IsConstant)
            {
                var info = Info(block);
                if (!info.UseCount.TryGetValue(v, out var left))
                    return;
                left--;
                info.UseCount[v] = left;
                if (left == 0)
                    _poolFree.Add(_slots[v]);
                return;
            }
            if (!_releaseLeft.TryGetValue(v, out var gleft))
                return;   // 合并臂等共享槽位（φ 槽）不参与结算
            gleft--;
            _releaseLeft[v] = gleft;
            Debug.Assert(gleft >= 0, "读取结算次数超过登记次数");
            if (gleft == 0 && _slots.TryGetValue(v, out var slot))
            {
                Debug.Assert(!_exclusiveSlots.Contains(slot), $"池化值归还独占槽 {slot}（合并臂未 scrub）");
                if (ReleaseSafeAt(block, v))
                {
                    _poolFree.Add(slot);
                    _freeSlotRelease[slot] = block;
                }
                // 不安全：末次读取块不后支配其余读取块——发射序上更晚的取用者可能
                // 先于本次读取执行（CFG 序 ≠ 发射序），槽位保守保留至函数结束
            }
        }

        /// <summary>归还安全性（CFG 活性守卫）：末次读取块须后支配该值全部读取块——
        /// 从任一其他读取块出发，不经末次读取块不可达出口。线性扫描的「末次静态读取
        /// 归还」以发射序为执行序，循环体内块重排/回边可倒置二者：复用者先覆写归还槽，
        /// 末次读取随即拿到陈值（LoopCarriedLocalRegressionTests 实证）。</summary>
        bool ReleaseSafeAt(SsaBlock release, SsaValue v)
        {
            foreach (var kv in _useInfo)
            {
                if (kv.Key == release || !kv.Value.UseCount.ContainsKey(v))
                    continue;
                if (!AllPathsPassThrough(kv.Key, release))
                    return false;
            }
            return true;
        }

        /// <summary>从 from 出发、不经 via，能否抵达出口（Return/未终结块）。能 → via 不后支配 from。</summary>
        bool AllPathsPassThrough(SsaBlock from, SsaBlock via)
        {
            var visited = new HashSet<SsaBlock>();
            var stack = new Stack<SsaBlock>();
            visited.Add(from);
            stack.Push(from);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur.IsReturn || !cur.IsTerminated)
                    return false;
                foreach (var s in cur.GetSuccessors())
                {
                    if (s == via || !visited.Add(s))
                        continue;
                    stack.Push(s);
                }
            }
            return true;
        }

        // ---- 常量物化（块首惰性，替代函数级 prologue）----

        void EmitLoadConst(SsaValue v) => EmitConstToSlot(v, Slot(v));

        /// <summary>常量 → 指定槽（LoadBool/LoadI 短立即数/LoadK 常量池）；块首物化与常量模板构建段共用。</summary>
        void EmitConstToSlot(SsaValue v, int dst)
        {
            switch (v.Op)
            {
                case SsaOp.ConstBool:
                    EmitIabc(EcsOpcode.LoadBool, dst, v.Const.GetBool() ? 1 : 0, 0);
                    break;
                case SsaOp.ConstByte:
                case SsaOp.ConstInt:
                case SsaOp.ConstUInt:
                    {
                        int val = v.Const.GetInt();
                        if (val >= short.MinValue && val <= short.MaxValue)
                            EmitAsBx(EcsOpcode.LoadI, dst, val);
                        else
                            EmitAbx(EcsOpcode.LoadK, dst, _pool.Add(EcsConst.FromInt(val)));
                        break;
                    }
                case SsaOp.ConstUInt64:
                    EmitAbx(EcsOpcode.LoadK, dst, _pool.Add(EcsConst.FromUInt64(v.Const.GetUInt64())));
                    break;
                case SsaOp.ConstDouble:
                    EmitAbx(EcsOpcode.LoadK, dst, _pool.Add(EcsConst.FromDouble(v.Const.GetDouble())));
                    break;
                case SsaOp.ConstPtr:
                    EmitAbx(EcsOpcode.LoadK, dst, _pool.Add(EcsConst.FromPtr(v.Const.GetPtr())));
                    break;
                case SsaOp.ConstString:
                    EmitAbx(EcsOpcode.LoadK, dst, _pool.AddString(v.ConstString ?? ""));
                    break;
                default:
                    throw Fail($"非常量值进入 prologue: {v.Op}");
            }
        }
    }
}