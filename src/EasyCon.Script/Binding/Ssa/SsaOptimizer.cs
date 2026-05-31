using EasyCon.Script.Symbols;

namespace EasyCon.Script.Binding.Ssa;

/// <summary>
/// SSA IR 优化 pass 集合。
/// 对每个 SsaFunction 就地优化，迭代至不动点：
/// 代数化简 → 常量折叠 → 拷贝传播 → 全局 CSE → 死代码消除 → 不可达块删除。
/// </summary>
static class SsaOptimizer
{
    public static void Optimize(SsaProgram program)
    {
        if (program.MainFunction != null)
            OptimizeFunction(program.MainFunction);
        foreach (var func in program.Functions.Values)
            OptimizeFunction(func);
    }

    internal static void OptimizeFunction(SsaFunction func)
    {
        bool changed;
        int iterations = 0;
        const int MaxIterations = 5;
        do
        {
            changed = false;
            changed |= AlgebraicSimplify(func);
            changed |= FoldConstants(func);
            changed |= PropagateCopies(func);
            changed |= EliminateCommonSubexpressions(func);
            changed |= EliminateDeadCode(func);
            changed |= RemoveUnreachableBlocks(func);
        } while (changed && ++iterations < MaxIterations);
    }

    // ============ 代数化简 ============

    internal static bool AlgebraicSimplify(SsaFunction func)
    {
        var replacements = new List<(SsaValue inst, SsaValue replacement)>();
        bool changed = false;

        foreach (var block in func.Blocks)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];
                if (inst.Arg0 == null || inst.Arg1 == null)
                    continue;

                var arg0 = inst.Arg0;
                var arg1 = inst.Arg1;

                // 同值模式：x == x → true, x != x → false
                if (arg0.Id == arg1.Id && !arg0.IsConstant)
                {
                    bool? result = inst.Op switch
                    {
                        SsaOp.EqInt or SsaOp.EqUInt or SsaOp.EqDouble or SsaOp.EqUInt64
                            or SsaOp.EqBool or SsaOp.EqByte or SsaOp.EqPtr => true,
                        SsaOp.NeqInt or SsaOp.NeqUInt or SsaOp.NeqDouble or SsaOp.NeqUInt64
                            or SsaOp.NeqBool or SsaOp.NeqByte or SsaOp.NeqPtr => false,
                        _ => null
                    };
                    if (result.HasValue)
                    {
                        SetBoolResult(inst, result.Value);
                        ReleaseOperands(inst);
                        changed = true;
                        continue;
                    }
                }

                // 确定哪个操作数是常量
                SsaValue? constArg = null;
                SsaValue? otherArg = null;
                if (arg0.IsConstant && !arg1.IsConstant)
                {
                    constArg = arg0;
                    otherArg = arg1;
                }
                else if (!arg0.IsConstant && arg1.IsConstant)
                {
                    constArg = arg1;
                    otherArg = arg0;
                }

                if (constArg == null)
                    continue;

                // 获取常量的整数值（用于整数运算的 0/1 检测）
                long constIntValue = 0;
                bool constIsInt = false;
                if (constArg.Op is SsaOp.ConstInt or SsaOp.ConstUInt or SsaOp.ConstByte
                    or SsaOp.ConstBool)
                {
                    constIntValue = constArg.Const.GetInt();
                    constIsInt = true;
                }

                double constDoubleValue = 0;
                bool constIsDouble = false;
                if (constArg.Op == SsaOp.ConstDouble)
                {
                    constDoubleValue = constArg.Const.GetDouble();
                    constIsDouble = true;
                }

                bool isLeftConst = (constArg == arg0);

                // x + 0 / 0 + x → x
                if ((constIsInt && constIntValue == 0) &&
                    inst.Op is SsaOp.AddInt or SsaOp.AddUInt or SsaOp.AddUInt64)
                {
                    replacements.Add((inst, otherArg!));
                    continue;
                }
                if ((constIsDouble && constDoubleValue == 0.0) &&
                    inst.Op == SsaOp.AddDouble)
                {
                    replacements.Add((inst, otherArg!));
                    continue;
                }

                // x - 0 → x
                if ((constIsInt && constIntValue == 0) && isLeftConst == false &&
                    inst.Op is SsaOp.SubInt or SsaOp.SubUInt or SsaOp.SubUInt64)
                {
                    replacements.Add((inst, otherArg!));
                    continue;
                }
                if ((constIsDouble && constDoubleValue == 0.0) && isLeftConst == false &&
                    inst.Op == SsaOp.SubDouble)
                {
                    replacements.Add((inst, otherArg!));
                    continue;
                }

                // x * 1 / 1 * x → x
                if ((constIsInt && constIntValue == 1) &&
                    inst.Op is SsaOp.MulInt or SsaOp.MulUInt or SsaOp.MulUInt64)
                {
                    replacements.Add((inst, otherArg!));
                    continue;
                }
                if ((constIsDouble && constDoubleValue == 1.0) &&
                    inst.Op == SsaOp.MulDouble)
                {
                    replacements.Add((inst, otherArg!));
                    continue;
                }

                // x * 0 / 0 * x → ConstInt/ConstDouble 0
                if ((constIsInt && constIntValue == 0) &&
                    inst.Op is SsaOp.MulInt or SsaOp.MulUInt or SsaOp.MulUInt64)
                {
                    SetIntResult(inst, 0);
                    ReleaseOperands(inst);
                    changed = true;
                    continue;
                }
                if ((constIsDouble && constDoubleValue == 0.0) &&
                    inst.Op == SsaOp.MulDouble)
                {
                    SetDoubleResult(inst, 0.0);
                    ReleaseOperands(inst);
                    changed = true;
                    continue;
                }

                // x / 1 → x
                if ((constIsInt && constIntValue == 1) && isLeftConst == false &&
                    inst.Op is SsaOp.DivInt or SsaOp.DivUInt or SsaOp.DivUInt64)
                {
                    replacements.Add((inst, otherArg!));
                    continue;
                }
                if ((constIsDouble && constDoubleValue == 1.0) && isLeftConst == false &&
                    inst.Op == SsaOp.DivDouble)
                {
                    replacements.Add((inst, otherArg!));
                    continue;
                }

                // x & 0 / 0 & x → ConstInt 0
                if ((constIsInt && constIntValue == 0) &&
                    inst.Op == SsaOp.AndInt)
                {
                    SetIntResult(inst, 0);
                    ReleaseOperands(inst);
                    changed = true;
                    continue;
                }

                // x | 0 / 0 | x → x
                if ((constIsInt && constIntValue == 0) &&
                    inst.Op == SsaOp.OrInt)
                {
                    replacements.Add((inst, otherArg!));
                    continue;
                }

                // x ^ 0 / 0 ^ x → x
                if ((constIsInt && constIntValue == 0) &&
                    inst.Op == SsaOp.XorInt)
                {
                    replacements.Add((inst, otherArg!));
                    continue;
                }
            }
        }

        // 应用替换：将所有对 inst 的使用替换为 replacement
        foreach (var (inst, replacement) in replacements)
        {
            ReplaceAllUsesInFunction(func, inst, replacement);
            // 释放 inst 的操作数引用
            ReleaseOperands(inst);
            inst.Uses = 0;
            changed = true;
        }

        return changed;
    }

    // ============ 常量折叠 ============

    internal static bool FoldConstants(SsaFunction func)
    {
        bool changed = false;
        foreach (var block in func.Blocks)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];
                var oldOp = inst.Op;
                FoldInPlace(inst);
                if (inst.Op != oldOp)
                    changed = true;
            }
        }
        return changed;
    }

    private static void FoldInPlace(SsaValue inst)
    {
        if (inst.Arg0 != null && inst.Arg1 != null &&
            inst.Arg0.IsConstant && inst.Arg1.IsConstant)
        {
            if (FoldBinaryInPlace(inst))
                ReleaseOperands(inst);
            return;
        }

        if (inst.Arg0 != null && inst.Arg1 == null && inst.Arg0.IsConstant)
        {
            if (FoldUnaryInPlace(inst))
                ReleaseOperands(inst);
        }
    }

    private static void ReleaseOperands(SsaValue inst)
    {
        if (inst.Arg0 != null) { inst.Arg0.Uses--; inst.Arg0 = null; }
        if (inst.Arg1 != null) { inst.Arg1.Uses--; inst.Arg1 = null; }
    }

    private static bool FoldBinaryInPlace(SsaValue inst)
    {
        var left = inst.Arg0!;
        var right = inst.Arg1!;

        switch (inst.Op)
        {
            case SsaOp.AddInt: SetIntResult(inst, left.Const.GetInt() + right.Const.GetInt()); return true;
            case SsaOp.SubInt: SetIntResult(inst, left.Const.GetInt() - right.Const.GetInt()); return true;
            case SsaOp.MulInt: SetIntResult(inst, left.Const.GetInt() * right.Const.GetInt()); return true;
            case SsaOp.DivInt:
                if (right.Const.GetInt() == 0) return false;
                SetIntResult(inst, left.Const.GetInt() / right.Const.GetInt()); return true;
            case SsaOp.ModInt:
                if (right.Const.GetInt() == 0) return false;
                SetIntResult(inst, left.Const.GetInt() % right.Const.GetInt()); return true;
            case SsaOp.RoundDivInt:
                if (right.Const.GetInt() == 0) return false;
                SetIntResult(inst, (int)Math.Round((double)left.Const.GetInt() / right.Const.GetInt())); return true;

            case SsaOp.EqInt: SetBoolResult(inst, left.Const.GetInt() == right.Const.GetInt()); return true;
            case SsaOp.NeqInt: SetBoolResult(inst, left.Const.GetInt() != right.Const.GetInt()); return true;
            case SsaOp.LtInt: SetBoolResult(inst, left.Const.GetInt() < right.Const.GetInt()); return true;
            case SsaOp.LeqInt: SetBoolResult(inst, left.Const.GetInt() <= right.Const.GetInt()); return true;
            case SsaOp.GtInt: SetBoolResult(inst, left.Const.GetInt() > right.Const.GetInt()); return true;
            case SsaOp.GeqInt: SetBoolResult(inst, left.Const.GetInt() >= right.Const.GetInt()); return true;

            case SsaOp.AndInt: SetIntResult(inst, left.Const.GetInt() & right.Const.GetInt()); return true;
            case SsaOp.OrInt: SetIntResult(inst, left.Const.GetInt() | right.Const.GetInt()); return true;
            case SsaOp.XorInt: SetIntResult(inst, left.Const.GetInt() ^ right.Const.GetInt()); return true;
            case SsaOp.ShlInt: SetIntResult(inst, left.Const.GetInt() << right.Const.GetInt()); return true;
            case SsaOp.ShrInt: SetIntResult(inst, left.Const.GetInt() >> right.Const.GetInt()); return true;

            case SsaOp.AddDouble: SetDoubleResult(inst, left.Const.GetDouble() + right.Const.GetDouble()); return true;
            case SsaOp.SubDouble: SetDoubleResult(inst, left.Const.GetDouble() - right.Const.GetDouble()); return true;
            case SsaOp.MulDouble: SetDoubleResult(inst, left.Const.GetDouble() * right.Const.GetDouble()); return true;
            case SsaOp.DivDouble: SetDoubleResult(inst, left.Const.GetDouble() / right.Const.GetDouble()); return true;

            case SsaOp.EqDouble: SetBoolResult(inst, left.Const.GetDouble() == right.Const.GetDouble()); return true;
            case SsaOp.NeqDouble: SetBoolResult(inst, left.Const.GetDouble() != right.Const.GetDouble()); return true;
            case SsaOp.LtDouble: SetBoolResult(inst, left.Const.GetDouble() < right.Const.GetDouble()); return true;
            case SsaOp.LeqDouble: SetBoolResult(inst, left.Const.GetDouble() <= right.Const.GetDouble()); return true;
            case SsaOp.GtDouble: SetBoolResult(inst, left.Const.GetDouble() > right.Const.GetDouble()); return true;
            case SsaOp.GeqDouble: SetBoolResult(inst, left.Const.GetDouble() >= right.Const.GetDouble()); return true;

            case SsaOp.EqBool: SetBoolResult(inst, left.Const.GetBool() == right.Const.GetBool()); return true;
            case SsaOp.NeqBool: SetBoolResult(inst, left.Const.GetBool() != right.Const.GetBool()); return true;

            case SsaOp.EqByte: SetBoolResult(inst, left.Const.GetInt() == right.Const.GetInt()); return true;
            case SsaOp.NeqByte: SetBoolResult(inst, left.Const.GetInt() != right.Const.GetInt()); return true;

            default: return false;
        }
    }

    private static bool FoldUnaryInPlace(SsaValue inst)
    {
        var operand = inst.Arg0!;

        switch (inst.Op)
        {
            case SsaOp.LogicNot: SetBoolResult(inst, !operand.Const.GetBool()); return true;
            case SsaOp.NotInt: SetIntResult(inst, ~operand.Const.GetInt()); return true;
            case SsaOp.ConvBoolToInt: SetIntResult(inst, operand.Const.GetBool() ? 1 : 0); return true;
            case SsaOp.ConvByteToInt: SetIntResult(inst, operand.Const.GetInt()); return true;
            case SsaOp.ConvIntToDouble: SetDoubleResult(inst, operand.Const.GetInt()); return true;
            case SsaOp.ConvDoubleToInt: SetIntResult(inst, (int)operand.Const.GetDouble()); return true;
            case SsaOp.ConvIntToByte: SetIntResult(inst, (byte)operand.Const.GetInt()); return true;
            case SsaOp.ConvToString:
                inst.Op = SsaOp.ConstString; inst.Type = ScriptType.String;
                inst.ConstString = operand.Const.GetInt().ToString();
                return true;
            case SsaOp.ConvToInt:
                SetIntResult(inst, operand.Const.GetInt());
                return true;
            default: return false;
        }
    }

    private static void SetIntResult(SsaValue inst, int value)
    {
        inst.Op = SsaOp.ConstInt; inst.Type = ScriptType.Int;
        inst.Const.SetInt(value);
    }

    private static void SetBoolResult(SsaValue inst, bool value)
    {
        inst.Op = SsaOp.ConstBool; inst.Type = ScriptType.Bool;
        inst.Const.SetBool(value);
    }

    private static void SetDoubleResult(SsaValue inst, double value)
    {
        inst.Op = SsaOp.ConstDouble; inst.Type = ScriptType.Double;
        inst.Const.SetDouble(value);
    }

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
                    ReplaceAllUsesInFunction(func, inst, knownValue);
                    inst.Uses = 0;
                    block.Instructions.RemoveAt(i);
                    i--;
                    changed = true;
                }
            }
            else if (inst.Op == SsaOp.Call)
            {
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
                    ReplaceAllUsesInBlock(block, inst, existing);
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
        var visited = new HashSet<SsaBlock>();
        int index = 0;
        DfsRpo(func.Entry, visited, ref index, func);
    }

    private static void DfsRpo(SsaBlock block, HashSet<SsaBlock> visited, ref int index, SsaFunction func)
    {
        if (!visited.Add(block)) return;
        foreach (var succ in block.GetSuccessors())
            DfsRpo(succ, visited, ref index, func);
        block.RpoIndex = index++;
    }

    readonly record struct CseKey(SsaOp Op, int Arg0Id, int Arg1Id, int ExtraHash, int AuxHash = 0)
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

    // ============ 死代码消除 ============

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
                DecrementUses(inst);
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
        if (inst.IsConstant) return false;
        // LoadLocal/LoadGlobal 不可删除（_lastValue 语义依赖）
        if (inst.Op is SsaOp.LoadLocal or SsaOp.LoadGlobal) return false;
        return true;
    }

    private static void DecrementUses(SsaValue inst)
    {
        if (inst.Arg0 != null) inst.Arg0.Uses--;
        if (inst.Arg1 != null) inst.Arg1.Uses--;
        if (inst.ExtraArgs != null)
            foreach (var arg in inst.ExtraArgs)
                arg.Uses--;
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
                    DecrementUses(inst);
                foreach (var phi in func.Blocks[i].Phis)
                    DecrementUses(phi);
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

    // ============ 值替换工具 ============

    /// <summary>
    /// 将 func 内所有对 oldValue 的引用替换为 newValue（指令 + phi + 终止条件）。
    /// 同时更新 Uses 计数。
    /// </summary>
    private static void ReplaceAllUsesInFunction(SsaFunction func, SsaValue oldValue, SsaValue newValue)
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
    private static void ReplaceAllUsesInBlock(SsaBlock block, SsaValue oldValue, SsaValue newValue)
    {
        foreach (var inst in block.Instructions)
            ReplaceOperand(inst, oldValue, newValue);
        foreach (var phi in block.Phis)
            ReplaceOperand(phi, oldValue, newValue);
        if (block.BranchCondition == oldValue)
            block.BranchCondition = newValue;
    }

    private static void ReplaceOperand(SsaValue inst, SsaValue oldValue, SsaValue newValue)
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
}