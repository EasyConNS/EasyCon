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
        for (int i = func.Blocks.Count - 1; i >= 0; i--)
        {
            if (!reachable.Contains(func.Blocks[i]))
            {
                // 递减被删除块中所有指令的操作数引用
                foreach (var inst in func.Blocks[i].Instructions)
                    SsaOptimizer.DecrementUses(inst);
                foreach (var phi in func.Blocks[i].Phis)
                    SsaOptimizer.DecrementUses(phi);
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