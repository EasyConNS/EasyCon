using EasyCon.Script;
using EasyCon.Script.Bytecode;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 编解码互逆验证网（docs/SingleStreamFormat.md §4 N2 + N6，v3 定长编码）：
///   Lift(Project(x)) ≡ x（逐指令等价）；Project(Lift(b)) == b（字节恒等）——定长布局下无条件下成立。
/// 覆盖：corpus + 合成边界值用例（AsBx s16、ABx u16、IsJ s24、槽位 u8 + 255 无接收哨兵、
/// 8B 数据字）；截断拒读；跳转落点必须为指令起始字节；格式位宽越界响亮失败。
/// </summary>
[TestFixture]
public class StreamInvariantTests
{
    [Test]
    public void Corpus_RoundTrip_InstructionAndByteIdentity()
    {
        var checkedFuncs = 0;
        foreach (var path in BytecodeSizeAnalysisTests.CorpusSources())
        {
            var result = Compilation.CompileFile(path, new CompileOptions { UseDiskCache = false });
            Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
                string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
            foreach (var f in result.Image!.Functions)
            {
                var bytes = InstructionCodec.Project(f.Instructions);
                var lifted = InstructionCodec.Lift(bytes, 0, bytes.Length, f.Name);
                Assert.That(lifted, Has.Count.EqualTo(f.Instructions.Count), $"[{f.Name}] 指令数一致");
                for (int i = 0; i < f.Instructions.Count; i++)
                    Assert.That(lifted[i], Is.EqualTo(f.Instructions[i]),
                        $"[{f.Name}@{i}] Lift(Project(x)) ≡ x（逐指令）");
                var reprojected = InstructionCodec.Project(lifted);
                Assert.That(reprojected, Is.EqualTo(bytes), $"[{f.Name}] Project(Lift(b)) == b（字节恒等）");
                checkedFuncs++;
            }
        }
        Assert.That(checkedFuncs, Is.GreaterThan(0), "至少覆盖一个函数");
    }

    [Test]
    public void Synthetic_BoundaryValues_Jumps_Ext_RoundTrip()
    {
        // 槽位上界 254 + 无接收哨兵 255、AsBx s16 两端、ABx u16 两端、正反向跳转链、8B 数据字
        var code = new List<EcsInstruction>
        {
            new(EcsOpcode.LoadI, 254, short.MinValue, 0),                 // AsBx s16 下界
            new(EcsOpcode.NewArrV, 200, 3, 201, 3),                       // 8B 数据字
            new(EcsOpcode.Jpt, 254, 0, 0, 0, 5),                          // 前跳 → idx8
            new(EcsOpcode.Jmp, 0, 0, 0, 0, 3),                            // 前跳 → idx7
            new(EcsOpcode.LoadG, 200, ushort.MaxValue),                   // ABx u16 上界（循环头，后跳目标）
            new(EcsOpcode.LoadI, 201, short.MaxValue, 0),                 // AsBx s16 上界
            new(EcsOpcode.Jmp, 0, 0, 0, 0, -3),                           // 后跳 → idx4
            new(EcsOpcode.Slice, 202, 200, 201, 0xFFFFFFFFu),             // 数据字（end 省略哨兵）
            new(EcsOpcode.Call, 0, 3, EcsInstruction.NoSlot, 7),          // C = NoSlot（线上 255）
            new(EcsOpcode.Ret, 202, 0, 0),
        };

        var bytes = InstructionCodec.Project(code);
        Assert.That(bytes.Length, Is.EqualTo(code.Sum(i => InstructionCodec.InstructionSize(i))), "总字节 = Σ定长尺寸");
        var lifted = InstructionCodec.Lift(bytes, 0, bytes.Length, "synthetic");
        Assert.That(lifted, Is.EqualTo(code), "边界值 + 跳转 + 数据字往返逐指令等价");
        Assert.That(InstructionCodec.Project(lifted), Is.EqualTo(bytes), "字节恒等");
    }

    [Test]
    public void Project_RejectsFormatWidthOverflows()
    {
        // 槽位 > 254 → 响亮拒绝（v3 定长 u8）
        var badSlot = new List<EcsInstruction> { new(EcsOpcode.LoadI, 40000, 7, 0) };
        Assert.That(() => InstructionCodec.Project(badSlot), Throws.TypeOf<BytecodeException>(), "槽位越界应拒绝");

        // AsBx 立即数超 s16 → 响亮拒绝（发射侧 LoadK 归一，此处验证编解码层守卫）
        var badSbx = new List<EcsInstruction> { new(EcsOpcode.LoadI, 3, 100000, 0) };
        Assert.That(() => InstructionCodec.Project(badSbx), Throws.TypeOf<BytecodeException>(), "sBx 越界应拒绝");

        // ABx 超	u16 → 响亮拒绝
        var badBx = new List<EcsInstruction> { new(EcsOpcode.LoadK, 3, 0x10000, 0) };
        Assert.That(() => InstructionCodec.Project(badBx), Throws.TypeOf<BytecodeException>(), "Bx 越界应拒绝");
    }

    [Test]
    public void Lift_RejectsTruncatedInstruction()
    {
        // 定长截断：LoadI 4B 只给 3 字节
        var truncated = new byte[] { (byte)EcsOpcode.LoadI, 1, 0x00 };
        Assert.That(() => InstructionCodec.Lift(truncated, 0, truncated.Length, "t"),
            Throws.TypeOf<BytecodeException>(), "截断指令应拒读");

        // 8B 指令缺数据字（只有 4B 头）
        var truncatedWord = new byte[] { (byte)EcsOpcode.Call, 0, 3, 255 };
        Assert.That(() => InstructionCodec.Lift(truncatedWord, 0, truncatedWord.Length, "w"),
            Throws.TypeOf<BytecodeException>(), "数据字截断应拒读");
    }

    [Test]
    public void Lift_RejectsJumpNotLandingOnInstructionStart()
    {
        // Jmp 偏移 +1 落在指令中段（非法）：篡改偏移字节使落点不再是指令起始
        var code = new List<EcsInstruction>
        {
            new(EcsOpcode.Jmp, 0, 0, 0, 0, 1),   // 前跳 1 条（目标 = 函数尾之后？→ n=2 时 t=2=尾，合法落点=字节 8）
            new(EcsOpcode.Ret, 0, 0, 0),
        };
        var bytes = InstructionCodec.Project(code);
        // 篡改 Jmp 的偏移字节 +1 → 落点不再是指令起始
        bytes[1] += 1;
        Assert.That(() => InstructionCodec.Lift(bytes, 0, bytes.Length, "j"),
            Throws.TypeOf<BytecodeException>(), "跳转落点非指令起始字节应拒读（N6）");
    }
}