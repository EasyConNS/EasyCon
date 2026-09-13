using EasyCon.Script.Binding;
using EasyCon.Script.Symbols;

namespace EasyCon.Script.Ssa;

/// <summary>
/// 全局变量提升（mem2reg）：把可证明安全的全局标量提升为 SSA 寄存器值，
/// 消除 LoadGlobal/StoreGlobal 重指令（SSA 级全局提升）。
///
/// 分类（全局为模块私有，程序级扫描即模块级；进程内跨编译安全——每次 Run 重置计数器）：
///   A. 常量全局 —— 全程序仅一次 StoreGlobal 且 RHS 为常量，且该 store 位于入口函数入口块、
///      先于任何 Call/CallN（保证先于一切函数调用执行）→ 所有读替换为常量，store/load 全删。
///   B. 单函数全局 —— 读写全部落在同一函数 → 一次支配树遍历批量提升：
///      到达值替换 + 合流 phi + 零初始化路径（按全局类型化零值）+ 平凡 phi 折叠。
///      仅写无读的全局：直接删 store（写死代码），不建任何 phi。
///   C. 其余（真正跨函数非常量全局）—— 保持原样。
///
/// 不提升 ARRAY/STRUCT 类型（零初始化/句柄语义不匹配）。
/// </summary>
internal static class SsaGlobalPromotion
{
    private static int _nextId;

    /// <summary>全局提升上下文：一个全局在提升期间的私有状态。</summary>
    sealed class GlobalInfo
    {
        public required GlobalVariableSymbol Symbol;
        public required SsaValue Pending;          // 本全局专属的未定值哨兵

        public List<SsaValue> StoreInsts = new();
        public List<SsaValue> LoadInsts = new();
    }

    internal static void Run(SsaProgram program)
    {
        _nextId = 0;
        foreach (var fn in AllFunctions(program))
            foreach (var b in fn.Blocks)
                foreach (var v in b.Instructions.Concat(b.Phis))
                    if (v.Id > _nextId) _nextId = v.Id;
        _nextId++;

        var fns = AllFunctions(program).ToList();
        var stores = new Dictionary<GlobalVariableSymbol, List<(SsaFunction Fn, SsaValue Inst)>>();
        var loads = new Dictionary<GlobalVariableSymbol, List<(SsaFunction Fn, SsaValue Inst)>>();

        foreach (var fn in fns)
        {
            foreach (var block in fn.Blocks)
            {
                foreach (var inst in block.Instructions)
                {
                    if (inst.Op == SsaOp.StoreGlobal)
                    {
                        var g = (GlobalVariableSymbol)inst.Aux!;
                        if (!stores.TryGetValue(g, out var sl)) stores[g] = sl = new();
                        sl.Add((fn, inst));
                    }
                    else if (inst.Op == SsaOp.LoadGlobal)
                    {
                        var g = (GlobalVariableSymbol)inst.Aux!;
                        if (!loads.TryGetValue(g, out var ll)) loads[g] = ll = new();
                        ll.Add((fn, inst));
                    }
                }
            }
        }

        // 按函数聚合 B 类
        var functionLocals = new Dictionary<SsaFunction, Dictionary<GlobalVariableSymbol, GlobalInfo>>();

        foreach (var g in stores.Keys.Concat(loads.Keys).Distinct().ToList())
        {
            stores.TryGetValue(g, out var s);
            loads.TryGetValue(g, out var l);
            if (g.Type is ArrayType or StructType)
                continue;   // 句柄语义不匹配零初始化，不提升

            // A. 常量全局：单次常量 store，位于入口函数入口块先于任何调用
            if (s != null && s.Count == 1 && IsEntryPrologueConstStore(program, s[0]))
            {
                PromoteConst(l ?? new List<(SsaFunction, SsaValue)>(), s[0].Inst.Arg0!, fns);
                RemoveInst(s[0].Fn, s[0].Inst);
                continue;
            }

            // B. 单函数全局
            var fnsTouched = new HashSet<SsaFunction>();
            if (s != null) foreach (var (fn, _) in s) fnsTouched.Add(fn);
            if (l != null) foreach (var (fn, _) in l) fnsTouched.Add(fn);
            if (fnsTouched.Count != 1)
                continue;   // C. 真正的跨函数全局，保持原样

            var fn1 = fnsTouched.First();
            if (!functionLocals.TryGetValue(fn1, out var globals))
                functionLocals[fn1] = globals = new();
            globals[g] = new GlobalInfo
            {
                Symbol = g,
                Pending = new SsaValue(_nextId++, SsaOp.Nop, g.Type),
                StoreInsts = s?.Select(x => x.Inst).ToList() ?? new(),
                LoadInsts = l?.Select(x => x.Inst).ToList() ?? new(),
            };
        }

        foreach (var (fn, globals) in functionLocals)
            PromoteFunctionLocals(fn, globals);
    }


    static IEnumerable<SsaFunction> AllFunctions(SsaProgram program)
        => program.Functions.Values;   // MainFunction 亦在字典中，不可重复枚举

    // ---- A. 常量全局 ----

    static bool IsEntryPrologueConstStore(SsaProgram program, (SsaFunction Fn, SsaValue Inst) store)
    {
        if (program.MainFunction == null || store.Fn != program.MainFunction)
            return false;
        var entry = program.MainFunction.Blocks[0];
        if (store.Inst.Block != entry || !store.Inst.Arg0!.IsConstant)
            return false;
        foreach (var inst in entry.Instructions)
        {
            if (inst == store.Inst)
                return true;
            if (inst.Op is SsaOp.Call or SsaOp.StaticCall)
                return false;
        }
        return false;
    }

    static void PromoteConst(List<(SsaFunction Fn, SsaValue Inst)> loadSites, SsaValue constVal, List<SsaFunction> fns)
    {
        var perFn = new Dictionary<SsaFunction, SsaValue>();
        foreach (var (fn, inst) in loadSites)
        {
            if (!perFn.TryGetValue(fn, out var cv))
            {
                cv = new SsaValue(_nextId++, constVal.Op, constVal.Type)
                {
                    Const = constVal.Const,
                    ConstString = constVal.ConstString,
                    Block = fn.Entry,
                };
                fn.Entry.Instructions.Insert(0, cv);   // 常量必须在 IR 中（校验/编码器扫描依赖）
                perFn[fn] = cv;
            }
            ReplaceUsesInFn(fn, inst, cv);
        }
        foreach (var (fn, inst) in loadSites)
            RemoveInst(fn, inst);
    }

    // ---- B. 单函数全局批量提升 ----

    static void PromoteFunctionLocals(
        SsaFunction fn,
        Dictionary<GlobalVariableSymbol, GlobalInfo> globals)
    {
        // 真实逆后序（RpoIndex 未被维护，必须自行 DFS）：支配者先于被支配者处理，
        // 否则 reaching 会被哨兵污染（Break_ExitsLoop 返回 0 的根因）
        var blocks = ReversePostOrder(fn);
        var blockSet = blocks.ToHashSet();

        var tracked = globals.Values.Where(gi => gi.LoadInsts.Count > 0).ToList();
        foreach (var gi in globals.Values)
        {
            if (gi.LoadInsts.Count == 0)
                foreach (var inst in gi.StoreInsts)
                    RemoveInst(fn, inst);   // 仅写无读：写死代码
        }
        if (tracked.Count == 0)
            return;

        var indexOf = new Dictionary<GlobalVariableSymbol, int>();
        for (int i = 0; i < tracked.Count; i++)
            indexOf[tracked[i].Symbol] = i;
        var n = tracked.Count;

        var storeValues = new Dictionary<(int Gi, SsaBlock Block), List<SsaValue>>();  // 块内按序的 store 值
        var loadSites = new List<(int Gi, SsaValue Inst)>();
        foreach (var gi in tracked)
        {
            foreach (var inst in gi.StoreInsts)
            {
                var block = FindBlock(fn, inst);
                if (block == null)
                    continue;
                var key = (indexOf[gi.Symbol], block);
                if (!storeValues.TryGetValue(key, out var list))
                    storeValues[key] = list = new();
                list.Add(inst.Arg0!);
            }
            foreach (var inst in gi.LoadInsts)
            {
                if (FindBlock(fn, inst) == null)
                    continue;   // 已死的重复登记（陈旧 Block 的防御）
                loadSites.Add((indexOf[gi.Symbol], inst));
            }
        }

        var reaching = new Dictionary<SsaBlock, SsaValue[]>();   // 块出口各全局的当前值
        var pendingArms = new List<(GlobalInfo Gi, SsaValue Phi, int ArmIdx, SsaBlock Pred)>();

        foreach (var b in blocks)
        {
            var preds = b.Predecessors.Where(blockSet.Contains).ToList();
            var cur = new SsaValue[n];
            var pending = new bool[n];

            for (int gi = 0; gi < n; gi++)
            {
                var info = tracked[gi];
                if (b == fn.Entry || preds.Count == 0)
                {
                    cur[gi] = info.Pending;
                    pending[gi] = true;
                }
                else if (preds.Count == 1)
                {
                    var known = reaching.TryGetValue(preds[0], out var r);
                    cur[gi] = known ? r[gi] : info.Pending;
                    pending[gi] = !known;
                }
                else
                {
                    var vals = new SsaValue[preds.Count];
                    bool allKnown = true;
                    for (int pi = 0; pi < preds.Count; pi++)
                    {
                        if (reaching.TryGetValue(preds[pi], out var r))
                            vals[pi] = r[gi];
                        else { vals[pi] = info.Pending; allKnown = false; }
                    }
                    if (allKnown && vals.All(v => ReferenceEquals(v, vals[0])))
                    {
                        cur[gi] = vals[0];
                    }
                    else
                    {
                        var phi = new SsaValue(_nextId++, SsaOp.Phi, info.Symbol.Type) { Block = b };
                        phi.ExtraArgs ??= new List<SsaValue>();
                        for (int pi = 0; pi < preds.Count; pi++)
                        {
                            if (vals[pi] != info.Pending) vals[pi].Uses++;
                            phi.ExtraArgs.Add(vals[pi]);
                            if (vals[pi] == info.Pending)
                                pendingArms.Add((info, phi, pi, preds[pi]));
                        }
                        b.Phis.Add(phi);
                        cur[gi] = phi;
                    }
                }
            }

            // 块内线性扫描：Load→替换为当前值；Store→更新当前值（块内按序）
            foreach (var inst in b.Instructions.ToList())
            {
                if (inst.Op == SsaOp.LoadGlobal)
                {
                    if (!indexOf.TryGetValue((GlobalVariableSymbol)inst.Aux!, out var gi1))
                        continue;   // 未跟踪的全局（仅写已删路径的防御）
                    ReplaceUsesInFn(fn, inst, pending[gi1] ? tracked[gi1].Pending : cur[gi1]);
                    RemoveInst(fn, inst);
                }
                else if (inst.Op == SsaOp.StoreGlobal)
                {
                    if (!indexOf.TryGetValue((GlobalVariableSymbol)inst.Aux!, out var gi2))
                        continue;
                    cur[gi2] = inst.Arg0!;
                    pending[gi2] = false;
                    RemoveInst(fn, inst);
                }
            }

            reaching[b] = (SsaValue[])cur.Clone();
        }

        // 回填未定的回边臂
        foreach (var (gi, phi, idx, pred) in pendingArms)
        {
            var known = reaching.TryGetValue(pred, out var r);
            var val = known ? r[indexOf[gi.Symbol]] : gi.Pending;
            var old = phi.ExtraArgs![idx];
            if (old != gi.Pending) old.Uses--;
            phi.ExtraArgs[idx] = val;
            if (val != gi.Pending) val.Uses++;
        }

        // 零初始化物化：按全局类型化零值，替换各自的 Pending 哨兵残留
        foreach (var gi in tracked)
        {
            var sites = CollectPendingSites(fn, gi.Pending);
            if (sites.Count == 0)
                continue;
            var zero = NewZero(gi.Symbol.Type, fn.Entry, _nextId++);
            fn.Entry.Instructions.Insert(0, zero);
            foreach (var inst in sites)
                ReplaceUsesInFn(fn, inst, zero);
        }

        CollapseTrivialPhis(fn);
    }

    /// <summary>入口 DFS 的逆后序；不可达块排在末尾。</summary>
    static List<SsaBlock> ReversePostOrder(SsaFunction fn)
    {
        var entry = fn.Entry;
        var post = new List<SsaBlock>();
        var visited = new HashSet<SsaBlock>();
        void Dfs(SsaBlock b)
        {
            if (!visited.Add(b))
                return;
            foreach (var s in b.GetSuccessors())
                Dfs(s);
            post.Add(b);
        }
        Dfs(entry);
        post.Reverse();
        foreach (var b in fn.Blocks)
            if (visited.Add(b))
                post.Add(b);   // 不可达块兜底
        return post;
    }

    // ---- 工具 ----

    static List<SsaValue> CollectPendingSites(SsaFunction fn, SsaValue pending)
    {
        var found = new List<SsaValue>();
        foreach (var b in fn.Blocks)
        {
            foreach (var phi in b.Phis)
            {
                if (phi.ExtraArgs != null)
                    foreach (var arm in phi.ExtraArgs)
                        if (ReferenceEquals(arm, pending))
                            found.Add(pending);
            }
            foreach (var inst in b.Instructions)
            {
                if (ReferenceEquals(inst.Arg0, pending)) found.Add(pending);
                if (ReferenceEquals(inst.Arg1, pending)) found.Add(pending);
                if (inst.ExtraArgs != null)
                    foreach (var e in inst.ExtraArgs)
                        if (ReferenceEquals(e, pending)) found.Add(pending);
            }
            if (ReferenceEquals(b.BranchCondition, pending)) found.Add(pending);
        }
        return found;
    }

    static SsaValue NewZero(ScriptType type, SsaBlock block, int id)
    {
        var v = new SsaValue(id, SsaOp.ConstInt, type) { Block = block };
        if (type.Equals(ScriptType.Double)) { v.Op = SsaOp.ConstDouble; v.Const.SetDouble(0); }
        else if (type.Equals(ScriptType.UInt64)) { v.Op = SsaOp.ConstUInt64; v.Const.SetUInt64(0); }
        else if (type.Equals(ScriptType.Ptr)) { v.Op = SsaOp.ConstPtr; v.Const.SetPtr(0); }
        else if (type.Equals(ScriptType.String)) { v.Op = SsaOp.ConstString; v.ConstString = ""; }
        else v.Const.SetInt(0);
        return v;
    }

    static void CollapseTrivialPhis(SsaFunction fn)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var b in fn.Blocks)
            {
                foreach (var phi in b.Phis.ToList())
                {
                    var arms = phi.ExtraArgs;
                    if (arms == null || arms.Count == 0)
                        continue;
                    var first = arms[0];
                    if (arms.All(a => ReferenceEquals(a, first)) && !ReferenceEquals(first, phi))
                    {
                        ReplaceUsesInFn(fn, phi, first);
                        foreach (var a in arms)
                            a.Uses--;
                        b.Phis.Remove(phi);
                        changed = true;
                    }
                }
            }
        }
    }

    static void ReplaceUsesInFn(SsaFunction fn, SsaValue oldVal, SsaValue newVal)
    {
        if (ReferenceEquals(oldVal, newVal))
            return;
        foreach (var b in fn.Blocks)
        {
            foreach (var inst in b.Instructions)
                ReplaceOperand(inst, oldVal, newVal);
            foreach (var phi in b.Phis)
            {
                if (phi.ExtraArgs != null)
                    for (int i = 0; i < phi.ExtraArgs.Count; i++)
                        if (phi.ExtraArgs[i] == oldVal)
                        {
                            phi.ExtraArgs[i] = newVal;
                            newVal.Uses++;
                            oldVal.Uses--;
                        }
            }
            if (b.BranchCondition == oldVal)
            {
                b.BranchCondition = newVal;
                newVal.Uses++;
                oldVal.Uses--;
            }
        }
    }

    static void ReplaceOperand(SsaValue inst, SsaValue oldVal, SsaValue newVal)
    {
        if (inst.Arg0 == oldVal) { inst.Arg0 = newVal; newVal.Uses++; oldVal.Uses--; }
        if (inst.Arg1 == oldVal) { inst.Arg1 = newVal; newVal.Uses++; oldVal.Uses--; }
        if (inst.ExtraArgs != null)
            for (int i = 0; i < inst.ExtraArgs.Count; i++)
                if (inst.ExtraArgs[i] == oldVal)
                {
                    inst.ExtraArgs[i] = newVal;
                    newVal.Uses++;
                    oldVal.Uses--;
                }
    }

    /// <summary>inst.Block 在 CFG 优化后可能陈旧（块合并不回填），按实际包含关系定位。</summary>
    static SsaBlock? FindBlock(SsaFunction fn, SsaValue inst)
    {
        foreach (var b in fn.Blocks)
            if (b.Instructions.Contains(inst))
                return b;
        return null;
    }

    static void RemoveInst(SsaFunction fn, SsaValue inst)
    {
        var block = FindBlock(fn, inst) ?? inst.Block;
        block?.Instructions.Remove(inst);
        if (inst.Arg0 != null) inst.Arg0.Uses--;
        if (inst.Arg1 != null) inst.Arg1.Uses--;
        if (inst.ExtraArgs != null)
            foreach (var e in inst.ExtraArgs)
                e.Uses--;
    }
}