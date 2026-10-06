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
            SsaOp.EqInt => 0, SsaOp.NeqInt => 1, SsaOp.LtInt => 2, SsaOp.LeqInt => 3, SsaOp.GtInt => 4, SsaOp.GeqInt => 5,
            SsaOp.EqUInt => 6, SsaOp.NeqUInt => 7, SsaOp.LtUInt => 8, SsaOp.LeqUInt => 9, SsaOp.GtUInt => 10, SsaOp.GeqUInt => 11,
            SsaOp.EqDouble => 12, SsaOp.NeqDouble => 13, SsaOp.LtDouble => 14, SsaOp.LeqDouble => 15, SsaOp.GtDouble => 16, SsaOp.GeqDouble => 17,
            SsaOp.EqUInt64 => 18, SsaOp.NeqUInt64 => 19, SsaOp.LtUInt64 => 20, SsaOp.LeqUInt64 => 21, SsaOp.GtUInt64 => 22, SsaOp.GeqUInt64 => 23,
            SsaOp.EqBool => 0, SsaOp.NeqBool => 1,                       // Bool 槽载荷 = I32 0/1，按 i32 比较
            SsaOp.EqByte => 0, SsaOp.NeqByte => 1, SsaOp.LtByte => 2, SsaOp.LeqByte => 3, SsaOp.GtByte => 4, SsaOp.GeqByte => 5,
            SsaOp.EqPtr => 18, SsaOp.NeqPtr => 19,                       // Ptr = I64 载荷
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
                // 零 φ 拷贝：遍历全部臂（各臂守卫独立成立；同 φ 多臂在不同前驱边执行，
                // 分时写同一槽即 φ 语义本身）。常量臂不合并——块首物化在 φ 槽上会覆写
                // 同迭代仍活跃的旧值。
            }
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
            // 帧槽读取者 = 参数入口 LoadLocal（Call ABI 播种）；仅给仍被引用的符号保留槽位；
            // 参数窗 [0, NParams) 恒等映射保留，其余（防御性：若未来出现非参数 LoadLocal）压缩到参数窗之后。
            var liveLocalSlots = new SortedSet<int>();
            foreach (var block in _fn.Blocks)
                foreach (var inst in block.Instructions)
                {
                    if (inst.Op == SsaOp.LoadLocal && inst.Aux is LocalVariableSymbol lv)
                        liveLocalSlots.Add(lv.Slot.Index);
                    else if (inst.Op == SsaOp.StoreLocal && inst.Aux is ParamSymbol ps)
                        liveLocalSlots.Add(ps.Slot.Index);
                }
            int compacted = _symbol.Parameters.Length;
            foreach (var oldIdx in liveLocalSlots)
            {
                if (oldIdx < _symbol.Parameters.Length)
                    _localSlotRemap[oldIdx] = oldIdx;
                else
                    _localSlotRemap[oldIdx] = compacted++;
            }
            _next = compacted;

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

            // 2. 分类：跨块值专用槽；块内值登记池化；常量已按块登记
            bool hasPhi = false, hasNeq = false, hasTpl = false;
            int maxArity = 1;
            foreach (var block in _fn.Blocks)
            {
                var info = Info(block);
                foreach (var phi in block.Phis)
                {
                    if (!_totalReads.ContainsKey(phi))
                        continue;   // 死 φ（防线 1）：全函数无读取——不占槽，前驱边不产生副本（EmitEdgeCopies 跳过）
                    _slots[phi] = _next++;
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
                    // ForStep：分支内嵌的自增目标槽必须独占（原地写），恒跨块专属槽；
                    // 零 φ 拷贝合并例外：目标槽 = φ 槽（C 直写 φ 槽，回边副本自消）
                    if (inst.Op == SsaOp.ForStep)
                    {
                        _slots[inst] = _coalesced.TryGetValue(inst, out var phiSlot) ? _slots[phiSlot] : _next++;
                        continue;
                    }
                    if (inst.Op is SsaOp.NeqInt or SsaOp.NeqUInt or SsaOp.NeqDouble or SsaOp.NeqUInt64
                        or SsaOp.NeqBool or SsaOp.NeqByte or SsaOp.NeqString or SsaOp.NeqPtr)
                        hasNeq = true;
                    // 常量模板降级：元素不占槽也不进 staging（maxArity 不计入），结果槽照常分类
                    bool templateInit = IsTemplateArrayInit(inst);
                    if (templateInit)
                        hasTpl = true;
                    int arity = templateInit ? 0 : ArityOf(inst);
                    if (arity > maxArity) maxArity = arity;

                    if (inst.IsConstant)
                    {
                        if (!_hoisted.Contains(inst))
                            _pooled.Add(inst);        // 常量池化：Consts 已在步骤 1 登记
                        // 外提常量：共享组槽位已在 HoistLoopInvariantConsts 定槽，跳过
                        continue;
                    }
                    if (!NeedsSlot(inst.Op)) continue;

                    int readsHere = info.UseCount.TryGetValue(inst, out var c) ? c : 0;
                    int totalReads = _totalReads.TryGetValue(inst, out var t) ? t : 0;
                    if (totalReads > readsHere)
                        _slots[inst] = _next++;         // 跨块：专用槽
                    else
                    {
                        _pooled.Add(inst);              // 块内：定义时入池
                    }
                }
            }

            if (hasNeq) _neqTemp = _next++;
            if (hasPhi) _scratch = _next++;
            if (hasTpl)
            {
                _tplGuard = _next++;
                _tplArr = _next++;
                _tplChunk = _next++;
            }
            _stagingBase = _next;
            var stagingCount = maxArity;
            if (hasTpl)
            {
                // 常量模板分块构建：staging 区须容纳一个分块（NewArrV 的 C:8 上限 255 之内取满）
                _tplChunkSize = Math.Min(TemplateChunkSize, 254 - _stagingBase);
                if (_tplChunkSize <= 0)
                    throw Fail("常量模板构建 staging 槽不足（函数局部槽过多）");
                if (stagingCount < _tplChunkSize)
                    stagingCount = _tplChunkSize;
            }
            _receiveSlot = _stagingBase + stagingCount;
            _next += stagingCount + 1;
            _maxArity = maxArity;

            // 池区在所有保留槽之后生长
            _poolNext = _next;
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
            if (_poolFree.Count > 0)
            {
                var s = _poolFree[_poolFree.Count - 1];
                _poolFree.RemoveAt(_poolFree.Count - 1);
                return s;
            }
            return _poolNext++;
        }

        /// <summary>读取点结算：池化值在读取块内的剩余计数减一，归零即归还槽位。
        /// 计数按块分段（UseCount）——跨块常量在各使用块有独立槽与独立计数，
        /// 若用全函数单计数器，首块槽位永远等不到归零（泄漏）。
        /// 只由结算计划回放调用：计划与登记同点推导，立即数消费天然无键，
        /// 不会出现「无读取的消费」抢走同块真实读取登记的失衡。</summary>
        void ReleaseDying(SsaBlock block, SsaValue? v)
        {
            if (v == null || !_pooled.Contains(v))
                return;
            var info = Info(block);
            if (!info.UseCount.TryGetValue(v, out var left))
                return;
            left--;
            info.UseCount[v] = left;
            Debug.Assert(left >= 0, "读取结算次数超过登记次数");
            if (left == 0)
                _poolFree.Add(_slots[v]);
        }

        static int ArityOf(SsaValue inst)
        {
            int n = inst.Arg0 != null ? 1 : 0;
            if (inst.ExtraArgs != null) n += inst.ExtraArgs.Count;
            return inst.Op switch
            {
                SsaOp.Call or SsaOp.StaticCall or SsaOp.ArrayInit => n,
                SsaOp.Capture or SsaOp.Ocr or SsaOp.Roi or SsaOp.OcrInit => n,
                _ => 0,
            };
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