namespace EasyCon.Script.Bytecode;

/// <summary>
/// 解码形态 ↔ 定长字节流的单处换算（docs/SingleStreamFormat.md §2/§5，v3 定长编码）。
///
/// 线格式 = 定长指令：4B（Iabc / ABx / AsBx / IsJ）或 8B（Ext / IabcJ = iABC + u32 数据字），
/// 尺寸由 <see cref="EcsFormat.WordCount"/> 唯一决定；操作数位宽由格式锁定——
/// 槽位/立即字节 = u8（Call/CallN 的接收槽 255 = 无）、ABx = u16、AsBx = s16、IsJ = s24、
/// 数据字 = u32（AsBx/IsJ/IabcJ 的「数据字/尾字段」承载跳转的**字节**偏移）。
/// Project(Lift(b)) == b、Lift(Project(x)) ≡ x 无条件下成立（布局不再依赖内容）。
///
/// 跳转偏移的双表示：**内存** = 相对指令下标（解释器按下标取指；前插平移不变）；
/// **线格式** = 相对**字节**偏移（目标 = 指令起始 + 本指令字节数 + delta）。
/// 换算只发生在本类：<see cref="Project"/> 一遍计算布局（定长 ⇒ 无定点迭代），
/// <see cref="Lift"/> 借指令起始字节表反查目标下标。
/// </summary>
public static class InstructionCodec
{
    /// <summary>每条指令的字节数（4B 或 8B，由格式表唯一决定）。</summary>
    public static int SizeOf(EcsOpcode op) => EcsFormat.WordCount(op) * 4;

    public static int InstructionSize(in EcsInstruction ins) => SizeOf(ins.Op);

    // ---- 布局依赖的跳转（内存 Jump（指令下标）→ 线上字节偏移） ----

    static bool HasLayoutDelta(in EcsInstruction ins)
        => ins.IsJump || ins.Op is EcsOpcode.ForStep or EcsOpcode.CmpJ;

    static int DeltaTargetIndex(in EcsInstruction ins, int i) => i + 1 + ins.Jump;

    static int JumpByteLimit(EcsOpcode op) => op switch
    {
        EcsOpcode.Jmp => (1 << 23) - 1,        // IsJ: s24
        EcsOpcode.Jpt or EcsOpcode.Jpf => short.MaxValue,   // AsBx: s16
        _ => int.MaxValue,                     // IabcJ 数据字: s32 全宽
    };

    static void CheckJumpRange(EcsOpcode op, int jumpBytes, int at)
    {
        int limit = JumpByteLimit(op);
        if (jumpBytes < -limit - 1 || jumpBytes > limit)
            throw new BytecodeException(new[] { new BytecodeDiagnostic(
                $"{op} 跳转字节偏移越界: {jumpBytes}（格式容许 ±{limit}）", null, at) });
    }

    static byte SlotByte(int v, EcsOpcode op, int at)
    {
        if (v == EcsInstruction.NoSlot)
            return 255;   // 无接收槽哨兵（槽位号 ≤254，无歧义）
        if (v < 0 || v > 254)
            throw new BytecodeException(new[] { new BytecodeDiagnostic(
                $"{op} 槽位越界: {v}（v3 定长编码槽位为 u8，≤254）", null, at) });
        return (byte)v;
    }

    static byte ByteOperand(int v, EcsOpcode op, string name, int at)
    {
        if (v < 0 || v > 255)
            throw new BytecodeException(new[] { new BytecodeDiagnostic(
                $"{op} 的 {name} 越界: {v}（v3 定长编码为 u8）", null, at) });
        return (byte)v;
    }

    // ---- Project：解码形态 → 字节流 ----

    /// <summary>编码一个函数的指令序列为定长字节流（一遍布局；跳转范围越界响亮失败）。</summary>
    public static byte[] Project(IReadOnlyList<EcsInstruction> instructions)
    {
        int n = instructions.Count;
        var offsets = new int[n + 1];
        int off = 0;
        for (int i = 0; i < n; i++)
        {
            offsets[i] = off;
            off += InstructionSize(instructions[i]);
        }
        offsets[n] = off;

        var buf = new List<byte>(off);
        for (int i = 0; i < n; i++)
        {
            var ins = instructions[i];
            int size = InstructionSize(ins);
            int jumpBytes = 0;
            if (HasLayoutDelta(ins))
            {
                int targetIdx = DeltaTargetIndex(ins, i);
                if (targetIdx < 0 || targetIdx > n)
                    throw new BytecodeException(new[] { new BytecodeDiagnostic(
                        $"跳转目标指令下标越界: {targetIdx}（共 {n} 条）", null, i) });
                jumpBytes = offsets[targetIdx] - (offsets[i] + size);
                CheckJumpRange(ins.Op, jumpBytes, i);
            }

            buf.Add((byte)ins.Op);
            switch (EcsFormat.Get(ins.Op))
            {
                case EcsInsFormat.Iabc:
                    buf.Add(SlotByte(ins.A, ins.Op, i));
                    buf.Add(ByteOperand(ins.B, ins.Op, "B", i));
                    buf.Add(ins.C == EcsInstruction.NoSlot ? (byte)255 : ByteOperand(ins.C, ins.Op, "C", i));
                    break;

                case EcsInsFormat.ABx:
                    buf.Add(SlotByte(ins.A, ins.Op, i));
                    W16(buf, ins.B >= 0 && ins.B <= 0xFFFF ? ins.B
                        : throw new BytecodeException(new[] { new BytecodeDiagnostic(
                            $"{ins.Op} 的 Bx 越界: {ins.B}（u16）", null, i) }));
                    break;

                case EcsInsFormat.AsBx:
                    buf.Add(SlotByte(ins.A, ins.Op, i));
                    if (ins.IsJump) { CheckJumpRange(ins.Op, jumpBytes, i); S16(buf, jumpBytes); }
                    else S16(buf, ins.B >= short.MinValue && ins.B <= short.MaxValue ? ins.B
                        : throw new BytecodeException(new[] { new BytecodeDiagnostic(
                            $"{ins.Op} 的 sBx 越界: {ins.B}（s16）", null, i) }));
                    break;

                case EcsInsFormat.IsJ:
                    CheckJumpRange(ins.Op, jumpBytes, i);
                    S24(buf, jumpBytes);
                    break;

                case EcsInsFormat.Ext:
                    buf.Add(SlotByte(ins.A, ins.Op, i));
                    buf.Add(ByteOperand(ins.B, ins.Op, "B", i));
                    buf.Add(ins.C == EcsInstruction.NoSlot ? (byte)255 : ByteOperand(ins.C, ins.Op, "C", i));
                    W32(buf, unchecked((int)ins.Ext));
                    break;

                case EcsInsFormat.IabcJ:
                    buf.Add(SlotByte(ins.A, ins.Op, i));
                    buf.Add(ByteOperand(ins.B, ins.Op, "B", i));
                    buf.Add(ByteOperand(ins.C, ins.Op, "C", i));
                    W32(buf, jumpBytes);
                    break;

                default:
                    throw new BytecodeException(new[] { new BytecodeDiagnostic(
                        $"{ins.Op} 未登记编码格式", null, i) });
            }
        }
        return buf.ToArray();
    }

    // ---- Lift：字节流 → 解码形态 ----

    /// <summary>解码单个函数的字节流区间 [start, end) 回指令序列。校验：操作码已登记、
    /// 指令完整（定长截断即拒）、跳转落点必须是某条指令的**起始字节**（验证网 N6）。</summary>
    public static List<EcsInstruction> Lift(ReadOnlySpan<byte> stream, int start, int end, string? function = null)
        => LiftWithStarts(stream, start, end, out _, function);

    /// <summary>同 <see cref="Lift"/>，另回传每条指令的起始字节（SEC_LINES 的字节偏移 → 指令下标还原用）。</summary>
    public static List<EcsInstruction> LiftWithStarts(ReadOnlySpan<byte> stream, int start, int end,
        out List<int> instructionStarts, string? function = null)
    {
        var result = new List<EcsInstruction>();
        var startBytes = new List<int>();
        var rawJumpBytes = new List<int>();   // 与 result 平行：跳转指令的线格式字节偏移（非跳转 = 0）
        int pos = start;
        while (pos < end)
        {
            int insStart = pos;
            var op = (EcsOpcode)stream[pos];
            if ((int)op >= System.Enum.GetValues<EcsOpcode>().Length)
                throw Fail($"未登记操作码 {(int)op}", function, insStart);
            int size = SizeOf(op);
            if (pos + size > end)
                throw Fail("指令流截断（定长指令不完整）", function, insStart);
            int a = stream[pos + 1];
            int b = stream[pos + 2];
            int c = stream[pos + 3];
            uint word = 0;
            if (size == 8)
                word = (uint)(stream[pos + 4] | stream[pos + 5] << 8 | stream[pos + 6] << 16 | stream[pos + 7] << 24);
            pos += size;

            int rawJump = 0;
            uint ext = 0;
            bool isBranch = op is EcsOpcode.Jmp or EcsOpcode.Jpt or EcsOpcode.Jpf;
            switch (EcsFormat.Get(op))
            {
                case EcsInsFormat.Iabc:
                    if (op is EcsOpcode.Call or EcsOpcode.CallN && c == 255)
                        c = EcsInstruction.NoSlot;   // 无接收槽哨兵
                    break;

                case EcsInsFormat.ABx:
                    b = b | c << 8;
                    c = 0;
                    break;

                case EcsInsFormat.AsBx:
                    {
                        int sbx = (short)(b | c << 8);
                        if (isBranch) { b = c = 0; rawJump = sbx; }
                        else { b = sbx; c = 0; }
                        break;
                    }

                case EcsInsFormat.IsJ:
                    {
                        int sj = stream[insStart + 1] | stream[insStart + 2] << 8 | stream[insStart + 3] << 16;
                        sj = (sj << 8) >> 8;   // s24 符号扩展
                        a = b = c = 0;
                        rawJump = sj;
                        break;
                    }

                case EcsInsFormat.Ext:
                    if (op is EcsOpcode.Call or EcsOpcode.CallN && c == 255)
                        c = EcsInstruction.NoSlot;
                    ext = word;
                    break;

                case EcsInsFormat.IabcJ:
                    rawJump = (int)word;   // 出口/跳转偏移（ForStep/CmpJ）
                    break;
            }

            startBytes.Add(insStart);
            rawJumpBytes.Add(rawJump);
            result.Add(isBranch
                ? new EcsInstruction(op, a, 0, 0, ext)
                : new EcsInstruction(op, a, b, c, ext));
        }

        instructionStarts = startBytes;
        // 跳转换算：字节偏移 → 相对指令下标（落点必须是指令起始字节，N6）；ForStep/CmpJ 的偏移在数据字
        for (int i = 0; i < result.Count; i++)
        {
            bool layoutJump = HasLayoutDelta(result[i]);
            if (!layoutJump)
                continue;
            int raw = rawJumpBytes[i];
            int targetByte = startBytes[i] + SizeOf(result[i].Op) + raw;
            int targetIdx = startBytes.BinarySearch(targetByte);
            if (targetIdx < 0)
                throw Fail($"跳转落点 {targetByte} 不是指令起始字节", function, i);
            int delta = targetIdx - (i + 1);
            result[i] = result[i] with { Jump = delta };
        }
        return result;
    }

    // ---- 小端写入 ----

    static void W16(List<byte> buf, int v)
    {
        buf.Add((byte)v);
        buf.Add((byte)(v >> 8));
    }

    static void S16(List<byte> buf, int v)
    {
        ushort u = unchecked((ushort)v);
        W16(buf, u);
    }

    static void S24(List<byte> buf, int v)
    {
        uint u = unchecked((uint)v) & 0xFFFFFF;
        buf.Add((byte)u);
        buf.Add((byte)(u >> 8));
        buf.Add((byte)(u >> 16));
    }

    static void W32(List<byte> buf, int v)
    {
        uint u = unchecked((uint)v);
        buf.Add((byte)u);
        buf.Add((byte)(u >> 8));
        buf.Add((byte)(u >> 16));
        buf.Add((byte)(u >> 24));
    }

    static BytecodeException Fail(string message, string? function, int at)
        => new(new[] { new BytecodeDiagnostic(message, function, at) });
}