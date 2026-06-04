using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;
using System.Linq;

namespace EasyCon.Script.Ssa;

/// <summary>
/// SSA IR 优化 pass 驱动器。
/// 对每个 SsaFunction 就地优化，迭代至不动点：
/// SCCP → 代数化简 → 拷贝传播 → 全局 CSE → 死代码消除 → CFG 简化 → 常量去重。
/// </summary>
static class SsaOptimizer
{
    public static void Optimize(SsaProgram program)
    {
        // 先移除不可达函数
        SsaInterprocedural.RemoveUnreachableFunctions(program);

        // 内联 trivial 函数（单基本块 + 仅 Return）
        bool changed;
        int inlineIterations = 0;
        const int MaxInlineIterations = 3;
        do
        {
            changed = false;
            if (program.MainFunction != null)
                changed |= SsaInterprocedural.InlineTrivialFunctions(program, program.MainFunction);
            foreach (var func in program.Functions.Values)
                changed |= SsaInterprocedural.InlineTrivialFunctions(program, func);
        } while (changed && ++inlineIterations < MaxInlineIterations);

        // 内联后做函数内优化
        if (program.MainFunction != null)
            OptimizeFunction(program.MainFunction);
        foreach (var func in program.Functions.Values)
            OptimizeFunction(func);

        // 内联 stdlib 包装函数（需要跨函数查表，放在函数内优化之后）
        if (program.MainFunction != null)
            SsaInterprocedural.InlineIntrinsicWrappers(program, program.MainFunction);

        // 内联后包装函数不再被引用，再次清理
        SsaInterprocedural.RemoveUnreachableFunctions(program);
    }

    internal static void OptimizeFunction(SsaFunction func)
    {
        bool changed;
        int iterations = 0;
        const int MaxIterations = 5;
        do
        {
            changed = false;
            changed |= SsaConstantPropagation.Run(func);
            changed |= SsaConstantPropagation.AlgebraicSimplify(func);
            changed |= SsaRedundancyElimination.PropagateCopies(func);
            changed |= SsaRedundancyElimination.EliminateCommonSubexpressions(func);
            changed |= SsaDeadCodeElimination.EliminateDeadCode(func);
            changed |= SsaCfgSimplification.MergeBlocks(func);
            changed |= SsaCfgSimplification.RemoveUnreachableBlocks(func);
            changed |= SsaConstantPropagation.DeduplicateConstants(func);

        } while (changed && ++iterations < MaxIterations);
    }

    // ============ 共享工具方法 ============

    /// <summary>
    /// 将 func 内所有对 oldValue 的引用替换为 newValue（指令 + phi + 终止条件）。
    /// 同时更新 Uses 计数。
    /// </summary>
    internal static void ReplaceAllUsesInFunction(SsaFunction func, SsaValue oldValue, SsaValue newValue)
    {
        foreach (var block in func.Blocks)
        {
            foreach (var inst in block.Instructions)
                ReplaceOperand(inst, oldValue, newValue);
            foreach (var phi in block.Phis)
                ReplaceOperand(phi, oldValue, newValue);
            if (block.BranchCondition == oldValue)
                block.BranchCondition = newValue;
        }
        newValue.Uses += oldValue.Uses;
        oldValue.Uses = 0;
    }

    /// <summary>
    /// 将 block 内所有对 oldValue 的引用替换为 newValue（指令 + phi + 终止条件）。
    /// 不更新 Uses 计数（调用者负责）。
    /// </summary>
    internal static void ReplaceAllUsesInBlock(SsaBlock block, SsaValue oldValue, SsaValue newValue)
    {
        foreach (var inst in block.Instructions)
            ReplaceOperand(inst, oldValue, newValue);
        foreach (var phi in block.Phis)
            ReplaceOperand(phi, oldValue, newValue);
        if (block.BranchCondition == oldValue)
            block.BranchCondition = newValue;
    }

    internal static void ReplaceOperand(SsaValue inst, SsaValue oldValue, SsaValue newValue)
    {
        if (inst.Arg0 == oldValue) inst.Arg0 = newValue;
        if (inst.Arg1 == oldValue) inst.Arg1 = newValue;
        if (inst.ExtraArgs != null)
        {
            for (int j = 0; j < inst.ExtraArgs.Count; j++)
            {
                if (inst.ExtraArgs[j] == oldValue)
                    inst.ExtraArgs[j] = newValue;
            }
        }
    }

    internal static void ReleaseOperands(SsaValue inst)
    {
        if (inst.Arg0 != null) { inst.Arg0.Uses--; inst.Arg0 = null; }
        if (inst.Arg1 != null) { inst.Arg1.Uses--; inst.Arg1 = null; }
    }

    internal static void DecrementUses(SsaValue inst)
    {
        if (inst.Arg0 != null) inst.Arg0.Uses--;
        if (inst.Arg1 != null) inst.Arg1.Uses--;
        if (inst.ExtraArgs != null)
            foreach (var arg in inst.ExtraArgs)
                arg.Uses--;
    }

    internal static void SetIntResult(SsaValue inst, int value)
    {
        inst.Op = SsaOp.ConstInt; inst.Type = ScriptType.Int;
        inst.Const.SetInt(value);
    }

    internal static void SetBoolResult(SsaValue inst, bool value)
    {
        inst.Op = SsaOp.ConstBool; inst.Type = ScriptType.Bool;
        inst.Const.SetBool(value);
    }

    internal static void SetDoubleResult(SsaValue inst, double value)
    {
        inst.Op = SsaOp.ConstDouble; inst.Type = ScriptType.Double;
        inst.Const.SetDouble(value);
    }
}