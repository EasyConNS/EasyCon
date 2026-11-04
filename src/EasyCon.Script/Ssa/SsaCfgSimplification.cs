using EasyCon.Script.Binding;
namespace EasyCon.Script.Ssa;

/// <summary>
/// CFG 简化 pass：基本块合并 + 不可达块删除。
/// </summary>
static class SsaCfgSimplification
{
    // ============ 基本块合并 ============

    /// <summary>
    /// 基本块合并：将只有一个前驱且前驱只有一个后继的块合并到前驱中。
    /// 条件：
    /// 1. B 只有一个前驱 A
    /// 2. A 只有一个后继（即 B）
    /// 3. A 不是 entry 块（entry 不需要合并到前驱）
    /// 4. B 不是 entry 块
    /// </summary>
    internal static bool MergeBlocks(SsaFunction func)
    {
        bool changed = false;

        // 反向遍历，这样删除块时索引不会错乱
        for (int i = func.Blocks.Count - 1; i >= 0; i--)
        {
            var block = func.Blocks[i];

            // 不能合并 entry
            if (block == func.Entry)
                continue;

            // 需要恰好一个前驱
            if (block.Predecessors.Count != 1)
                continue;

            var pred = block.Predecessors[0];

            // 前驱必须恰好有一个后继（即 block）：无条件跳转且目标是 block
            if (pred.BranchCondition != null || pred.JumpTarget != block)
                continue;

            // 注：旧实现有 `pred.Phis.Count > 0 ⇒ skip` 守卫，理由是「phi 需多前驱」。
            // 该守卫掩盖了 MergeBlockIntoPredecessor 不重对齐 phi 臂的 bug。
            // 现 step-1 已正确按 pred 前驱数复制被移入 phi 的臂（之后被 SimplifyPhis
            // 化为全同臂消除），故无需再跳过 pred 含 phi 的情形。

            // 执行合并
            MergeBlockIntoPredecessor(func, pred, block, i);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// 将 block 合并到 pred 中。
    /// 前置条件：block 恰有一个前驱 pred，且 pred 恰有一个后继（即 block）。
    /// </summary>
    private static void MergeBlockIntoPredecessor(SsaFunction func, SsaBlock pred, SsaBlock block, int blockIndex)
    {
        // 1. 转移 block 的 Phi 到 pred，并修复臂对齐。
        //    block 的每个 phi 恰好有 1 条臂（对应 block 的唯一前驱 pred 路径）。
        //    移到 pred 后，phi 应按 pred 的前驱数复制该臂：
        //      语义正确——到达 block 的所有路径都先经 pred，故该值在 pred 每条入口路径上相同。
        //    复制后 phi 成为「全同臂」，会被后续 phi 简化（SimplifyPhis）自动消除。
        //    旧实现仅做 AddRange 而不重对齐，靠 `pred.Phis.Count>0 ⇒ skip` 守卫侥幸不崩；
        //    phi 密度上升后（真 SSA）该守卫不再成立，故此处显式重对齐。
        foreach (var phi in block.Phis)
        {
            // 捕获该 phi 的唯一臂（复制基准值），并清掉旧臂的 Uses
            SsaValue sourceArm = (phi.ExtraArgs != null && phi.ExtraArgs.Count > 0)
                ? phi.ExtraArgs[0]
                : null!;
            if (phi.ExtraArgs != null)
                foreach (var arm in phi.ExtraArgs)
                    if (arm != null) arm.Uses--;
            // 按 pred 前驱数复制全同臂
            int predCount = pred.Predecessors.Count;
            phi.ExtraArgs = new List<SsaValue>(predCount);
            for (int k = 0; k < predCount; k++)
            {
                phi.ExtraArgs.Add(sourceArm);
                if (sourceArm != null) sourceArm.Uses++;
            }
            phi.Block = pred;
            pred.Phis.Add(phi);
        }

        // 2. 将 block 的 Instructions 添加到 pred 的 Instructions
        pred.Instructions.AddRange(block.Instructions);
        foreach (var inst in block.Instructions)
            inst.Block = pred;

        // 3. 转移 block 的出口信息到 pred
        pred.BranchCondition = block.BranchCondition;
        pred.TrueSuccessor = block.TrueSuccessor;
        pred.FalseSuccessor = block.FalseSuccessor;
        pred.JumpTarget = block.JumpTarget;
        pred.IsReturn = block.IsReturn;

        // 4. 更新 block 后继的 Predecessors：将 block 替换为 pred
        //    用 ReplacePredecessor：沿同一条边到达后继的「值」语义未变（边现在源自 pred，
        //    而 pred 已吸收 block 的指令），phi 臂保持不变即可维护对齐不变量。
        foreach (var succ in block.GetSuccessors())
        {
            succ.ReplacePredecessor(block, pred);
        }

        // 5. 从 func.Blocks 中移除 block（使用已知索引，避免 O(N) 线性搜索）
        func.Blocks.RemoveAt(blockIndex);
    }

    // ============ 空 trampoline 折叠 ============

    /// <summary>
    /// 空 trampoline 块折叠：无指令、无 phi、纯 JumpTarget 的中转块 T（T→S），
    /// 前驱重定向到 S 并按 T 的臂值对齐 S 的 phi——每处省一条 Jmp（常驻循环周边路径，
    /// FOR 嵌套场景每轮 1-2 条）。MergeBlocks 不覆盖此形态：T 的前驱是条件分支块
    /// （两个后继）时无法合并入前驱，T 作为「合并汇」幸存到编码期，只贡献一条跳转。
    /// 语义依据：T 无 phi 无指令 ⟹ 经 T 到达 S 的所有路径携带同一臂值（T 的原臂），
    /// 重定向后按前驱逐边补臂即可（Predecessors↔ExtraArgs 对齐由增删配对维护）。
    /// </summary>
    internal static bool FoldEmptyTrampolines(SsaFunction func)
    {
        bool changed = false;
        for (int i = func.Blocks.Count - 1; i >= 0; i--)
        {
            var t = func.Blocks[i];
            if (t == func.Entry)
                continue;
            if (t.Instructions.Count > 0 || t.Phis.Count > 0)
                continue;
            if (t.BranchCondition != null || t.IsReturn)
                continue;
            var s = t.JumpTarget;
            if (s == null || s == t)
                continue;
            int tIdx = s.Predecessors.IndexOf(t);
            if (tIdx < 0)
                continue;   // 无前驱：死块，RemoveUnreachableBlocks 处理
            var preds = t.Predecessors.ToList();
            if (preds.Contains(s))
                continue;   // S→T→S 自环形态：重定向即自跳，保守跳过
            if (preds.Any(p => p.TrueSuccessor == s || p.FalseSuccessor == s || p.JumpTarget == s))
                continue;   // 重定向将产生重复边（P→S 既有直连又有经 T）：SCCP 的 executableEdges
                            // 是 (from,to) 集合，φ 双臂被同一条边标记门控、meet 误并两臂值
                            //（素数筛 9999 实证）——此形态保留 trampoline 不折

            // T 贡献给 S 的臂值（删除前捕获）
            var arms = new List<SsaValue?>();
            foreach (var phi in s.Phis)
                arms.Add(phi.ExtraArgs != null && tIdx < phi.ExtraArgs.Count ? phi.ExtraArgs[tIdx] : null);

            // 前驱重定向到 S
            foreach (var p in preds)
            {
                if (p.TrueSuccessor == t) p.TrueSuccessor = s;
                if (p.FalseSuccessor == t) p.FalseSuccessor = s;
                if (p.JumpTarget == t) p.JumpTarget = s;
            }

            // S：摘除 T（臂随删），再按新前驱逐边补臂（值 = T 的原臂）
            s.RemovePredecessorAt(tIdx);
            foreach (var p in preds)
            {
                s.Predecessors.Add(p);
                for (int k = 0; k < s.Phis.Count; k++)
                {
                    var phi = s.Phis[k];
                    phi.ExtraArgs ??= new List<SsaValue>();
                    var arm = arms[k];
                    if (arm == null)
                        continue;   // T 摘除前臂缺失（防御）：占位自引用由 FillPhiArms 语义兜底
                    phi.ExtraArgs.Add(arm);
                    arm.Uses++;
                }
            }

            func.Blocks.RemoveAt(i);
            changed = true;
            if (Environment.GetEnvironmentVariable("ECX_PASS_TRACE") == "1")
                Console.Error.WriteLine($"[tramp] 折叠空中转块 -> b{s.Id}（fn={func.Symbol.Name}）");
        }
        return changed;
    }

    // ============ 不可达块删除 ============

    internal static bool RemoveUnreachableBlocks(SsaFunction func)
    {
        var reachable = new HashSet<SsaBlock>();
        var stack = new Stack<SsaBlock>();
        stack.Push(func.Entry);
        while (stack.Count > 0)
        {
            var block = stack.Pop();
            if (!reachable.Add(block)) continue;
            foreach (var succ in block.GetSuccessors())
                stack.Push(succ);
        }

        bool changed = false;
        if (Environment.GetEnvironmentVariable("ECX_UR_TRACE") == "1" && func.Symbol.Name.Contains("eval"))
        {
            var removedIds = func.Blocks.Where(b => !reachable.Contains(b)).Select(b => b.Id).ToList();
            if (removedIds.Count > 0)
            {
                Console.Error.WriteLine($"[ur] fn={func.Symbol.Name} entry=b{func.Entry.Id} reachable={reachable.Count}/{func.Blocks.Count} removed=[{string.Join(",", removedIds)}]");
                foreach (var b in func.Blocks)
                {
                    var r = reachable.Contains(b) ? "R" : "X";
                    var bc = b.BranchCondition != null ? $" bc=v{b.BranchCondition.Id}:{b.BranchCondition.Op}" : (b.JumpTarget != null ? $" jmp->b{b.JumpTarget.Id}" : (b.IsReturn ? " ret" : " NO-TERM"));
                    var phis = b.Phis.Count > 0 ? " phis" : "";
                    Console.Error.WriteLine($"[urb {r}] b{b.Id} preds=[{string.Join(",", b.Predecessors.Select(p => p.Id))}]{bc}{phis} insts={b.Instructions.Count}");
                }
            }
            // 断裂边诊断：被删块存在「可达前驱」→ 该前驱的 GetSuccessors 漏边
            foreach (var b in func.Blocks)
            {
                if (reachable.Contains(b)) continue;
                var rp = b.Predecessors.Where(reachable.Contains).ToList();
                if (rp.Count > 0)
                {
                    var missing = string.Join(",", rp.SelectMany(p => p.GetSuccessors()).Where(s => s == b).Select(_ => "edge").DefaultIfEmpty("NONE-IN-SUCCS"));
                    Console.Error.WriteLine($"[ur-cut] b{b.Id} 可达前驱=[{string.Join(",", rp.Select(p => p.Id))}] 该前驱 successors 含本块={missing}");
                    foreach (var p in rp)
                        Console.Error.WriteLine($"[ur-cut]   pred b{p.Id}: bc={(p.BranchCondition != null ? $"v{p.BranchCondition.Id}:{p.BranchCondition.Op}" : "null")} jump={(p.JumpTarget != null ? $"b{p.JumpTarget.Id}" : "null")} isRet={p.IsReturn}");
                }
            }
        }
        for (int i = func.Blocks.Count - 1; i >= 0; i--)
        {
            if (!reachable.Contains(func.Blocks[i]))
            {
                var doomed = func.Blocks[i];
                // 递减被删除块中所有指令的操作数引用
                foreach (var inst in doomed.Instructions)
                    SsaOptimizer.DecrementUses(inst);
                foreach (var phi in doomed.Phis)
                    SsaOptimizer.DecrementUses(phi);
                // 仍被外部引用的常量（如其它存活块 φ 的臂）迁移到入口块再删：
                // 常量自包含（无操作数依赖），迁移后引用不悬空。
                // fuzz 实证：dedup 代表/循环 init 常量所在块因合法折叠不可达后被删，
                // Φ 臂指向已移出 IR 的 ConstInt（GP-off 12 种子签名）。
                foreach (var inst in doomed.Instructions)
                {
                    if (inst.Uses > 0 && inst.IsConstant)
                    {
                        inst.Block = func.Entry;
                        func.Entry.Instructions.Add(inst);
                    }
                }
                func.Blocks.RemoveAt(i);
                changed = true;
            }
        }

        // 清理 Predecessors 引用：移除指向不可达块的前驱。
        // 用 RemovePredecessorAt 同步删除对应 phi 臂（并递减其 Uses）：
        // 该边永不被执行，其 phi 臂为死臂，删除是正确的且维持 Predecessors↔ExtraArgs 对齐。
        foreach (var block in func.Blocks)
        {
            for (int i = block.Predecessors.Count - 1; i >= 0; i--)
            {
                if (!reachable.Contains(block.Predecessors[i]))
                    block.RemovePredecessorAt(i);
            }
        }

        return changed;
    }
}