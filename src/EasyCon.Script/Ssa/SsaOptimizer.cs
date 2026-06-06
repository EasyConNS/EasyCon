using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace EasyCon.Script.Ssa;

/// <summary>
/// SSA IR 优化 pass 驱动器。
/// 对每个 SsaFunction 就地优化，迭代至不动点：
/// SCCP → 代数化简 → 拷贝传播 → 全局 CSE → 死代码消除 → CFG 简化 → 常量去重。
/// </summary>
static class SsaOptimizer
{
    public static void Optimize(SsaProgram program, SsaOptimizeTiming? optTiming = null)
    {
        var sw = Stopwatch.StartNew();

        // 先移除不可达函数
        SsaInterprocedural.RemoveUnreachableFunctions(program);
        if (optTiming != null) optTiming.RemoveUnreachable1 = sw.Elapsed;

        // 内联 trivial 函数（单基本块 + 仅 Return）
        sw.Restart();
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
        if (optTiming != null) optTiming.InlineTrivial = sw.Elapsed;

        // 内联后做函数内优化（函数间无共享可变状态，可并行）
        sw.Restart();
        if (program.MainFunction != null)
            OptimizeFunction(program.MainFunction);
        var funcList = program.Functions.Values.ToList();
        if (funcList.Count > 0)
            Parallel.ForEach(funcList, OptimizeFunction);
        if (optTiming != null) optTiming.IntraFunctionOpt = sw.Elapsed;

        // 内联 stdlib 包装函数（需要跨函数查表，放在函数内优化之后）
        sw.Restart();
        if (program.MainFunction != null)
            SsaInterprocedural.InlineIntrinsicWrappers(program, program.MainFunction);
        if (optTiming != null) optTiming.InlineIntrinsic = sw.Elapsed;

        // 内联后包装函数不再被引用，再次清理
        sw.Restart();
        SsaInterprocedural.RemoveUnreachableFunctions(program);
        if (optTiming != null) optTiming.RemoveUnreachable2 = sw.Elapsed;
    }

    // CFG 复杂度阈值：超过则跳过函数内优化
    private const int MaxBlocks = 50;
    private const int MaxEdges = 400;

    internal static void OptimizeFunction(SsaFunction func)
    {
        if (IsTooComplex(func))
            return;

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

    private static bool IsTooComplex(SsaFunction func)
    {
        int blocks = func.Blocks.Count;
        if (blocks > MaxBlocks)
            return true;

        int edges = 0;
        foreach (var block in func.Blocks)
        {
            if (block.TrueSuccessor != null) edges++;
            if (block.FalseSuccessor != null) edges++;
            if (block.JumpTarget != null && block.TrueSuccessor == null) edges++;
        }
        return edges > MaxEdges;
    }

    // ============ 共享工具方法 ============

    /// <summary>
    /// 批量延迟替换：将 replacements 字典中所有 oldValue → newValue 的引用
    /// 在 func 内统一应用。先解析传递链（A→B, B→C ⇒ A→C），再一遍扫描完成。
    /// 同步更新 Uses 计数。
    /// </summary>
    internal static void ApplyReplaceMap(SsaFunction func, Dictionary<SsaValue, SsaValue> replacements)
    {
        if (replacements.Count == 0) return;

        // 1. 解析传递链
        var keys = replacements.Keys.ToList();
        foreach (var key in keys)
        {
            var current = replacements[key];
            int depth = 0;
            while (replacements.TryGetValue(current, out var next) && depth++ < replacements.Count)
                current = next;
            if (current != replacements[key])
                replacements[key] = current;
        }

        // 2. 一遍扫描应用所有替换
        foreach (var block in func.Blocks)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
                ReplaceOperandsFromMap(block.Instructions[i], replacements);
            for (int i = 0; i < block.Phis.Count; i++)
                ReplaceOperandsFromMap(block.Phis[i], replacements);
            if (block.BranchCondition != null && replacements.TryGetValue(block.BranchCondition, out var newBranch))
                block.BranchCondition = newBranch;
        }

        // 3. 批量更新 Uses 计数
        foreach (var (old, @new) in replacements)
        {
            @new.Uses += old.Uses;
            old.Uses = 0;
        }
    }

    private static void ReplaceOperandsFromMap(SsaValue inst, Dictionary<SsaValue, SsaValue> map)
    {
        if (inst.Arg0 != null && map.TryGetValue(inst.Arg0, out var newArg0))
            inst.Arg0 = newArg0;
        if (inst.Arg1 != null && map.TryGetValue(inst.Arg1, out var newArg1))
            inst.Arg1 = newArg1;
        if (inst.ExtraArgs != null)
        {
            for (int j = 0; j < inst.ExtraArgs.Count; j++)
            {
                if (map.TryGetValue(inst.ExtraArgs[j], out var newArg))
                    inst.ExtraArgs[j] = newArg;
            }
        }
    }

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