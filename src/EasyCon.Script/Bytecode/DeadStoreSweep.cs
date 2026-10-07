namespace EasyCon.Script.Bytecode;

/// <summary>
/// 链接期死存储清扫（docs/Pipeline.md 防线 3；LLVM DeadMachineInstructionElim 的镜像级等价物）：
/// SSA φ 降级与变量落槽在块边界留下的死 Move/SetVar、无人读取的常量物化，不进最终镜像。
///
/// 判定 = 反向活跃性数据流：可删指令的定义槽在后继所有路径上无读取（迭代至不动点，
/// 删除可级联——死副本的源值随之死亡）。删除族严格限定无副作用的拷贝/物化六指令：
/// Move / SetVar / LoadI / LoadBool / LoadK / LoadG。SetVar 的深拷贝对象在结果无人读取时
/// 不可观察（副本随帧释放）；LoadK 串常量为 pinned 静态值（S-20），删除不影响任何分配。
/// 域操作 / Call / CallN / StoreG / 控制流一律保留（副作用或控制依赖）。
///
/// use/def 表按 EcxInterpreter 逐 op 语义登记；未登记操作码 fail-closed（抛出），
/// 指令集扩充时须显式登记——与 EcsFormat「漏登自检」原则一致。use 只可保守多记（少删不误删），
/// def 漏记同向安全。NSlots 回收在编码期完成（BytecodeEncoder：StoreLocal 仅参数发射——
/// 非参数局部的帧槽无读取者；TRE 回边参数 store 保留——entry 循环头每轮重读参数槽——
/// 配合 AssignSlots 步骤 0 存活局部重编号，非参数局部不占帧槽）；
/// 死 φ 的槽由防线 1 在编码期免分配。
///
/// 单流化（docs/SingleStreamFormat.md §2）：解码形态下不再有「槽位被截断的影子流」，
/// PcCode 早退随之移除——所有函数一视同仁（原 R6 风险由专项用例覆盖）。
/// </summary>
public static class DeadStoreSweep
{
    public static void Sweep(EcxImage image)
    {
        foreach (var f in image.Functions)
            SweepFunction(f);
    }

    static void SweepFunction(EcsFunction f)
    {
        var code = f.Instructions;
        if (code.Count == 0)
            return;

        int count = code.Count;

        // ---- 1. 跳转目标（解码形态：单位 = 相对指令下标）----
        var targets = new int[count];
        for (int i = 0; i < count; i++)
        {
            if (!code[i].IsJump && code[i].Op is not (EcsOpcode.ForStep or EcsOpcode.CmpJ))
            {
                targets[i] = -1;
                continue;
            }
            int t = i + 1 + code[i].Jump;
            if (t < 0 || t > count)
                throw new BytecodeException(new[] { new BytecodeDiagnostic($"死存储清扫：跳转目标下标 {t} 越界", f.Name, i) });
            targets[i] = t;
        }

        // ---- 2. 基本块：leader = 指令 0 / 跳转目标 / 终结符后继 ----
        var leaders = new SortedSet<int> { 0 };
        for (int i = 0; i < count; i++)
        {
            if (targets[i] >= 0)
                leaders.Add(targets[i]);
            bool terminator = ops(code[i].Op);
            if (terminator && i + 1 < count)
                leaders.Add(i + 1);
        }
        var blockBegin = leaders.ToList();
        var blockOf = new int[count];
        for (int b = 0; b < blockBegin.Count; b++)
        {
            int end = b + 1 < blockBegin.Count ? blockBegin[b + 1] : count;
            for (int i = blockBegin[b]; i < end; i++)
                blockOf[i] = b;
        }
        int blockCount = blockBegin.Count;
        var succs = new List<int>[blockCount];
        for (int b = 0; b < blockCount; b++)
            succs[b] = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (!ops(code[i].Op))
                continue;
            int b = blockOf[i];
            if (targets[i] >= 0)
                succs[b].Add(blockOf[targets[i]]);
            if (code[i].Op is EcsOpcode.Jpt or EcsOpcode.Jpf or EcsOpcode.ForStep or EcsOpcode.CmpJ && i + 1 < count)
                succs[b].Add(blockOf[i + 1]);   // 条件分支 fall-through（ForStep：i==upper 出循环；CmpJ：比较不成立）
        }

        // 无终结符的块（编码期尾跳转消除后的直落块，P1a）：控制流入下一块 → 补 fall-through 边。
        // 漏边会让直落块的 liveOut 为空，块内定义被误判死指令（入口初始化被删，corpus arith 实证）。
        for (int b = 0; b < blockCount; b++)
        {
            int end = b + 1 < blockBegin.Count ? blockBegin[b + 1] : count;
            if (end > blockBegin[b] && !ops(code[end - 1].Op) && b + 1 < blockCount)
                succs[b].Add(b + 1);
        }

        // ---- 3. use/def 逐 op 登记（语义权威 = EcxInterpreter 的 ExecOther/主循环）----
        var uses = new List<int>[count];
        var defs = new int[count];
        for (int i = 0; i < count; i++)
        {
            var ins = code[i];
            var u = new List<int>();
            int d;
            switch (ins.Op)
            {
                case EcsOpcode.Move or EcsOpcode.SetVar: d = ins.A; u.Add(ins.B); break;
                case EcsOpcode.LoadI or EcsOpcode.LoadBool or EcsOpcode.LoadK or EcsOpcode.LoadG: d = ins.A; break;
                case EcsOpcode.StoreG: u.Add(ins.A); d = -1; break;
                case EcsOpcode.Jpt or EcsOpcode.Jpf: u.Add(ins.A); d = -1; break;
                case EcsOpcode.Ret: u.Add(ins.A); d = -1; break;
                case EcsOpcode.Call or EcsOpcode.CallN:
                    for (int k = 0; k < ins.B; k++) u.Add(ins.A + k);   // 实参窗
                    d = ins.C;                                          // NoSlot(-1)：无接收槽
                    break;
                case EcsOpcode.NewArrV:
                    d = ins.A;
                    for (int k = 0; k < ins.B; k++) u.Add(ins.C + k);   // 元素窗
                    break;
                case EcsOpcode.SetI: u.AddRange(new[] { ins.A, ins.B, ins.C }); d = -1; break;
                case EcsOpcode.Slice:
                    d = ins.A; u.Add(ins.B); u.Add(ins.C);
                    if (ins.Ext != 0xFFFFFFFF) u.Add((int)ins.Ext);     // 端点省略哨兵
                    break;
                case EcsOpcode.GetFI: d = ins.A; u.Add(ins.B); u.Add((int)ins.Ext); break;
                case EcsOpcode.PutFI: u.AddRange(new[] { ins.A, ins.B, (int)ins.Ext }); d = -1; break;
                case EcsOpcode.StickPv: u.Add(ins.C); d = -1; break;
                case EcsOpcode.KeyV: u.Add(ins.B); d = -1; break;
                case EcsOpcode.WaitV: u.Add(ins.A); d = -1; break;
                case EcsOpcode.Rand: d = ins.A; u.Add(ins.B); break;
                case EcsOpcode.Img or EcsOpcode.NewArrE or EcsOpcode.NewSt: d = ins.A; break;
                case EcsOpcode.Len or EcsOpcode.GetF or EcsOpcode.Not or EcsOpcode.NegI or EcsOpcode.NegD:
                    d = ins.A; u.Add(ins.B); break;
                case EcsOpcode.PutF: u.AddRange(new[] { ins.A, ins.B }); d = -1; break;
                case EcsOpcode.Conv: d = ins.A; u.Add(ins.B); break;   // C = 转换类别立即数
                case EcsOpcode.Nop or EcsOpcode.Jmp or EcsOpcode.Ret0
                    or EcsOpcode.WaitI or EcsOpcode.KeyI or EcsOpcode.KeySt
                    or EcsOpcode.StickSet or EcsOpcode.StickP or EcsOpcode.Halt:
                    d = -1; break;
                case EcsOpcode.ForStep:
                    u.AddRange(new[] { ins.A, ins.B }); d = ins.C; break;   // 写 dst（c），读 src/limit
                case EcsOpcode.CmpJ:
                    u.AddRange(new[] { ins.A, ins.B }); d = -1; break;   // 纯比较跳转：读 A/B，C=kind 码非槽位，无 def
                default:
                    // 其余为 iABC 算术/比较族（AddI..GeL/EqS 等）：def=a, use=b,c
                    if (EcsFormat.Get(ins.Op) == EcsInsFormat.Iabc)
                    {
                        d = ins.A; u.Add(ins.B); u.Add(ins.C);
                    }
                    else
                        throw new BytecodeException(new[] { new BytecodeDiagnostic(
                            $"死存储清扫：未登记操作码 {ins.Op}（fail-closed，须显式登记 use/def）", f.Name, i) });
                    break;
            }
            uses[i] = u;
            defs[i] = d;
        }

        // ---- 4. 反向活跃性不动点 + 死指令标记 ----
        var dead = new bool[count];
        var liveIn = new HashSet<int>[blockCount];
        for (int b = 0; b < blockCount; b++)
            liveIn[b] = new HashSet<int>();
        var gen = new HashSet<int>[blockCount];
        var kill = new HashSet<int>[blockCount];
        for (int b = 0; b < blockCount; b++)
        {
            gen[b] = new HashSet<int>();
            kill[b] = new HashSet<int>();
        }

        bool changed = true;
        while (changed)
        {
            changed = false;

            // gen/kill 随 dead 标记重算（块内正向：未定义先使用 ⇒ 向上暴露）
            for (int b = 0; b < blockCount; b++)
            {
                gen[b].Clear();
                kill[b].Clear();
                int end = b + 1 < blockBegin.Count ? blockBegin[b + 1] : count;
                var defined = new HashSet<int>();
                for (int i = blockBegin[b]; i < end; i++)
                {
                    if (dead[i])
                        continue;
                    foreach (var u in uses[i])
                        if (defined.Add(u))
                            gen[b].Add(u);
                    if (defs[i] >= 0 && kill[b].Add(defs[i]))
                        defined.Add(defs[i]);
                }
            }

            // liveIn 不动点（逆序）
            bool flow = true;
            while (flow)
            {
                flow = false;
                for (int b = blockCount - 1; b >= 0; b--)
                {
                    var liveOut = new HashSet<int>();
                    foreach (var s in succs[b])
                        liveOut.UnionWith(liveIn[s]);
                    var next = new HashSet<int>(liveOut);
                    next.ExceptWith(kill[b]);
                    next.UnionWith(gen[b]);
                    if (!next.SetEquals(liveIn[b]))
                    {
                        liveIn[b] = next;
                        flow = true;
                    }
                }
            }

            // 标记：块内反向扫描，live = liveOut(B)
            for (int b = 0; b < blockCount; b++)
            {
                int end = b + 1 < blockBegin.Count ? blockBegin[b + 1] : count;
                var live = new HashSet<int>();
                foreach (var s in succs[b])
                    live.UnionWith(liveIn[s]);
                for (int i = end - 1; i >= blockBegin[b]; i--)
                {
                    if (dead[i])
                        continue;
                    int d = defs[i];
                    if (d >= 0 && Removable(code[i].Op) && !live.Contains(d))
                    {
                        dead[i] = true;
                        changed = true;
                        continue;
                    }
                    if (d >= 0)
                        live.Remove(d);
                    live.UnionWith(uses[i]);
                }
            }
        }

        if (Environment.GetEnvironmentVariable("ECX_SWEEP_TRACE") == "2")
        {
            for (int i = 0; i < count; i++)
                Console.Error.WriteLine($"[sweep2] f={f.Name} i={i} {code[i].Op} A={code[i].A} B={code[i].B} C={code[i].C} J={code[i].Jump} targets={targets[i]} block={blockOf[i]}");
        }
        if (Environment.GetEnvironmentVariable("ECX_SWEEP_TRACE") == "1")
        {
            for (int b = 0; b < blockCount; b++)
            {
                int end = b + 1 < blockBegin.Count ? blockBegin[b + 1] : count;
                Console.Error.WriteLine($"[sweep] B{b} [{blockBegin[b]}..{end - 1}] succs=[{string.Join(",", succs[b])}] liveIn=[{string.Join(",", liveIn[b].Order())}] dead=[{string.Join(",", Enumerable.Range(blockBegin[b], end - blockBegin[b]).Where(i => dead[i]))}]");
            }
        }

        // ---- 5. 压缩重写：跳转偏移按下标重映射（被删指令映射到其后继首个存活指令）----
        var newIdxOf = new int[count];
        var newCode = new List<EcsInstruction>(count);
        for (int i = 0; i < count; i++)
        {
            newIdxOf[i] = newCode.Count;
            if (!dead[i])
                newCode.Add(code[i]);
        }
        int nextIdx = newCode.Count;
        for (int i = count - 1; i >= 0; i--)
        {
            if (!dead[i])
                nextIdx = newIdxOf[i];
            else
                newIdxOf[i] = nextIdx;
        }
        for (int i = 0; i < count; i++)
        {
            if (dead[i] || targets[i] < 0)
                continue;
            int newOffset = newIdxOf[targets[i]] - (newIdxOf[i] + 1);
            newCode[newIdxOf[i]] = newCode[newIdxOf[i]] with { Jump = newOffset };
        }

        f.Instructions = newCode;
    }

    static bool ops(EcsOpcode op) => op is EcsOpcode.Jmp or EcsOpcode.Jpt or EcsOpcode.Jpf
        or EcsOpcode.Ret or EcsOpcode.Ret0 or EcsOpcode.Halt or EcsOpcode.ForStep or EcsOpcode.CmpJ;

    /// <summary>可删族：纯拷贝/物化（定义槽死 ⇒ 指令死）。</summary>
    static bool Removable(EcsOpcode op) => op is EcsOpcode.Move or EcsOpcode.SetVar
        or EcsOpcode.LoadI or EcsOpcode.LoadBool or EcsOpcode.LoadK or EcsOpcode.LoadG;
}