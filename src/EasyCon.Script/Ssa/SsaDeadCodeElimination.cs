using EasyCon.Script.Binding;
namespace EasyCon.Script.Ssa;

/// <summary>
/// 死代码消除 pass：删除 Uses==0 且无副作用的指令。
/// </summary>
static class SsaDeadCodeElimination
{
    internal static bool EliminateDeadCode(SsaFunction func)
    {
        // 迭代消除：删除一条死指令可能使其操作数也变成死代码
        bool changed = false;
        bool passChanged;
        do
        {
            passChanged = false;
            foreach (var block in func.Blocks)
                passChanged |= DceBlock(block);
            changed |= passChanged;
        } while (passChanged);

        return changed;
    }

    private static bool DceBlock(SsaBlock block)
    {
        bool changed = false;
        for (int i = block.Instructions.Count - 1; i >= 0; i--)
        {
            var inst = block.Instructions[i];
            if (IsDead(inst))
            {
                SsaOptimizer.DecrementUses(inst);
                block.Instructions.RemoveAt(i);
                changed = true;
            }
        }
        return changed;
    }

    private static bool IsDead(SsaValue inst)
    {
        if (inst.Uses > 0) return false;
        if (inst.HasSideEffect) return false;
        return true;
    }
}