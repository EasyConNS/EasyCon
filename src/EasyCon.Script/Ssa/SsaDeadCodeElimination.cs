using EasyCon.Script.Binding;
namespace EasyCon.Script.Ssa;

/// <summary>
/// 死代码消除 pass：删除 Uses==0 且无副作用的指令。
/// 使用 worklist 驱动的 mark-sweep 算法：
/// 1. 标记初始死指令
/// 2. Worklist 传播死性（递减操作数 Uses）
/// 3. Sweep 阶段统一移除
/// </summary>
static class SsaDeadCodeElimination
{
    internal static bool EliminateDeadCode(SsaFunction func)
    {
        // Phase 1: 初始扫描找到所有死指令
        var dead = new HashSet<SsaValue>();
        var worklist = new Queue<SsaValue>();

        foreach (var block in func.Blocks)
        {
            foreach (var inst in block.Instructions)
            {
                if (inst.Uses == 0 && !inst.HasSideEffect && dead.Add(inst))
                    worklist.Enqueue(inst);
            }
            // Phi 同样参与死代码消除：结果无用的 phi 会经 worklist 递减其臂 Uses，
            // 若 sweep 只删指令不删 phi，死 phi 的臂引用会成为悬空（fuzz 悬空 ConstInt 根因）
            foreach (var phi in block.Phis)
            {
                if (phi.Uses == 0 && dead.Add(phi))
                    worklist.Enqueue(phi);
            }
        }

        if (dead.Count == 0) return false;

        // Phase 2: worklist 传播死性 — 递减操作数 Uses，发现新的死指令
        while (worklist.Count > 0)
        {
            var inst = worklist.Dequeue();

            if (inst.Arg0 != null) { inst.Arg0.Uses--; CheckDead(inst.Arg0, dead, worklist); }
            if (inst.Arg1 != null) { inst.Arg1.Uses--; CheckDead(inst.Arg1, dead, worklist); }
            if (inst.ExtraArgs != null)
            {
                foreach (var arg in inst.ExtraArgs)
                {
                    arg.Uses--;
                    CheckDead(arg, dead, worklist);
                }
            }
        }

        // Phase 3: sweep — 统一从 block 中移除所有死指令（含死 phi：与 Phase 2 的
        // 臂 Uses 递减配套，否则死 phi 残留且其臂引用已衰减的值）
        foreach (var block in func.Blocks)
        {
            for (int i = block.Instructions.Count - 1; i >= 0; i--)
            {
                if (dead.Contains(block.Instructions[i]))
                    block.Instructions.RemoveAt(i);
            }
            for (int i = block.Phis.Count - 1; i >= 0; i--)
            {
                if (dead.Contains(block.Phis[i]))
                    block.Phis.RemoveAt(i);
            }
        }

        return true;
    }

    private static void CheckDead(SsaValue inst, HashSet<SsaValue> dead, Queue<SsaValue> worklist)
    {
        if (inst.Uses == 0 && !inst.HasSideEffect && dead.Add(inst))
            worklist.Enqueue(inst);
    }
}