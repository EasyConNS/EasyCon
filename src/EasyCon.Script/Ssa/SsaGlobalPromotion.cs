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
                StoreInsts = s?.Select(x => x.Inst).ToList() ?? new(),
                LoadInsts = l?.Select(x => x.Inst).ToList() ?? new(),
            };
        }

        foreach (var (fn, globals) in functionLocals)
        {
            PromoteFunctionLocals(fn, globals);
            if (Environment.GetEnvironmentVariable("ECX_GP_TRACE") == "1")
                DumpFn(fn, "post-promotion");
        }
    }

    static void DumpFn(SsaFunction fn, string tag)
    {
        if (!fn.Symbol.Name.Contains("eval")) return;
        foreach (var b in fn.Blocks)
        {
            var bc = b.BranchCondition != null ? $" bc=v{b.BranchCondition.Id}:{b.BranchCondition.Op}(u{b.BranchCondition.Uses})" : "";
            var phis = b.Phis.Count > 0
                ? " phis=[" + string.Join(",", b.Phis.Select(p => $"v{p.Id}<-[{string.Join(",", p.ExtraArgs?.Select(a => $"v{a.Id}:{a.Op}") ?? [])}]")) + "]" : "";
            Console.Error.WriteLine($"[gp {tag}] fn={fn.Symbol.Name} b{b.Id} preds=[{string.Join(",", b.Predecessors.Select(p => p.Id))}]{bc}{phis} insts=[{string.Join(", ", b.Instructions.Select(i => $"v{i.Id}:{i.Op}(u{i.Uses})"))}]");
        }
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

    /// <summary>
    /// 单函数全局批量提升 = 按需查找式 mem2reg（Braun et al. CC 2013 §3，
    /// 与构建期 SsaVariableState 同机制；LLVM mem2reg 的支配树重命名与其语义等价）：
    /// store = writeVariable，load = readVariable；多前驱查找处建 φ，回边自引用臂
    /// 由 removeTrivialPhi 折叠。全部块视为 sealed（此阶段 CFG 已完成）。
    /// 取代旧「单遍 RPO + reaching 数组 + Pending 回填」手写数据流：共享 latch
    /// （嵌套 IF/FOR/WHILE）会让 latch 在 RPO 中先于 header 被处理，单遍无不动点的
    /// 推导填入错误臂值，循环条件被折叠成恒真 → 静默死循环（fuzz 实证缺陷）。
    /// </summary>
    static void PromoteFunctionLocals(
        SsaFunction fn,
        Dictionary<GlobalVariableSymbol, GlobalInfo> globals)
    {
        // 仅写无读：写死代码，直接删
        foreach (var gi in globals.Values)
        {
            if (gi.LoadInsts.Count != 0)
                continue;
            foreach (var inst in gi.StoreInsts)
                RemoveInst(fn, inst);
        }

        // 登记本函数内的 store/load（跨函数陈旧项由 FindBlock 过滤）；
        // 同块多次 store 后写覆盖 exitDef = 最后一次 store 的值
        var stores = new List<(GlobalVariableSymbol G, SsaValue Inst)>();
        var loads = new List<(GlobalVariableSymbol G, SsaValue Inst)>();
        foreach (var gi in globals.Values)
        {
            foreach (var inst in gi.StoreInsts)
                if (FindBlock(fn, inst) != null)
                    stores.Add((gi.Symbol, inst));
            foreach (var inst in gi.LoadInsts)
                if (FindBlock(fn, inst) != null)
                    loads.Add((gi.Symbol, inst));
        }
        if (loads.Count == 0)
        {
            foreach (var (_, inst) in stores)
                RemoveInst(fn, inst);
            return;
        }

        var exitDef = new Dictionary<(SsaBlock, GlobalVariableSymbol), SsaValue>();   // 块出口定值
        var entryDef = new Dictionary<(SsaBlock, GlobalVariableSymbol), SsaValue>();  // 块入口到达值（φ 断环缓存）
        foreach (var (g, inst) in stores)
        {
            var block = FindBlock(fn, inst)!;
            exitDef[(block, g)] = inst.Arg0!;
        }

        // 块入口到达值：多前驱 → 占位 φ 先登记断环（回边自引用臂由此产生），再填臂；
        // 无前驱（不可达）→ 零值物化（未初始化全局读取 = 0，沿用原零物化语义）
        SsaValue EntryValue(GlobalVariableSymbol g, SsaBlock block)
        {
            if (entryDef.TryGetValue((block, g), out var def))
                return def;
            if (block.Predecessors.Count == 0)
            {
                var zero = NewZero(g.Type, block, _nextId++);
                block.Instructions.Insert(0, zero);
                entryDef[(block, g)] = zero;
                return zero;
            }
            if (block.Predecessors.Count == 1)
            {
                var v = ExitValue(g, block.Predecessors[0]);
                entryDef[(block, g)] = v;
                return v;
            }
            var phi = new SsaValue(_nextId++, SsaOp.Phi, g.Type) { Block = block };
            phi.ExtraArgs = new List<SsaValue>();
            entryDef[(block, g)] = phi;
            foreach (var pred in block.Predecessors)
            {
                var arm = ExitValue(g, pred);
                phi.ExtraArgs.Add(arm);
                arm.Uses++;
            }
            block.Phis.Add(phi);
            return TryRemoveTrivialPhi(g, phi, block);
        }

        // 块出口值：块内最后一次 store 的值；块内无 store → 入口值
        SsaValue ExitValue(GlobalVariableSymbol g, SsaBlock block)
        {
            if (exitDef.TryGetValue((block, g), out var def))
                return def;
            return EntryValue(g, block);
        }

        // Braun §3.3 removeTrivialPhi：除自引用外所有臂同值 → 用该值替换 φ
        //（循环体内未改写的全局，其循环 φ 由此正确折叠为入口值）
        SsaValue TryRemoveTrivialPhi(GlobalVariableSymbol g, SsaValue phi, SsaBlock block)
        {
            SsaValue? same = null;
            foreach (var arm in phi.ExtraArgs!)
            {
                if (ReferenceEquals(arm, phi)) continue;
                if (same == null) { same = arm; continue; }
                if (!ReferenceEquals(arm, same))
                    return phi;   // 臂含多个不同值：非平凡，保留
            }
            if (same == null)
                return phi;   // 全自引用：未定义，保留
            ReplaceUsesInFn(fn, phi, same);
            // 入口值缓存全量重定向（悬挂缓存 = 嵌套循环死循环的根因，
            // 与 SsaVariableState.ReplacePhiWith 同一防御）
            var staleKeys = entryDef.Where(kv => kv.Value == phi).Select(kv => kv.Key).ToList();
            foreach (var k in staleKeys)
                entryDef[k] = same;
            return same;
        }

        // 逐块按序：store → 记录块内当前值；load → 块内前序 store 优先，否则入口值
        var removals = new List<SsaValue>();
        foreach (var block in fn.Blocks)
        {
            var cur = new Dictionary<GlobalVariableSymbol, SsaValue>();
            foreach (var inst in block.Instructions.ToList())
            {
                if (inst.Op == SsaOp.StoreGlobal)
                {
                    var g = (GlobalVariableSymbol)inst.Aux!;
                    if (!globals.TryGetValue(g, out var gi) || gi.LoadInsts.Count == 0)
                        continue;   // 跨函数全局（不提升）/ 仅写无读（已在上方删除）：保持原样
                    cur[g] = inst.Arg0!;
                    removals.Add(inst);
                }
                else if (inst.Op == SsaOp.LoadGlobal)
                {
                    var g = (GlobalVariableSymbol)inst.Aux!;
                    if (!globals.TryGetValue(g, out var gi) || gi.LoadInsts.Count == 0)
                        continue;   // 跨函数全局（不提升）：保持原样
                    SsaValue v = cur.TryGetValue(g, out var local)
                        ? local                        // 块内前序 store 的值
                        : EntryValue(g, block);        // 入口到达值（φ 按需生成）
                    ReplaceUsesInFn(fn, inst, v);
                    removals.Add(inst);
                }
            }
        }
        foreach (var inst in removals)
            RemoveInst(fn, inst);

        CollapseTrivialPhis(fn);   // 兜底：清理因臂替换而变得平凡的 φ
    }

    // ---- 工具 ----

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
        // 只删本函数块内的指令：inst.Block 可能是陈旧/跨函数归属，
        // 兜底盲删会误删其它函数的指令（fuzz 发现的 f0_23 体被清空的根因）
        var block = FindBlock(fn, inst);
        if (block == null)
            return;
        block.Instructions.Remove(inst);
        if (inst.Arg0 != null) inst.Arg0.Uses--;
        if (inst.Arg1 != null) inst.Arg1.Uses--;
        if (inst.ExtraArgs != null)
            foreach (var e in inst.ExtraArgs)
                e.Uses--;
    }
}