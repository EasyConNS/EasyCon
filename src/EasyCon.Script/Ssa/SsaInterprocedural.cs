using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;
using System.Linq;

namespace EasyCon.Script.Ssa;

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

    // ============ Single-block 函数内联 ============

    /// <summary>
    /// 全局 ID 计数器，用于内联时分配新的 SsaValue ID，避免与 caller 的 ID 冲突。
    /// 在 InlineTrivialFunctions 入口处从程序最大 ID 初始化。
    /// </summary>
    private static int _globalInlineIdCounter;

    /// <summary>
    /// 内联单基本块函数：将 callee 的指令克隆（带全新 ID）到 caller 的调用点，
    /// 参数引用替换为实参，Return 替换为返回值。
    /// 支持任意单块叶子函数（算术、比较、类型转换等），不限于 LoadLocal+Return。
    /// </summary>
    internal static bool InlineTrivialFunctions(SsaProgram program, SsaFunction caller)
    {
        // 初始化 ID 计数器为程序最大 ID + 1
        if (_globalInlineIdCounter == 0)
        {
            int maxId = 0;
            foreach (var func in program.Functions.Values)
                foreach (var block in func.Blocks)
                    foreach (var val in block.Instructions.Concat(block.Phis))
                        if (val.Id > maxId) maxId = val.Id;
            _globalInlineIdCounter = maxId + 1;
        }

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

                if (!IsInlineableSingleBlockFunction(callee))
                    continue;

                // 收集调用参数
                var callArgs = new List<SsaValue>();
                if (inst.Arg0 != null) callArgs.Add(inst.Arg0);
                if (inst.ExtraArgs != null) callArgs.AddRange(inst.ExtraArgs);

                if (TryInlineSingleBlock(callee, caller, callArgs, inst, block, i))
                {
                    changed = true;
                    i--; // 内联后当前索引被删除，需要重新检查
                }
            }
        }
        return changed;
    }

    /// <summary>
    /// 判断是否可内联的单块叶子函数。
    /// 条件：单基本块 + 无副作用指令（除 Return）+ 有 Return + 无 Call（叶子函数）。
    /// </summary>
    private static bool IsInlineableSingleBlockFunction(SsaFunction func)
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
            if (inst.HasSideEffect)
                return false;
            if (inst.Op is SsaOp.Call or SsaOp.StaticCall)
                return false;
        }

        return hasReturn;
    }

    /// <summary>
    /// 尝试内联单块函数。
    /// 将 callee 的非参数、非 Return 指令克隆（新 ID）到 caller，
    /// 参数 LoadLocal 替换为实参，Return 的返回值替换 Call 的所有使用。
    /// </summary>
    private static bool TryInlineSingleBlock(
        SsaFunction callee,
        SsaFunction caller,
        List<SsaValue> callArgs,
        SsaValue callInst,
        SsaBlock callerBlock,
        int callIndex)
    {
        var calleeBlock = callee.Entry;

        // 构建参数映射：ParamSymbol → 实参
        var paramMap = new Dictionary<VariableSymbol, SsaValue>();
        foreach (var param in callee.Symbol.Parameters)
        {
            if (param.Ordinal < callArgs.Count)
                paramMap[param] = callArgs[param.Ordinal];
        }

        // 值映射：callee 的 SsaValue → caller 中对应的 SsaValue
        var valueMap = new Dictionary<SsaValue, SsaValue>();

        // 找到返回值和 Return 指令
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
            return false;

        // 第一遍：映射参数的 LoadLocal → 实参
        foreach (var inst in calleeBlock.Instructions)
        {
            if (inst.Op == SsaOp.LoadLocal && inst.Aux is VariableSymbol vs && paramMap.TryGetValue(vs, out var arg))
                valueMap[inst] = arg;
        }

        // 第二遍：克隆非参数、非 Return 指令到 caller block（新 ID 避免冲突）
        int insertIndex = callIndex;
        foreach (var inst in calleeBlock.Instructions)
        {
            if (inst.Op == SsaOp.Return)
                continue;
            if (valueMap.ContainsKey(inst))
                continue; // 参数 LoadLocal 已映射，跳过

            var cloned = CloneWithFreshId(inst, valueMap, callerBlock);
            valueMap[inst] = cloned;

            callerBlock.Instructions.Insert(insertIndex, cloned);
            insertIndex++;
            callIndex++; // Call 指令的位置后移
        }

        // 解析返回值
        var retValue = ResolveOperand(returnInst.Arg0, valueMap);
        if (retValue == null)
            return false;

        // 删除 Call 指令
        SsaOptimizer.DecrementUses(callInst);
        callerBlock.Instructions.RemoveAt(callIndex);

        // 替换所有使用
        SsaOptimizer.ReplaceAllUsesInFunction(caller, callInst, retValue);
        retValue.Uses += callInst.Uses;

        return true;
    }

    /// <summary>
    /// 克隆 SsaValue，分配全新 ID，替换操作数为 valueMap 中的映射值。
    /// </summary>
    private static SsaValue CloneWithFreshId(SsaValue original, Dictionary<SsaValue, SsaValue> valueMap, SsaBlock targetBlock)
    {
        int newId = Interlocked.Increment(ref _globalInlineIdCounter);
        var cloned = new SsaValue(newId, original.Op, original.Type)
        {
            Block = targetBlock,
            Slot = original.Slot,
            Const = original.Const,
            ConstString = original.ConstString,
            Aux = original.Aux,
        };

        cloned.Arg0 = ResolveOperand(original.Arg0, valueMap);
        cloned.Arg1 = ResolveOperand(original.Arg1, valueMap);

        if (original.ExtraArgs != null)
        {
            cloned.ExtraArgs = new List<SsaValue>();
            foreach (var arg in original.ExtraArgs)
            {
                var resolved = ResolveOperand(arg, valueMap);
                if (resolved != null) cloned.ExtraArgs.Add(resolved);
            }
        }

        // 更新 Uses
        if (cloned.Arg0 != null) cloned.Arg0.Uses++;
        if (cloned.Arg1 != null) cloned.Arg1.Uses++;
        if (cloned.ExtraArgs != null)
            foreach (var a in cloned.ExtraArgs) a.Uses++;

        return cloned;
    }

    private static SsaValue? ResolveOperand(SsaValue? operand, Dictionary<SsaValue, SsaValue> valueMap)
    {
        if (operand == null) return null;
        return valueMap.TryGetValue(operand, out var mapped) ? mapped : operand;
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

        // 就地替换 Call 为 intrinsic（保持 Arg0/Arg1/ExtraArgs 与 EmitIntrinsic 一致）
        callInst.Op = mapping.Op;
        callInst.Type = mapping.Type;
        // 有值参数时清除旧 Aux（FunctionSymbol）；无值参数时保留 callee 的 Aux（如 RuntimeValueNameSymbol）
        callInst.Aux = newArgs.Count > 0 ? null : intrinsicCall.Aux;
        callInst.Arg0 = newArgs.Count > 0 ? newArgs[0] : null;
        callInst.Arg1 = newArgs.Count > 1 ? newArgs[1] : null;
        callInst.ExtraArgs = newArgs.Count > 2 ? newArgs.GetRange(2, newArgs.Count - 2) : null;

        return true;
    }
}