namespace EasyCon.Script.Bytecode;

/// <summary>
/// 链接期死存储清扫（docs/Pipeline.md 防线 3；LLVM DeadMachineInstructionElim 的镜像级等价物）：
/// SSA φ 降级与变量落槽在块边界留下的死 Move/SetVar、无人读取的常量物化，不进最终镜像。
///
/// 判定 = 反向活跃性数据流：可删指令的定义槽在后继所有路径上无读取（迭代至不动点，
/// 删除可级联——死副本的源值随之死亡）。删除族严格限定无副作用的拷贝/物化六指令：
/// Move / SetVar / LoadI / LoadBool / LoadK / LoadG。SetVar 的深拷贝对象在结果无人读取时
/// 不可观察（副本随帧释放）；LoadK 串常量经解释器驻留缓存，删除仅影响单次运行的分配时刻。
/// 域操作 / Call / CallN / StoreG / 控制流一律保留（副作用或控制依赖）。
///
/// use/def 表按 EcxInterpreter 逐 op 语义登记；未登记操作码 fail-closed（抛出），
/// 指令集扩充时须显式登记——与 EcsFormat「漏登自检」原则一致。use 只可保守多记（少删不误删），
/// def 漏记同向安全。NSlots 不回收（槽位重编号需重写全部寄存器操作数，16B/槽的收益
/// 与重编码风险不成比例；死 φ 的槽已由防线 1 在编码期免分配）。
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
        var code = f.Code;
        if (code.Count == 0)
            return;

        // ---- 1. 指令切分（EXT-aware 步进）；跳转目标先记 pc，扫描完成后统一映射指令下标 ----
        int n = code.Count;
        var ops = new List<EcsOpcode>();
        var instrPc = new List<int>();
        var extWords = new List<uint>();
        var targetPcs = new List<int>();    // 跳转目标 pc；-1 = 非跳转
        int word = 0;
        while (word < n)
        {
            uint ins = code[word];
            var op = (EcsOpcode)(ins & 0xFF);
            ops.Add(op);
            instrPc.Add(word);
            extWords.Add(EcsFormat.ExtWords(op) > 0 ? code[word + 1] : 0);
            targetPcs.Add(op switch
            {
                EcsOpcode.Jmp => word + 1 + Sign24(ins >> 8),
                EcsOpcode.Jpt or EcsOpcode.Jpf => word + 1 + Sign16(ins >> 16),
                _ => -1,
            });
            word += EcsFormat.WordCount(op);
        }
        int count = ops.Count;
        var pcToIdx = new Dictionary<int, int>(count);
        for (int i = 0; i < count; i++)
            pcToIdx[instrPc[i]] = i;
        var targets = new int[count];
        for (int i = 0; i < count; i++)
        {
            int pc = targetPcs[i];
            if (pc < 0)
            {
                targets[i] = -1;
                continue;
            }
            if (!pcToIdx.TryGetValue(pc, out var idx))
                throw new BytecodeException(new[] { new BytecodeDiagnostic($"死存储清扫：跳转目标 pc {pc} 不是指令边界", f.Name, pc) });
            targets[i] = idx;
        }

        // ---- 2. 基本块：leader = 指令 0 / 跳转目标 / 终结符后继 ----
        var leaders = new SortedSet<int> { 0 };
        for (int i = 0; i < count; i++)
        {
            if (targets[i] >= 0)
                leaders.Add(targets[i]);
            bool terminator = ops[i] is EcsOpcode.Jmp or EcsOpcode.Jpt or EcsOpcode.Jpf
                or EcsOpcode.Ret or EcsOpcode.Ret0 or EcsOpcode.Halt;
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
            bool terminator = ops[i] is EcsOpcode.Jmp or EcsOpcode.Jpt or EcsOpcode.Jpf
                or EcsOpcode.Ret or EcsOpcode.Ret0 or EcsOpcode.Halt;
            if (!terminator)
                continue;
            int b = blockOf[i];
            if (targets[i] >= 0)
                succs[b].Add(blockOf[targets[i]]);
            if (ops[i] is EcsOpcode.Jpt or EcsOpcode.Jpf && i + 1 < count)
                succs[b].Add(blockOf[i + 1]);   // 条件分支 fall-through
        }

        // ---- 3. use/def 逐 op 登记（语义权威 = EcxInterpreter 的 ExecOther/主循环）----
        var uses = new List<int>[count];
        var defs = new int[count];
        for (int i = 0; i < count; i++)
        {
            uint ins = code[instrPc[i]];
            int a = (int)((ins >> 8) & 0xFF), b = (int)((ins >> 16) & 0xFF), c = (int)((ins >> 24) & 0xFF);
            var u = new List<int>();
            int d;
            switch (ops[i])
            {
                case EcsOpcode.Move or EcsOpcode.SetVar: d = a; u.Add(b); break;
                case EcsOpcode.LoadI or EcsOpcode.LoadBool or EcsOpcode.LoadK or EcsOpcode.LoadG: d = a; break;
                case EcsOpcode.StoreG: u.Add(a); d = -1; break;
                case EcsOpcode.Jpt or EcsOpcode.Jpf: u.Add(a); d = -1; break;
                case EcsOpcode.Ret: u.Add(a); d = -1; break;
                case EcsOpcode.Call or EcsOpcode.CallN:
                    for (int k = 0; k < b; k++) u.Add(a + k);   // 实参窗
                    d = c == 255 ? -1 : c;                       // C=255：无接收槽
                    break;
                case EcsOpcode.NewArrV:
                    d = a;
                    for (int k = 0; k < b; k++) u.Add(c + k);   // 元素窗
                    break;
                case EcsOpcode.SetI: u.AddRange(new[] { a, b, c }); d = -1; break;
                case EcsOpcode.Slice:
                    d = a; u.Add(b); u.Add(c);
                    if (extWords[i] != 0xFFFFFFFF) u.Add((int)extWords[i]);   // 端点省略哨兵
                    break;
                case EcsOpcode.GetFI: d = a; u.Add(b); u.Add((int)extWords[i]); break;
                case EcsOpcode.PutFI: u.AddRange(new[] { a, b, (int)extWords[i] }); d = -1; break;
                case EcsOpcode.StickPv: u.Add(c); d = -1; break;
                case EcsOpcode.KeyV: u.Add(b); d = -1; break;
                case EcsOpcode.WaitV: u.Add(a); d = -1; break;
                case EcsOpcode.Rand: d = a; u.Add(b); break;
                case EcsOpcode.Img or EcsOpcode.NewArrE or EcsOpcode.NewSt: d = a; break;
                case EcsOpcode.Len or EcsOpcode.GetF or EcsOpcode.Not or EcsOpcode.NegI or EcsOpcode.NegD:
                    d = a; u.Add(b); break;
                case EcsOpcode.PutF: u.AddRange(new[] { a, b }); d = -1; break;
                case EcsOpcode.Conv: d = a; u.Add(b); break;   // c = 转换类别立即数
                case EcsOpcode.Nop or EcsOpcode.Jmp or EcsOpcode.Ret0
                    or EcsOpcode.WaitI or EcsOpcode.KeyI or EcsOpcode.KeySt
                    or EcsOpcode.StickSet or EcsOpcode.StickP or EcsOpcode.Halt:
                    d = -1; break;
                default:
                    // 其余为 iABC 算术/比较族（AddI..GeL/EqS 等）：def=a, use=b,c
                    if (EcsFormat.Get(ops[i]) == EcsInsFormat.Iabc)
                    {
                        d = a; u.Add(b); u.Add(c);
                    }
                    else
                        throw new BytecodeException(new[] { new BytecodeDiagnostic(
                            $"死存储清扫：未登记操作码 {ops[i]}（fail-closed，须显式登记 use/def）", f.Name, instrPc[i]) });
                    break;
            }
            uses[i] = u;
            defs[i] = d;
        }

        // ---- 4. 反向活跃性不动点 + 死指令标记 ----
        // 数据流方程（被标记指令按不存在参与）：
        //   liveOut(B) = ∪ liveIn(s)，liveIn(B) = gen(B) ∪ (liveOut(B) \ kill(B))
        // gen = 块内向上暴露使用；kill = 块内全部定义。删除可级联（死副本的源值随之死亡），
        // 故外层迭代到无新标记为止。
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
            foreach (var b in Enumerable.Range(0, blockCount))
            {
                gen[b].Clear();
                kill[b].Clear();
                int end = b + 1 < blockCount ? blockBegin[b + 1] : count;
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
                    // liveIn = gen ∪ (liveOut \ kill)：gen 是块内向上暴露的使用，
                    // 发生在本块任何定义之前，不受 kill 影响（kill 只作用于 liveOut）
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
                int end = b + 1 < blockCount ? blockBegin[b + 1] : count;
                var live = new HashSet<int>();
                foreach (var s in succs[b])
                    live.UnionWith(liveIn[s]);
                for (int i = end - 1; i >= blockBegin[b]; i--)
                {
                    if (dead[i])
                        continue;
                    int d = defs[i];
                    if (d >= 0 && Removable(ops[i]) && !live.Contains(d))
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

        // ---- 5. 压缩重写：跳转偏移按 old→new pc 重映射（被删指令映射到其后继首个存活指令）----
        var newPcOf = new int[n];
        var newCode = new List<uint>(n);
        int np = 0;
        for (int i = 0; i < count; i++)
        {
            newPcOf[instrPc[i]] = np;
            if (dead[i])
                continue;
            int wc = EcsFormat.WordCount(ops[i]);
            for (int k = 0; k < wc; k++)
                newCode.Add(code[instrPc[i] + k]);
            np += wc;
        }
        int nextPc = np;
        for (int i = count - 1; i >= 0; i--)
        {
            if (!dead[i])
                nextPc = newPcOf[instrPc[i]];
            else
                newPcOf[instrPc[i]] = nextPc;
        }
        for (int i = 0; i < count; i++)
        {
            if (dead[i] || targets[i] < 0)
                continue;
            int oldW = instrPc[i];
            int newW = newPcOf[oldW];
            int newOffset = newPcOf[instrPc[targets[i]]] - (newW + 1);
            uint w = newCode[newW];
            newCode[newW] = ops[i] == EcsOpcode.Jmp
                ? (w & 0xFF) | (uint)(newOffset & 0xFFFFFF) << 8
                : (w & 0xFFFF) | (uint)(newOffset & 0xFFFF) << 16;
        }

        f.Code = newCode;
    }

    /// <summary>可删族：纯拷贝/物化（定义槽死 ⇒ 指令死）。</summary>
    static bool Removable(EcsOpcode op) => op is EcsOpcode.Move or EcsOpcode.SetVar
        or EcsOpcode.LoadI or EcsOpcode.LoadBool or EcsOpcode.LoadK or EcsOpcode.LoadG;

    static int Sign24(uint v) => (int)(v << 8) >> 8;
    static int Sign16(uint v) => (int)(short)v;
}