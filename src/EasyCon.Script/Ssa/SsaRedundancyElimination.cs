using EasyCon.Script.Binding;
using EasyCon.Script.Symbols;

namespace EasyCon.Script.Ssa;

/// <summary>
/// 冗余消除 pass：拷贝传播 + 全局 CSE。
/// </summary>
static class SsaRedundancyElimination
{
    // ============ 拷贝传播 ============

    internal static bool PropagateCopies(SsaFunction func)
    {
        bool changed = false;
        foreach (var block in func.Blocks)
            changed |= PropagateCopiesBlock(func, block);
        return changed;
    }

    private static bool PropagateCopiesBlock(SsaFunction func, SsaBlock block)
    {
        var known = new Dictionary<object, SsaValue>();
        bool changed = false;

        for (int i = 0; i < block.Instructions.Count; i++)
        {
            var inst = block.Instructions[i];

            if (inst.Op is SsaOp.StoreLocal or SsaOp.StoreGlobal)
            {
                if (inst.Aux != null && inst.Arg0 != null)
                    known[inst.Aux] = inst.Arg0;
            }
            else if (inst.Op is SsaOp.LoadLocal or SsaOp.LoadGlobal)
            {
                if (inst.Aux != null && known.TryGetValue(inst.Aux, out var knownValue))
                {
                    SsaOptimizer.ReplaceAllUsesInFunction(func, inst, knownValue);
                    inst.Uses = 0;
                    block.Instructions.RemoveAt(i);
                    i--;
                    changed = true;
                }
            }
            else if (inst.Op is SsaOp.Call or SsaOp.StaticCall)
            {
                // 只有用户自定义函数可能修改全局变量，内置函数不需要清空
                if (inst.Aux is FunctionSymbol fs && !BuiltinFunctions.IsBuiltin(fs))
                    known.Clear();
            }
        }

        return changed;
    }

    // ============ 全局 CSE ============

    internal static bool EliminateCommonSubexpressions(SsaFunction func)
    {
        ComputeRpo(func);
        bool changed = false;

        // 每个块出口的可用表达式
        var exitAvail = new Dictionary<SsaBlock, Dictionary<CseKey, SsaValue>>();

        // 按 RPO 顺序处理
        var sortedBlocks = func.Blocks.OrderBy(b => b.RpoIndex).ToList();

        foreach (var block in sortedBlocks)
        {
            // 计算入口可用：所有前驱块出口可用表达式的交集
            var available = new Dictionary<CseKey, SsaValue>();
            if (block.Predecessors.Count > 0)
            {
                bool first = true;
                foreach (var pred in block.Predecessors)
                {
                    if (!exitAvail.TryGetValue(pred, out var predAvail))
                    {
                        // 前驱尚未处理（RPO 中不应出现，但安全起见清空）
                        available.Clear();
                        break;
                    }
                    if (first)
                    {
                        foreach (var kv in predAvail)
                            available[kv.Key] = kv.Value;
                        first = false;
                    }
                    else
                    {
                        // 仅保留两个前驱中共有且指向同一 SsaValue 的键
                        var keysToRemove = new List<CseKey>();
                        foreach (var k in available.Keys)
                        {
                            if (!predAvail.TryGetValue(k, out var predVal) || predVal.Id != available[k].Id)
                                keysToRemove.Add(k);
                        }
                        foreach (var k in keysToRemove)
                            available.Remove(k);
                    }
                }
            }

            // 前向扫描
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];

                // 跳过不可 CSE 的指令
                if (inst.HasSideEffect) continue;
                if (inst.IsConstant) continue;
                if (inst.Op is SsaOp.LoadLocal or SsaOp.LoadGlobal or SsaOp.Phi or SsaOp.Nop
                    or SsaOp.LoadField or SsaOp.LoadFieldIndex or SsaOp.LoadIndex) continue;
                if (inst.Arg0 == null && inst.Arg1 == null) continue;

                var key = new CseKey(inst.Op,
                    inst.Arg0?.Id ?? -1,
                    inst.Arg1?.Id ?? -1,
                    inst.ExtraArgs != null ? CseKey.HashExtraArgs(inst.ExtraArgs) : 0,
                    inst.Aux != null ? inst.Aux.GetHashCode() : 0);

                if (available.TryGetValue(key, out var existing))
                {
                    // 找到相同表达式，用 existing 替换 inst 的所有使用
                    SsaOptimizer.ReplaceAllUsesInBlock(block, inst, existing);
                    existing.Uses += inst.Uses;
                    inst.Uses = 0;
                    block.Instructions.RemoveAt(i);
                    i--;
                    changed = true;
                }
                else
                {
                    available[key] = inst;
                }
            }

            exitAvail[block] = new Dictionary<CseKey, SsaValue>(available);
        }

        return changed;
    }

    private static void ComputeRpo(SsaFunction func)
    {
        // 迭代 DFS 求 RPO，避免深 CFG 递归栈溢出
        var visited = new HashSet<SsaBlock>();
        var postOrder = new List<SsaBlock>();
        var stack = new Stack<(SsaBlock Block, bool Processed)>();
        stack.Push((func.Entry, false));

        while (stack.Count > 0)
        {
            var (block, processed) = stack.Pop();
            if (!visited.Add(block)) continue;
            if (processed)
            {
                postOrder.Add(block);
                continue;
            }
            // 先标记为待处理（post-order），再压入后继
            stack.Push((block, true));
            // 反向压入以保持原始后继顺序
            var succs = block.GetSuccessors().Reverse();
            foreach (var succ in succs)
                stack.Push((succ, false));
        }

        // RPO = post-order 的逆序
        int index = 0;
        for (int i = postOrder.Count - 1; i >= 0; i--)
            postOrder[i].RpoIndex = index++;
    }

    internal readonly record struct CseKey(SsaOp Op, int Arg0Id, int Arg1Id, int ExtraHash, int AuxHash = 0)
    {
        public static int HashExtraArgs(List<SsaValue> extras)
        {
            unchecked
            {
                int h = 17;
                foreach (var v in extras)
                    h = h * 31 + v.Id;
                return h;
            }
        }
    }
}