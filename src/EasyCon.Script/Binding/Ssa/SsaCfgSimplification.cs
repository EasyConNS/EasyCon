namespace EasyCon.Script.Binding.Ssa;

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

            // 前驱必须恰好有一个后继（即 block）
            var predSuccs = pred.GetSuccessors().ToList();
            if (predSuccs.Count != 1 || predSuccs[0] != block)
                continue;

            // 如果前驱有 Phi 节点，不能合并（Phi 节点需要多个前驱）
            if (pred.Phis.Count > 0)
                continue;

            // 执行合并
            MergeBlockIntoPredecessor(func, pred, block);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// 将 block 合并到 pred 中。
    /// </summary>
    private static void MergeBlockIntoPredecessor(SsaFunction func, SsaBlock pred, SsaBlock block)
    {
        // 1. 将 block 的 Phis 添加到 pred 的 Phis（虽然此时 block 只有一个前驱，但可能有 Phi）
        pred.Phis.AddRange(block.Phis);
        foreach (var phi in block.Phis)
            phi.Block = pred;

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
        foreach (var succ in block.GetSuccessors())
        {
            for (int j = 0; j < succ.Predecessors.Count; j++)
            {
                if (succ.Predecessors[j] == block)
                {
                    succ.Predecessors[j] = pred;
                    break;
                }
            }
        }

        // 5. 从 func.Blocks 中移除 block
        func.Blocks.Remove(block);
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

        // 清理 Predecessors 引用
        foreach (var block in func.Blocks)
        {
            for (int i = block.Predecessors.Count - 1; i >= 0; i--)
            {
                if (!reachable.Contains(block.Predecessors[i]))
                    block.Predecessors.RemoveAt(i);
            }
        }

        return changed;
    }
}