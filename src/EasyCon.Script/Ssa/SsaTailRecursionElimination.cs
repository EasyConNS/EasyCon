using EasyCon.Script.Symbols;

namespace EasyCon.Script.Ssa;

/// <summary>
/// 尾递归消除（Tail Recursion Elimination）pass：
/// 将 SSA IR 中的尾递归调用转换为循环结构。
///
/// 变换前:
///   BB_tail: ... t1=n-1; t2=acc+n; t3=call self(t1, t2); return t3
/// 变换后:
///   BB_tail: ... t1=n-1; t2=acc+n; store.local n, t1; store.local acc, t2; br BB_entry
///
/// 变换后，解释器和 JIT 都不再需要特殊处理尾递归。
/// </summary>
static class SsaTailRecursionElimination
{
    /// <summary>全局下一个可用 SSA ID（跨函数唯一）。由 SsaOptimizer 设置。</summary>
    internal static int GlobalNextId;
    /// <summary>
    /// 对函数执行尾递归消除。返回 true 表示做了变换。
    /// </summary>
    internal static bool Eliminate(SsaFunction func)
    {
        var sym = func.Symbol;
        var parameters = sym.Parameters;
        if (parameters.Length == 0)
            return false; // 无参函数不可能自递归（保守）

        bool changed = false;

        // 收集所有尾递归块
        var tailBlocks = new List<(SsaBlock block, SsaValue callInst, SsaValue retInst)>();

        foreach (var block in func.Blocks)
        {
            if (!block.IsReturn) continue;
            if (block.Instructions.Count == 0) continue;

            // 找 Return 指令
            var retInst = block.Instructions[^1];
            if (retInst.Op != SsaOp.Return) continue;
            if (retInst.Arg0 == null) continue;

            // Return 的 Arg0 必须是 Call 到自身
            var callInst = retInst.Arg0;
            if (callInst.Op is not (SsaOp.Call or SsaOp.StaticCall)) continue;
            if (callInst.Aux is not FunctionSymbol called || !called.Equals(sym)) continue;

            // 确认 Call 紧接在 Return 之前（尾调用位置）
            int callIdx = block.Instructions.IndexOf(callInst);
            if (callIdx < 0 || callIdx != block.Instructions.Count - 2) continue;

            tailBlocks.Add((block, callInst, retInst));
        }

        if (tailBlocks.Count == 0) return false;

        var entry = func.Entry;

        foreach (var (block, callInst, retInst) in tailBlocks)
        {
            // 1. 收集 Call 的参数值
            var argValues = new List<SsaValue>();
            if (callInst.Arg0 != null) argValues.Add(callInst.Arg0);
            if (callInst.ExtraArgs != null) argValues.AddRange(callInst.ExtraArgs);

            if (argValues.Count != parameters.Length)
                continue; // 参数数量不匹配，跳过（保守）

            // 2. 为每个参数创建 StoreLocal，将新值写回参数变量
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var newVal = argValues[i];

                var store = new SsaValue(
                    System.Threading.Interlocked.Increment(ref GlobalNextId) - 1,
                    SsaOp.StoreLocal, ScriptType.Void)
                {
                    Arg0 = newVal,
                    Aux = param,
                    Block = block
                };
                block.Instructions.Insert(block.Instructions.Count - 1, store);
                newVal.Uses++;
            }

            // 3. 删除 Call 和 Return 指令
            // 先释放 Call 的操作数引用
            SsaOptimizer.ReleaseOperands(callInst);
            // 释放 Return 的 Arg0 引用（指向 Call）
            if (retInst.Arg0 != null) { retInst.Arg0.Uses--; retInst.Arg0 = null; }
            block.Instructions.Remove(callInst);
            block.Instructions.Remove(retInst);

            // 4. 将块出口改为无条件跳转到 entry
            block.IsReturn = false;
            block.BranchCondition = null;
            block.TrueSuccessor = null;
            block.FalseSuccessor = null;
            block.JumpTarget = entry;

            // 5. entry 增加此前驱
            if (!entry.Predecessors.Contains(block))
                entry.Predecessors.Add(block);

            changed = true;
        }

        // 如果做了变换，entry 可能有 phi 节点需要处理。
        // 尾递归变换后 entry 新增了回边前驱，已有的 phi 需要增加对应臂。
        if (changed)
        {
            FixEntryPhis(func, entry, tailBlocks.Select(t => t.block).ToList());
        }

        return changed;
    }

    /// <summary>
    /// 尾递归变换后，entry 块新增了回边前驱，需要为已有 phi 增加占位臂。
    /// 占位臂用 phi 自身（自引用），后续的拷贝传播/常量折叠会处理。
    /// </summary>
    private static void FixEntryPhis(SsaFunction func, SsaBlock entry, List<SsaBlock> newPreds)
    {
        foreach (var phi in entry.Phis)
        {
            foreach (var pred in newPreds)
            {
                // 占位：phi 引用自身
                phi.ExtraArgs ??= [];
                phi.ExtraArgs.Add(phi);
                // 自引用不递增 Uses（phi 不应算作自身的使用）
                entry.Predecessors.Add(pred);
            }
        }

        // 对于没有 phi 的情况，仍需添加前驱
        if (entry.Phis.Count == 0)
        {
            foreach (var pred in newPreds)
            {
                if (!entry.Predecessors.Contains(pred))
                    entry.Predecessors.Add(pred);
            }
        }
    }
}