using EasyCon.Script.Binding;
using EasyCon.Script.Symbols;
using System.Diagnostics;

namespace EasyCon.Script.Ssa;

/// <summary>
/// 稀疏条件常量传播（SCCP）：Wegman-Zadeck 算法。
/// 分两阶段：Analyze 产出 lattice map，Rewrite 消费它改写 IR。
/// </summary>
static class SsaConstantPropagation
{
    internal static bool Run(SsaFunction func)
    {
        var lattice = new Dictionary<SsaValue, LatticeValue>();
        var reachableBlocks = new HashSet<SsaBlock>();
        var executableEdges = new HashSet<(SsaBlock from, SsaBlock to)>();

        Analyze(func, lattice, reachableBlocks, executableEdges);
        return Rewrite(func, lattice);
    }

    // ============================================================
    // 阶段 1：分析
    // ============================================================

    private static void Analyze(
        SsaFunction func,
        Dictionary<SsaValue, LatticeValue> lattice,
        HashSet<SsaBlock> reachableBlocks,
        HashSet<(SsaBlock from, SsaBlock to)> executableEdges)
    {
        // 预构建 def-use map：value → 使用它的指令列表
        // 以及 BranchCondition 反查表：value → 使用它作为分支条件的 block 列表
        var useMap = BuildUseMap(func, out var branchCondMap);

        // 初始化所有值为 Top
        foreach (var block in func.Blocks)
        {
            foreach (var phi in block.Phis)
                lattice[phi] = LatticeValue.Top();
            foreach (var inst in block.Instructions)
                lattice[inst] = LatticeValue.Top();
            // BranchCondition 也需要 lattice 条目
            if (block.BranchCondition != null && !lattice.ContainsKey(block.BranchCondition))
                lattice[block.BranchCondition] = LatticeValue.Top();
        }

        // 入口块的 BranchCondition 可能来自其他块，确保也初始化
        foreach (var block in func.Blocks)
        {
            if (block.BranchCondition != null && !lattice.ContainsKey(block.BranchCondition))
                lattice[block.BranchCondition] = LatticeValue.Top();
        }

        var blockWorklist = new Queue<SsaBlock>();
        var ssaWorklist = new Queue<SsaValue>();

        // 入口块直接入队
        blockWorklist.Enqueue(func.Entry);
        reachableBlocks.Add(func.Entry);

        while (blockWorklist.Count > 0 || ssaWorklist.Count > 0)
        {
            // 优先处理块 worklist
            while (blockWorklist.Count > 0)
            {
                var block = blockWorklist.Dequeue();
                ProcessBlock(block, lattice, reachableBlocks, executableEdges, blockWorklist, ssaWorklist, useMap);
            }

            // 处理 SSA worklist
            while (ssaWorklist.Count > 0)
            {
                var val = ssaWorklist.Dequeue();
                ProcessSsaEdge(val, func, lattice, reachableBlocks, executableEdges, blockWorklist, ssaWorklist, useMap, branchCondMap);
            }
        }
    }

    private static void ProcessBlock(
        SsaBlock block,
        Dictionary<SsaValue, LatticeValue> lattice,
        HashSet<SsaBlock> reachableBlocks,
        HashSet<(SsaBlock from, SsaBlock to)> executableEdges,
        Queue<SsaBlock> blockWorklist,
        Queue<SsaValue> ssaWorklist,
        Dictionary<SsaValue, List<SsaValue>> useMap)
    {
        // 1. 处理 Phi 节点
        foreach (var phi in block.Phis)
            EvaluatePhi(phi, block, lattice, reachableBlocks, executableEdges, ssaWorklist, useMap);

        // 2. 处理普通指令
        foreach (var inst in block.Instructions)
            EvaluateInstruction(inst, lattice, ssaWorklist, useMap);

        // 3. 处理终结指令（分支条件）
        ProcessTerminator(block, lattice, reachableBlocks, executableEdges, blockWorklist);
    }

    private static void EvaluatePhi(
        SsaValue phi,
        SsaBlock block,
        Dictionary<SsaValue, LatticeValue> lattice,
        HashSet<SsaBlock> reachableBlocks,
        HashSet<(SsaBlock from, SsaBlock to)> executableEdges,
        Queue<SsaValue> ssaWorklist,
        Dictionary<SsaValue, List<SsaValue>> useMap)
    {
        // meet 所有可执行边上的入参
        var result = LatticeValue.Top();
        bool hasIncoming = false;

        if (phi.ExtraArgs != null)
        {
            for (int i = 0; i < phi.ExtraArgs.Count && i < block.Predecessors.Count; i++)
            {
                var pred = block.Predecessors[i];
                // 只考虑已执行边
                if (!executableEdges.Contains((pred, block)))
                    continue;

                hasIncoming = true;
                var incoming = GetLatticeOrBottom(phi.ExtraArgs[i], lattice);
                LatticeValue.Meet(ref result, incoming);
            }
        }

        // 无可执行边 → 保持 Top（块不可达时不该到这里，但安全起见）
        if (!hasIncoming)
            return;

        UpdateLattice(phi, result, lattice, ssaWorklist, useMap);
    }

    private static void EvaluateInstruction(
        SsaValue inst,
        Dictionary<SsaValue, LatticeValue> lattice,
        Queue<SsaValue> ssaWorklist,
        Dictionary<SsaValue, List<SsaValue>> useMap)
    {
        // 跳过有副作用的指令（不传播值，但操作数可能传播）
        // 常量载荷已经是 Const，不需要 evaluate
        if (inst.IsConstant)
        {
            if (lattice[inst].Tag == LatticeTag.Top)
            {
                UpdateLattice(inst, LatticeValue.FromConstant(inst), lattice, ssaWorklist, useMap);
            }
            return;
        }

        // 加载指令、调用、领域操作等无法通过 evaluate 确定结果
        if (inst.Op is SsaOp.LoadLocal or SsaOp.LoadGlobal
            or SsaOp.Call or SsaOp.StaticCall
            or SsaOp.KeyPress or SsaOp.KeyAction
            or SsaOp.StickAction or SsaOp.StickPress or SsaOp.Wait
            or SsaOp.StoreLocal or SsaOp.StoreGlobal
            or SsaOp.StoreField or SsaOp.StoreIndex or SsaOp.StoreFieldIndex
            or SsaOp.Return or SsaOp.Nop
            or SsaOp.Capture or SsaOp.Ocr or SsaOp.Roi
            or SsaOp.Rand
            or SsaOp.RuntimeValue or SsaOp.ImageLabel
            or SsaOp.LoadIndex or SsaOp.LoadField or SsaOp.LoadFieldIndex
            or SsaOp.Slice or SsaOp.Contains
            or SsaOp.DeepCopy or SsaOp.StructInit)
        {
            // 这些指令结果为 Bottom（非常量）
            UpdateLattice(inst, LatticeValue.Bottom(), lattice, ssaWorklist, useMap);
            return;
        }

        // 数组操作：长度传播（KnownArrayLength 侧信道）
        if (inst.Op == SsaOp.ArrayInit)
        { EvaluateArrayInit(inst, lattice, ssaWorklist, useMap); return; }
        if (inst.Op == SsaOp.ArrayAppend)
        { EvaluateArrayAppend(inst, lattice, ssaWorklist, useMap); return; }
        if (inst.Op == SsaOp.ArrayLen)
        { EvaluateArrayLen(inst, lattice, ssaWorklist, useMap); return; }
        if (inst.Op == SsaOp.Concat)
        { EvaluateConcat(inst, lattice, ssaWorklist, useMap); return; }

        // Phi 不在这里处理
        if (inst.Op == SsaOp.Phi)
            return;

        // 二元操作
        if (inst.Arg0 != null && inst.Arg1 != null)
        {
            var left = GetLatticeOrBottom(inst.Arg0, lattice);
            var right = GetLatticeOrBottom(inst.Arg1, lattice);
            var result = LatticeValue.EvaluateBinary(inst.Op, left, right);
            UpdateLattice(inst, result, lattice, ssaWorklist, useMap);
            return;
        }

        // 一元操作
        if (inst.Arg0 != null && inst.Arg1 == null)
        {
            var operand = GetLatticeOrBottom(inst.Arg0, lattice);
            var result = LatticeValue.EvaluateUnary(inst.Op, operand);
            UpdateLattice(inst, result, lattice, ssaWorklist, useMap);
            return;
        }

        // 无操作数指令（无 Arg0）→ Bottom
        UpdateLattice(inst, LatticeValue.Bottom(), lattice, ssaWorklist, useMap);
    }

    // ---- 数组长度传播 ----

    private static void EvaluateArrayInit(
        SsaValue inst,
        Dictionary<SsaValue, LatticeValue> lattice,
        Queue<SsaValue> ssaWorklist,
        Dictionary<SsaValue, List<SsaValue>> useMap)
    {
        // 元素数始终静态已知
        int elementCount = (inst.Arg0 != null ? 1 : 0) + (inst.ExtraArgs?.Count ?? 0);
        var result = LatticeValue.Bottom();
        result.KnownArrayLength = elementCount;
        UpdateLattice(inst, result, lattice, ssaWorklist, useMap);
    }

    private static void EvaluateArrayAppend(
        SsaValue inst,
        Dictionary<SsaValue, LatticeValue> lattice,
        Queue<SsaValue> ssaWorklist,
        Dictionary<SsaValue, List<SsaValue>> useMap)
    {
        var sourceLattice = GetLatticeOrBottom(inst.Arg0!, lattice);
        var result = LatticeValue.Bottom();
        if (sourceLattice.KnownArrayLength >= 0)
            result.KnownArrayLength = sourceLattice.KnownArrayLength + 1;
        UpdateLattice(inst, result, lattice, ssaWorklist, useMap);
    }

    private static void EvaluateArrayLen(
        SsaValue inst,
        Dictionary<SsaValue, LatticeValue> lattice,
        Queue<SsaValue> ssaWorklist,
        Dictionary<SsaValue, List<SsaValue>> useMap)
    {
        var containerLattice = GetLatticeOrBottom(inst.Arg0!, lattice);
        if (containerLattice.KnownArrayLength >= 0)
        {
            // 数组长度编译期已知 → 常量 int
            UpdateLattice(inst, LatticeValue.ConstInt(containerLattice.KnownArrayLength), lattice, ssaWorklist, useMap);
            return;
        }
        UpdateLattice(inst, LatticeValue.Bottom(), lattice, ssaWorklist, useMap);
    }

    private static void EvaluateConcat(
        SsaValue inst,
        Dictionary<SsaValue, LatticeValue> lattice,
        Queue<SsaValue> ssaWorklist,
        Dictionary<SsaValue, List<SsaValue>> useMap)
    {
        var leftLattice = GetLatticeOrBottom(inst.Arg0!, lattice);
        var rightLattice = GetLatticeOrBottom(inst.Arg1!, lattice);
        var result = LatticeValue.Bottom();
        if (leftLattice.KnownArrayLength >= 0 && rightLattice.KnownArrayLength >= 0)
            result.KnownArrayLength = leftLattice.KnownArrayLength + rightLattice.KnownArrayLength;
        UpdateLattice(inst, result, lattice, ssaWorklist, useMap);
    }

    private static void ProcessTerminator(
        SsaBlock block,
        Dictionary<SsaValue, LatticeValue> lattice,
        HashSet<SsaBlock> reachableBlocks,
        HashSet<(SsaBlock from, SsaBlock to)> executableEdges,
        Queue<SsaBlock> blockWorklist)
    {
        if (block.BranchCondition != null)
        {
            var condLattice = GetLatticeOrBottom(block.BranchCondition, lattice);

            if (condLattice.Tag == LatticeTag.Const && condLattice.ConstKind == SsaOp.ConstBool)
            {
                // 条件已知为常量 → 只执行一条边
                bool condVal = condLattice.Value.GetBool();
                var target = condVal ? block.TrueSuccessor! : block.FalseSuccessor!;
                MarkEdge(block, target, reachableBlocks, executableEdges, blockWorklist);
            }
            else if (condLattice.Tag == LatticeTag.Bottom)
            {
                // 条件非常量 → 两条边都执行
                MarkEdge(block, block.TrueSuccessor!, reachableBlocks, executableEdges, blockWorklist);
                MarkEdge(block, block.FalseSuccessor!, reachableBlocks, executableEdges, blockWorklist);
            }
            // Top → 不标记任何边（等条件确定后再标记）
        }
        else if (block.JumpTarget != null)
        {
            MarkEdge(block, block.JumpTarget, reachableBlocks, executableEdges, blockWorklist);
        }
        // IsReturn → 无后继
    }

    private static void MarkEdge(
        SsaBlock from, SsaBlock to,
        HashSet<SsaBlock> reachableBlocks,
        HashSet<(SsaBlock from, SsaBlock to)> executableEdges,
        Queue<SsaBlock> blockWorklist)
    {
        if (executableEdges.Add((from, to)))
        {
            // 新边 → 目标块若首次可达则入队
            if (reachableBlocks.Add(to))
                blockWorklist.Enqueue(to);
            else
            {
                // 块已可达但有新边 → 需要重新处理 Phi（入队块）
                blockWorklist.Enqueue(to);
            }
        }
    }

    private static void ProcessSsaEdge(
        SsaValue changedVal,
        SsaFunction func,
        Dictionary<SsaValue, LatticeValue> lattice,
        HashSet<SsaBlock> reachableBlocks,
        HashSet<(SsaBlock from, SsaBlock to)> executableEdges,
        Queue<SsaBlock> blockWorklist,
        Queue<SsaValue> ssaWorklist,
        Dictionary<SsaValue, List<SsaValue>> useMap,
        Dictionary<SsaValue, List<SsaBlock>> branchCondMap)
    {
        if (!useMap.TryGetValue(changedVal, out var users))
            return;

        foreach (var user in users)
        {
            // 只处理可达块中的指令
            if (!reachableBlocks.Contains(user.Block))
                continue;

            if (user.Op == SsaOp.Phi)
            {
                // Phi 需要重新 meet
                EvaluatePhi(user, user.Block, lattice, reachableBlocks, executableEdges, ssaWorklist, useMap);
            }
            else
            {
                EvaluateInstruction(user, lattice, ssaWorklist, useMap);
            }
        }

        // 如果 changedVal 是某个块的 BranchCondition，需要重新处理终结指令
        // 使用反查表 O(1) 而非遍历全部 block O(N)
        if (branchCondMap.TryGetValue(changedVal, out var condBlocks))
        {
            foreach (var block in condBlocks)
            {
                if (reachableBlocks.Contains(block))
                    ProcessTerminator(block, lattice, reachableBlocks, executableEdges, blockWorklist);
            }
        }
    }

    private static void UpdateLattice(
        SsaValue val,
        LatticeValue newValue,
        Dictionary<SsaValue, LatticeValue> lattice,
        Queue<SsaValue> ssaWorklist,
        Dictionary<SsaValue, List<SsaValue>> useMap)
    {
        if (!lattice.TryGetValue(val, out var current))
            current = LatticeValue.Top();

        // 数组长度侧信道：Meet 不感知 KnownArrayLength，需单独传播
        bool lengthChanged = false;
        if (newValue.KnownArrayLength >= 0 && current.KnownArrayLength != newValue.KnownArrayLength)
        {
            if (current.KnownArrayLength < 0) // 未知 → 已知
            {
                current.KnownArrayLength = newValue.KnownArrayLength;
                lengthChanged = true;
            }
        }

        if (LatticeValue.Meet(ref current, newValue) || lengthChanged)
        {
            // Meet 可能创建新的 Bottom()（KnownArrayLength=-1），回填侧信道
            if (current.KnownArrayLength < 0 && newValue.KnownArrayLength >= 0)
                current.KnownArrayLength = newValue.KnownArrayLength;
            lattice[val] = current;
            // 值发生了变化，通知所有使用者
            ssaWorklist.Enqueue(val);
        }
    }

    private static LatticeValue GetLatticeOrBottom(SsaValue val, Dictionary<SsaValue, LatticeValue> lattice)
    {
        return lattice.TryGetValue(val, out var lv) ? lv : LatticeValue.Bottom();
    }

    /// <summary>
    /// 构建 def-use map：value → 直接使用该 value 的所有指令列表。
    /// </summary>
    private static Dictionary<SsaValue, List<SsaValue>> BuildUseMap(SsaFunction func, out Dictionary<SsaValue, List<SsaBlock>> branchCondMap)
    {
        var useMap = new Dictionary<SsaValue, List<SsaValue>>();
        branchCondMap = new Dictionary<SsaValue, List<SsaBlock>>();

        foreach (var block in func.Blocks)
        {
            // Phi 节点
            foreach (var phi in block.Phis)
            {
                if (phi.ExtraArgs != null)
                {
                    foreach (var arg in phi.ExtraArgs)
                        AddUse(useMap, arg, phi);
                }
            }

            // 普通指令
            foreach (var inst in block.Instructions)
            {
                if (inst.Arg0 != null) AddUse(useMap, inst.Arg0, inst);
                if (inst.Arg1 != null) AddUse(useMap, inst.Arg1, inst);
                if (inst.ExtraArgs != null)
                {
                    foreach (var arg in inst.ExtraArgs)
                        AddUse(useMap, arg, inst);
                }
            }

            // BranchCondition 反查表：value → 使用它作为分支条件的 block
            if (block.BranchCondition != null)
            {
                if (!branchCondMap.TryGetValue(block.BranchCondition, out var list))
                {
                    list = new List<SsaBlock>();
                    branchCondMap[block.BranchCondition] = list;
                }
                list.Add(block);
            }
        }

        return useMap;
    }

    private static void AddUse(Dictionary<SsaValue, List<SsaValue>> useMap, SsaValue def, SsaValue user)
    {
        if (!useMap.TryGetValue(def, out var list))
        {
            list = new List<SsaValue>();
            useMap[def] = list;
        }
        list.Add(user);
    }

    // ============================================================
    // 阶段 2：改写
    // ============================================================

    private static bool Rewrite(
        SsaFunction func,
        Dictionary<SsaValue, LatticeValue> lattice)
    {
        bool changed = false;

        // 1. 将 lattice 中为 Const 的非常量指令改写为常量
        changed |= RewriteConstants(func, lattice);

        // 2. 常量分支折叠
        changed |= FoldConstantBranches(func, lattice);

        // 3. 简化 Phi（平凡 phi：忽略自引用臂后剩唯一值 → 折叠）
        changed |= SimplifyPhis(func, lattice);

        return changed;
    }

    private static bool RewriteConstants(SsaFunction func, Dictionary<SsaValue, LatticeValue> lattice)
    {
        bool changed = false;

        foreach (var block in func.Blocks)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];
                if (inst.IsConstant) continue; // 已经是常量
                if (inst.HasSideEffect) continue; // 副作用指令不改写结果

                if (!lattice.TryGetValue(inst, out var lv)) continue;
                if (lv.Tag != LatticeTag.Const) continue;

                // 改写为常量
                RewriteToConstant(inst, lv);
                changed = true;
            }
        }

        return changed;
    }

    private static void RewriteToConstant(SsaValue inst, LatticeValue lv)
    {
        // 释放操作数引用
        if (inst.Arg0 != null) { inst.Arg0.Uses--; inst.Arg0 = null; }
        if (inst.Arg1 != null) { inst.Arg1.Uses--; inst.Arg1 = null; }
        if (inst.ExtraArgs != null)
        {
            foreach (var arg in inst.ExtraArgs)
                arg.Uses--;
            inst.ExtraArgs = null;
        }

        switch (lv.ConstKind)
        {
            case SsaOp.ConstBool:
                inst.Op = SsaOp.ConstBool; inst.Type = ScriptType.Bool;
                inst.Const.SetBool(lv.Value.GetBool());
                break;
            case SsaOp.ConstInt:
                inst.Op = SsaOp.ConstInt; inst.Type = ScriptType.Int;
                inst.Const.SetInt(lv.Value.GetInt());
                break;
            case SsaOp.ConstUInt:
                inst.Op = SsaOp.ConstUInt; inst.Type = ScriptType.UInt;
                inst.Const.SetUInt(lv.Value.GetUInt());
                break;
            case SsaOp.ConstUInt64:
                inst.Op = SsaOp.ConstUInt64; inst.Type = ScriptType.UInt64;
                inst.Const.SetUInt64(lv.Value.GetUInt64());
                break;
            case SsaOp.ConstDouble:
                inst.Op = SsaOp.ConstDouble; inst.Type = ScriptType.Double;
                inst.Const.SetDouble(lv.Value.GetDouble());
                break;
            case SsaOp.ConstByte:
                inst.Op = SsaOp.ConstByte; inst.Type = ScriptType.Byte;
                inst.Const.SetByte(lv.Value.GetByte());
                break;
            case SsaOp.ConstPtr:
                inst.Op = SsaOp.ConstPtr; inst.Type = ScriptType.Ptr;
                inst.Const.SetPtr(lv.Value.GetPtr());
                break;
            case SsaOp.ConstString:
                inst.Op = SsaOp.ConstString; inst.Type = ScriptType.String;
                // String lattice 值没有存到 ConstPayload，这里无法恢复
                // 但标准 SCCP 不传播字符串，所以不会到这里
                break;
        }
    }

    /// <summary>
    /// 常量分支折叠：cond true → br TrueSuccessor; cond false → br FalseSuccessor。
    /// 同时清理被删除后继的 Predecessors 引用。
    /// </summary>
    private static bool FoldConstantBranches(SsaFunction func, Dictionary<SsaValue, LatticeValue> lattice)
    {
        bool changed = false;

        foreach (var block in func.Blocks)
        {
            if (block.BranchCondition == null) continue;

            var condLattice = lattice.TryGetValue(block.BranchCondition, out var lv) ? lv : default;
            if (condLattice.Tag != LatticeTag.Const || condLattice.ConstKind != SsaOp.ConstBool)
                continue;

            bool condVal = condLattice.Value.GetBool();
            var keptTarget = condVal ? block.TrueSuccessor! : block.FalseSuccessor!;
            var removedTarget = condVal ? block.FalseSuccessor! : block.TrueSuccessor!;

            // BranchCondition Uses 减 1（不再引用条件值）
            if (block.BranchCondition != null)
                block.BranchCondition.Uses--;

            // 转为无条件跳转
            block.BranchCondition = null;
            block.TrueSuccessor = null;
            block.FalseSuccessor = null;
            block.JumpTarget = keptTarget;

            // 从被删除的后继中移除当前块的前驱引用（同步删除其 phi 对应臂，维护不变量）
            removedTarget.RemovePredecessor(block);

            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Phi 简化（平凡 phi 折叠）：忽略 phi 自引用臂后，若剩余臂全指向同一值即折叠。
    /// 这是 Braun removeTrivialPhi 规则在优化器层面的体现：phi(x, x, phi) == x。
    ///
    /// 与旧实现「严格全臂引用相等」相比，这里能折叠 SCCP 把循环头某臂改写后出现的
    /// 形如 phi(const, self) 的 phi，打断自引用依赖。
    ///
    /// 安全性：折叠条件是「所有非自引用臂均相同」—— 此时每条到达路径都产生该值，
    /// 与 phi 语义等价，无需支配信息。多臂不同时挑支配臂属支配简化（需 Dominators），
    /// 本阶段不处理。死边对应的臂由 FoldConstantBranches/RemoveUnreachableBlocks
    /// 先行删除（RemovePredecessor 同步删臂），故此处无需重复判断可执行性。
    /// </summary>
    private static bool SimplifyPhis(SsaFunction func, Dictionary<SsaValue, LatticeValue> lattice)
    {
        bool changed = false;
        var replacements = new Dictionary<SsaValue, SsaValue>();

        foreach (var block in func.Blocks)
        {
            for (int i = block.Phis.Count - 1; i >= 0; i--)
            {
                var phi = block.Phis[i];
                if (phi.ExtraArgs == null || phi.ExtraArgs.Count == 0)
                    continue;

                // 收集候选唯一值：跳过自引用臂（phi(phi) 不携带新信息）。
                SsaValue? unique = null;  // 候选唯一非自引用臂
                bool ambiguous = false;  // 出现 >1 个不同的非自引用臂
                bool hasAnyArm = false;  // 是否存在至少一条非自引用臂

                foreach (var arm in phi.ExtraArgs)
                {
                    if (arm == phi)
                        continue; // 自引用臂忽略

                    hasAnyArm = true;
                    if (unique == null)
                        unique = arm;
                    else if (unique != arm)
                    {
                        ambiguous = true;
                        break;
                    }
                }

                // 全为自引用（值未定义，运行时取默认）或臂不一致 → 保留 phi
                if (!hasAnyArm || ambiguous)
                    continue;

                // 唯一非自引用臂 → 折叠
                replacements[phi] = unique!;

                // 释放 phi 的 ExtraArgs 引用
                foreach (var arg in phi.ExtraArgs)
                    arg.Uses--;
                phi.ExtraArgs.Clear();

                // 从 Phis 列表移除
                block.Phis.RemoveAt(i);
                changed = true;
            }
        }

        // 一遍扫描应用所有替换
        if (replacements.Count > 0)
            SsaOptimizer.ApplyReplaceMap(func, replacements);

        return changed;
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
                        SsaOptimizer.SetBoolResult(inst, result.Value);
                        SsaOptimizer.ReleaseOperands(inst);
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
                    SsaOptimizer.SetIntResult(inst, 0);
                    SsaOptimizer.ReleaseOperands(inst);
                    changed = true;
                    continue;
                }
                if ((constIsDouble && constDoubleValue == 0.0) &&
                    inst.Op == SsaOp.MulDouble)
                {
                    SsaOptimizer.SetDoubleResult(inst, 0.0);
                    SsaOptimizer.ReleaseOperands(inst);
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
                    SsaOptimizer.SetIntResult(inst, 0);
                    SsaOptimizer.ReleaseOperands(inst);
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

        // 应用替换：批量收集到 map，一遍扫描统一应用
        if (replacements.Count > 0)
        {
            var replaceMap = new Dictionary<SsaValue, SsaValue>();
            foreach (var (inst, replacement) in replacements)
            {
                replaceMap[inst] = replacement;
                // 释放 inst 的操作数引用
                SsaOptimizer.ReleaseOperands(inst);
                inst.Uses = 0;
                changed = true;
            }
            SsaOptimizer.ApplyReplaceMap(func, replaceMap);
        }

        return changed;
    }

    // ============ 常量折叠（已被 SCCP 替代，保留供单元测试） ============

    [Obsolete("已被 SCCP 替代，仅保留供单元测试")]
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
                SsaOptimizer.ReleaseOperands(inst);
            return;
        }

        if (inst.Arg0 != null && inst.Arg1 == null && inst.Arg0.IsConstant)
        {
            if (FoldUnaryInPlace(inst))
                SsaOptimizer.ReleaseOperands(inst);
        }
    }

    private static bool FoldBinaryInPlace(SsaValue inst)
    {
        var left = inst.Arg0!;
        var right = inst.Arg1!;

        switch (inst.Op)
        {
            case SsaOp.AddInt: SsaOptimizer.SetIntResult(inst, left.Const.GetInt() + right.Const.GetInt()); return true;
            case SsaOp.SubInt: SsaOptimizer.SetIntResult(inst, left.Const.GetInt() - right.Const.GetInt()); return true;
            case SsaOp.MulInt: SsaOptimizer.SetIntResult(inst, left.Const.GetInt() * right.Const.GetInt()); return true;
            case SsaOp.DivInt:
                if (right.Const.GetInt() == 0) return false;
                SsaOptimizer.SetIntResult(inst, left.Const.GetInt() / right.Const.GetInt()); return true;
            case SsaOp.ModInt:
                if (right.Const.GetInt() == 0) return false;
                SsaOptimizer.SetIntResult(inst, left.Const.GetInt() % right.Const.GetInt()); return true;
            case SsaOp.RoundDivInt:
                if (right.Const.GetInt() == 0) return false;
                SsaOptimizer.SetIntResult(inst, (int)Math.Round((double)left.Const.GetInt() / right.Const.GetInt())); return true;

            case SsaOp.EqInt: SsaOptimizer.SetBoolResult(inst, left.Const.GetInt() == right.Const.GetInt()); return true;
            case SsaOp.NeqInt: SsaOptimizer.SetBoolResult(inst, left.Const.GetInt() != right.Const.GetInt()); return true;
            case SsaOp.LtInt: SsaOptimizer.SetBoolResult(inst, left.Const.GetInt() < right.Const.GetInt()); return true;
            case SsaOp.LeqInt: SsaOptimizer.SetBoolResult(inst, left.Const.GetInt() <= right.Const.GetInt()); return true;
            case SsaOp.GtInt: SsaOptimizer.SetBoolResult(inst, left.Const.GetInt() > right.Const.GetInt()); return true;
            case SsaOp.GeqInt: SsaOptimizer.SetBoolResult(inst, left.Const.GetInt() >= right.Const.GetInt()); return true;

            case SsaOp.AndInt: SsaOptimizer.SetIntResult(inst, left.Const.GetInt() & right.Const.GetInt()); return true;
            case SsaOp.OrInt: SsaOptimizer.SetIntResult(inst, left.Const.GetInt() | right.Const.GetInt()); return true;
            case SsaOp.XorInt: SsaOptimizer.SetIntResult(inst, left.Const.GetInt() ^ right.Const.GetInt()); return true;
            case SsaOp.ShlInt: SsaOptimizer.SetIntResult(inst, left.Const.GetInt() << right.Const.GetInt()); return true;
            case SsaOp.ShrInt: SsaOptimizer.SetIntResult(inst, left.Const.GetInt() >> right.Const.GetInt()); return true;

            case SsaOp.AddDouble: SsaOptimizer.SetDoubleResult(inst, left.Const.GetDouble() + right.Const.GetDouble()); return true;
            case SsaOp.SubDouble: SsaOptimizer.SetDoubleResult(inst, left.Const.GetDouble() - right.Const.GetDouble()); return true;
            case SsaOp.MulDouble: SsaOptimizer.SetDoubleResult(inst, left.Const.GetDouble() * right.Const.GetDouble()); return true;
            case SsaOp.DivDouble: SsaOptimizer.SetDoubleResult(inst, left.Const.GetDouble() / right.Const.GetDouble()); return true;

            case SsaOp.EqDouble: SsaOptimizer.SetBoolResult(inst, left.Const.GetDouble() == right.Const.GetDouble()); return true;
            case SsaOp.NeqDouble: SsaOptimizer.SetBoolResult(inst, left.Const.GetDouble() != right.Const.GetDouble()); return true;
            case SsaOp.LtDouble: SsaOptimizer.SetBoolResult(inst, left.Const.GetDouble() < right.Const.GetDouble()); return true;
            case SsaOp.LeqDouble: SsaOptimizer.SetBoolResult(inst, left.Const.GetDouble() <= right.Const.GetDouble()); return true;
            case SsaOp.GtDouble: SsaOptimizer.SetBoolResult(inst, left.Const.GetDouble() > right.Const.GetDouble()); return true;
            case SsaOp.GeqDouble: SsaOptimizer.SetBoolResult(inst, left.Const.GetDouble() >= right.Const.GetDouble()); return true;

            case SsaOp.EqBool: SsaOptimizer.SetBoolResult(inst, left.Const.GetBool() == right.Const.GetBool()); return true;
            case SsaOp.NeqBool: SsaOptimizer.SetBoolResult(inst, left.Const.GetBool() != right.Const.GetBool()); return true;

            case SsaOp.EqByte: SsaOptimizer.SetBoolResult(inst, left.Const.GetInt() == right.Const.GetInt()); return true;
            case SsaOp.NeqByte: SsaOptimizer.SetBoolResult(inst, left.Const.GetInt() != right.Const.GetInt()); return true;

            case SsaOp.Concat when left.Op == SsaOp.ConstString && right.Op == SsaOp.ConstString:
                inst.Op = SsaOp.ConstString; inst.Type = ScriptType.String;
                inst.ConstString = (left.ConstString ?? "") + (right.ConstString ?? "");
                return true;

            default: return false;
        }
    }

    private static bool FoldUnaryInPlace(SsaValue inst)
    {
        var operand = inst.Arg0!;

        switch (inst.Op)
        {
            case SsaOp.LogicNot: SsaOptimizer.SetBoolResult(inst, !operand.Const.GetBool()); return true;
            case SsaOp.NotInt: SsaOptimizer.SetIntResult(inst, ~operand.Const.GetInt()); return true;
            case SsaOp.ConvBoolToInt: SsaOptimizer.SetIntResult(inst, operand.Const.GetBool() ? 1 : 0); return true;
            case SsaOp.ConvByteToInt: SsaOptimizer.SetIntResult(inst, operand.Const.GetInt()); return true;
            case SsaOp.ConvIntToDouble: SsaOptimizer.SetDoubleResult(inst, operand.Const.GetInt()); return true;
            case SsaOp.ConvDoubleToInt: SsaOptimizer.SetIntResult(inst, (int)operand.Const.GetDouble()); return true;
            case SsaOp.ConvIntToByte: SsaOptimizer.SetIntResult(inst, (byte)operand.Const.GetInt()); return true;
            case SsaOp.ConvToString:
                inst.Op = SsaOp.ConstString; inst.Type = ScriptType.String;
                inst.ConstString = operand.Const.GetInt().ToString();
                return true;
            case SsaOp.ConvToInt:
                SsaOptimizer.SetIntResult(inst, operand.Const.GetInt());
                return true;
            default: return false;
        }
    }

    // ============ 常量去重 ============

    /// <summary>
    /// 同一函数内，相同值的常量只保留一份，后续引用全部替换为第一份。
    /// 安全：求值器预计算所有常量并缓存，常量不依赖控制流，跨块引用始终可用。
    /// </summary>
    internal static bool DeduplicateConstants(SsaFunction func)
    {
        bool changed = false;
        // key = (SsaOp, 值哈希/字符串)，value = 首次出现的 SsaValue
        var seen = new Dictionary<ConstKey, SsaValue>();
        var replacements = new Dictionary<SsaValue, SsaValue>();
        var toRemove = new List<(SsaBlock block, int index)>();

        foreach (var block in func.Blocks)
        {
            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var inst = block.Instructions[i];
                if (!inst.IsConstant) continue;

                var key = ConstKey.From(inst);
                if (seen.TryGetValue(key, out var original))
                {
                    // 记录延迟替换
                    replacements[inst] = original;
                    toRemove.Add((block, i));
                    changed = true;
                }
                else
                {
                    seen[key] = inst;
                }
            }
        }

        // 一遍扫描应用所有替换
        if (replacements.Count > 0)
        {
            SsaOptimizer.ApplyReplaceMap(func, replacements);

            // 从指令列表中移除重复常量（反向删除避免索引错位）
            for (int i = toRemove.Count - 1; i >= 0; i--)
                toRemove[i].block.Instructions.RemoveAt(toRemove[i].index);
        }

        return changed;
    }

    internal readonly record struct ConstKey(SsaOp Op, int IntHash, long LongHash, string? StrVal)
    {
        public static ConstKey From(SsaValue v) => v.Op switch
        {
            SsaOp.ConstBool => new(v.Op, v.Const.GetBool() ? 1 : 0, 0, null),
            SsaOp.ConstByte => new(v.Op, v.Const.GetByte(), 0, null),
            SsaOp.ConstInt => new(v.Op, v.Const.GetInt(), 0, null),
            SsaOp.ConstUInt => new(v.Op, unchecked((int)v.Const.GetUInt()), 0, null),
            SsaOp.ConstUInt64 => new(v.Op, 0, (long)v.Const.GetUInt64(), null),
            SsaOp.ConstDouble => new(v.Op, 0, BitConverter.DoubleToInt64Bits(v.Const.GetDouble()), null),
            SsaOp.ConstString => new(v.Op, 0, 0, v.ConstString ?? ""),
            SsaOp.ConstPtr => new(v.Op, 0, v.Const.GetPtr(), null),
            _ => new(v.Op, v.Id, 0, null),
        };
    }
}