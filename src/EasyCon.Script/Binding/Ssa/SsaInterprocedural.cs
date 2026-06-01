using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding.Ssa;

/// <summary>
/// 过程间 pass：不可达函数消除 + trivial 函数内联。
/// </summary>
static class SsaInterprocedural
{
    // ============ 不可达函数消除 ============

    /// <summary>
    /// 从 MainFunction 出发，沿 Call 指令收集所有可达函数，移除不可达的。
    /// </summary>
    internal static void RemoveUnreachableFunctions(SsaProgram program)
    {
        if (program.MainFunction == null) return;

        // 1. 收集所有可达函数
        var reachable = new HashSet<FunctionSymbol>();
        var worklist = new Stack<SsaFunction>();
        worklist.Push(program.MainFunction);

        while (worklist.Count > 0)
        {
            var func = worklist.Pop();
            if (!reachable.Add(func.Symbol)) continue;

            foreach (var block in func.Blocks)
            {
                CollectCallTargets(block.Instructions, program, reachable, worklist);
                CollectCallTargets(block.Phis, program, reachable, worklist);
            }
        }

        // 2. 如果全部可达则无需修改
        if (reachable.Count == program.Functions.Count) return;

        // 3. 收集可达函数用到的结构体类型
        var usedStructDefs = new HashSet<EcsStructDef>();
        foreach (var sym in reachable)
        {
            CollectUsedStructs(sym.ReturnType, usedStructDefs);
            foreach (var p in sym.Parameters)
                CollectUsedStructs(p.Type, usedStructDefs);
        }
        foreach (var func in program.Functions.Values.Where(f => reachable.Contains(f.Symbol)))
        {
            foreach (var block in func.Blocks)
            {
                foreach (var inst in block.Instructions.Concat(block.Phis))
                {
                    CollectUsedStructs(inst.Type, usedStructDefs);
                }
            }
        }
        // 主函数也要扫描
        foreach (var block in program.MainFunction.Blocks)
        {
            foreach (var inst in block.Instructions.Concat(block.Phis))
            {
                CollectUsedStructs(inst.Type, usedStructDefs);
            }
        }

        // 4. 构建新的 Functions 字典和 StructDefinitions
        var newFunctions = program.Functions
            .Where(kv => reachable.Contains(kv.Key))
            .ToImmutableDictionary(kv => kv.Key, kv => kv.Value);

        var newStructDefs = program.StructDefinitions
            .Where(kv => usedStructDefs.Contains(kv.Value))
            .ToImmutableDictionary(kv => kv.Key, kv => kv.Value);

        // 5. 就地替换（SsaProgram 的属性不是 readonly，可以重新赋值）
        var funcField = typeof(SsaProgram).GetProperty(nameof(SsaProgram.Functions));
        var structField = typeof(SsaProgram).GetProperty(nameof(SsaProgram.StructDefinitions));
        funcField!.SetValue(program, newFunctions);
        structField!.SetValue(program, newStructDefs);
    }

    private static void CollectCallTargets(
        List<SsaValue> values,
        SsaProgram program,
        HashSet<FunctionSymbol> reachable,
        Stack<SsaFunction> worklist)
    {
        foreach (var inst in values)
        {
            if (inst.Op is not (SsaOp.Call or SsaOp.StaticCall)) continue;
            if (inst.Aux is not FunctionSymbol fs) continue;
            if (reachable.Contains(fs)) continue;

            // 在 Functions 字典中查找该函数
            if (program.Functions.TryGetValue(fs, out var callee))
                worklist.Push(callee);
        }
    }

    private static void CollectUsedStructs(ScriptType type, HashSet<EcsStructDef> used)
    {
        if (type is StructType st)
            used.Add(st.Definition);
        else if (type is ArrayType at)
            CollectUsedStructs(at.ElementType, used);
    }

    // ============ Trivial 函数内联 ============

    /// <summary>
    /// 内联 trivial 函数：单基本块 + 仅 Return（可能有前置 LoadLocal）。
    /// 模式：[LoadLocal...] → Return
    /// </summary>
    internal static bool InlineTrivialFunctions(SsaProgram program, SsaFunction caller)
    {
        bool changed = false;

        foreach (var block in caller.Blocks)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];
                if (inst.Op != SsaOp.StaticCall && inst.Op != SsaOp.Call) continue;
                if (inst.Aux is not FunctionSymbol fs) continue;

                if (!program.Functions.TryGetValue(fs, out var callee))
                    continue;

                if (!IsTrivialFunction(callee))
                    continue;

                // 收集调用参数
                var callArgs = new List<SsaValue>();
                if (inst.Arg0 != null) callArgs.Add(inst.Arg0);
                if (inst.ExtraArgs != null) callArgs.AddRange(inst.ExtraArgs);

                if (TryInlineTrivial(callee, caller, callArgs, inst, block, i))
                {
                    changed = true;
                    i--; // 内联后当前索引被删除，需要重新检查
                }
            }
        }
        return changed;
    }

    /// <summary>
    /// 判断是否是 trivial 函数。
    /// 条件：单基本块、仅 LoadLocal + Return。
    /// </summary>
    private static bool IsTrivialFunction(SsaFunction func)
    {
        if (func.Blocks.Count != 1)
            return false;

        var block = func.Entry;
        bool hasReturn = false;

        foreach (var inst in block.Instructions)
        {
            if (inst.Op == SsaOp.Return)
            {
                hasReturn = true;
                continue;
            }
            if (inst.Op != SsaOp.LoadLocal && inst.Op != SsaOp.Nop)
                return false;
        }

        return hasReturn;
    }

    /// <summary>
    /// 尝试内联 trivial 函数。
    /// 成功时删除 Call 指令，将返回值替换到 Call 的使用处。
    /// </summary>
    private static bool TryInlineTrivial(
        SsaFunction callee,
        SsaFunction caller,
        List<SsaValue> callArgs,
        SsaValue callInst,
        SsaBlock callerBlock,
        int callIndex)
    {
        var calleeBlock = callee.Entry;

        // 构建参数映射：ParamSymbol → 实参
        var paramMap = new Dictionary<ParamSymbol, SsaValue>();
        foreach (var param in callee.Symbol.Parameters)
        {
            if (param.Ordinal < callArgs.Count)
                paramMap[param] = callArgs[param.Ordinal];
        }

        // 找到 Return 指令
        SsaValue? returnInst = null;
        foreach (var inst in calleeBlock.Instructions)
        {
            if (inst.Op == SsaOp.Return)
            {
                returnInst = inst;
                break;
            }
        }

        if (returnInst == null || returnInst.Arg0 == null)
            return false; // void 返回或无返回值

        // 解析返回值：可能是 LoadLocal，直接替换为对应的实参
        SsaValue? retValue = null;
        if (returnInst.Arg0.Op == SsaOp.LoadLocal && returnInst.Arg0.Aux is ParamSymbol ps)
        {
            if (paramMap.TryGetValue(ps, out var argValue))
                retValue = argValue;
        }

        if (retValue == null)
            return false; // 无法解析

        // 删除 Call 指令，替换所有使用
        SsaOptimizer.DecrementUses(callInst);
        callerBlock.Instructions.RemoveAt(callIndex);

        SsaOptimizer.ReplaceAllUsesInFunction(caller, callInst, retValue);
        retValue.Uses += callInst.Uses;

        return true;
    }

    // ============ stdlib 包装函数内联 ============

    /// <summary>
    /// 内联映射：函数名 → (intrinsic 操作, 额外常量参数)。
    /// 包装函数的参数会直接传递给 intrinsic，extra 固定追加在末尾。
    /// </summary>
    private static readonly Dictionary<string, (SsaOp Op, ScriptType Type, SsaValue?[] Extra)> IntrinsicMap = new()
    {
        ["TIME"] = (SsaOp.RuntimeValue, ScriptType.Int, []),
        ["FRAME"] = (SsaOp.Capture, ScriptType.String, []),  // 0 参数和 4 参数共用，通过参数数区分
        ["OCR"] = (SsaOp.Ocr, ScriptType.String, []), // 4 参数版本（"chi_sim" 由包装函数内部提供）
        ["ROI"] = (SsaOp.Roi, ScriptType.String, []),
    };

    /// <summary>
    /// 将 MainFunction 中对 stdlib 包装函数的调用内联为 intrinsic 操作。
    /// 只处理 body 为 "load params → optional const args → call intrinsic → return" 的薄包装。
    /// </summary>
    internal static bool InlineIntrinsicWrappers(SsaProgram program, SsaFunction caller)
    {
        // 建立函数名 → SsaFunction 的查找表
        var funcLookup = new Dictionary<string, List<SsaFunction>>();
        foreach (var (sym, func) in program.Functions)
        {
            if (!funcLookup.TryGetValue(sym.Name, out var list))
                funcLookup[sym.Name] = list = new();
            list.Add(func);
        }

        bool changed = false;
        foreach (var block in caller.Blocks)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];
                if (inst.Op != SsaOp.StaticCall) continue;
                if (inst.Aux is not FunctionSymbol fs) continue;

                // 收集调用参数
                var callArgs = new List<SsaValue>();
                if (inst.Arg0 != null) callArgs.Add(inst.Arg0);
                if (inst.ExtraArgs != null) callArgs.AddRange(inst.ExtraArgs);

                // 查找匹配的包装函数
                if (!funcLookup.TryGetValue(fs.Name, out var candidates)) continue;
                var callee = candidates.FirstOrDefault(c => c.Symbol.Parameters.Length == callArgs.Count);
                if (callee == null) continue;

                if (!TryInlineWrapper(callee, callArgs, inst, block))
                    continue;

                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// 尝试将单个 Call 指令替换为 intrinsic 操作。
    /// 成功时就地修改 inst 并返回 true。
    /// </summary>
    private static bool TryInlineWrapper(SsaFunction callee, List<SsaValue> callArgs, SsaValue callInst, SsaBlock block)
    {
        if (callee.Blocks.Count != 1) return false;
        var calleeBlock = callee.Entry;

        // 模式匹配：从 entry 块收集指令序列
        var allInsts = calleeBlock.Phis.Concat(calleeBlock.Instructions).ToList();

        // 找到 intrinsic call（最后一个非 Return 指令）
        SsaValue? intrinsicCall = null;
        for (int j = allInsts.Count - 1; j >= 0; j--)
        {
            if (allInsts[j].Op == SsaOp.Return) continue;
            intrinsicCall = allInsts[j];
            break;
        }
        if (intrinsicCall == null) return false;

        // 检查是否是已知 intrinsic
        if (!IntrinsicMap.TryGetValue(callee.Symbol.Name, out var mapping))
            return false;
        if (intrinsicCall.Op != mapping.Op) return false;

        // 收集 intrinsic 的参数（Arg0 + Arg1 + ExtraArgs），替换 param 引用，保留常量
        var intrinsicArgs = new List<SsaValue>();
        if (intrinsicCall.Arg0 != null) intrinsicArgs.Add(intrinsicCall.Arg0);
        if (intrinsicCall.Arg1 != null) intrinsicArgs.Add(intrinsicCall.Arg1);
        if (intrinsicCall.ExtraArgs != null) intrinsicArgs.AddRange(intrinsicCall.ExtraArgs);

        var newArgs = new List<SsaValue>();
        foreach (var arg in intrinsicArgs)
        {
            if (arg.Aux is ParamSymbol ps && ps.Ordinal < callArgs.Count)
            {
                // 参数引用 → 用调用者的实参替换
                newArgs.Add(callArgs[ps.Ordinal]);
            }
            else if (arg.IsConstant)
            {
                // 常量 → 直接使用（拷贝到调用者块中）
                arg.Block = block;
                newArgs.Add(arg);
            }
            else
            {
                return false; // 无法处理的模式
            }
        }

        if (newArgs.Count == 0) return false;

        // 就地替换 Call 为 intrinsic（保持 Arg0/Arg1/ExtraArgs 与 EmitIntrinsic 一致）
        callInst.Op = mapping.Op;
        callInst.Type = mapping.Type;
        callInst.Aux = null;
        callInst.Arg0 = newArgs[0];
        callInst.Arg1 = newArgs.Count > 1 ? newArgs[1] : null;
        callInst.ExtraArgs = newArgs.Count > 2 ? newArgs.GetRange(2, newArgs.Count - 2) : null;

        return true;
    }
}