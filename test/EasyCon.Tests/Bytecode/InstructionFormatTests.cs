using EasyCon.Script.Bytecode;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// EcsFormat 指令格式表锁定（docs/VM2.md §4.1/§4.2 的代码侧投影）：
/// 表是全库唯一的「哪些操作码带 EXT 后随数据字」事实源（EcxLinker 扫描步进、编码器发射自检、
/// 解释器 EXT 预取、反汇编字数、C 侧 ecs_op_has_ext 均消费它）。本测试把表内容与 §4.1/§4.2
/// 规格逐条对锁——任何一端（表/C 侧/文档）漂移都在此显式失败，而不是静默误读 EXT 数据字
/// （历史 F4 缺陷形态）。
/// </summary>
[TestFixture]
public class InstructionFormatTests
{
    static readonly EcsOpcode[] Vm2ExtOps =
    [
        EcsOpcode.Call, EcsOpcode.CallN, EcsOpcode.NewArrV, EcsOpcode.Slice,
        EcsOpcode.GetFI, EcsOpcode.PutFI, EcsOpcode.StickP, EcsOpcode.StickPv,
    ];

    [Test]
    public void ExtOps_ExactlyMatchVm2Spec()
    {
        var withExt = new List<EcsOpcode>();
        foreach (var op in Enum.GetValues<EcsOpcode>())
            if (EcsFormat.ExtWords(op) > 0)
                withExt.Add(op);
        Assert.That(withExt, Is.EquivalentTo(Vm2ExtOps));
    }

    [Test]
    public void WordCount_ExtIsTwoOthersOne()
    {
        foreach (var op in Enum.GetValues<EcsOpcode>())
        {
            int expected = EcsFormat.Get(op) == EcsInsFormat.Ext ? 2 : 1;
            Assert.That(EcsFormat.WordCount(op), Is.EqualTo(expected), $"{op} 总字数");
            Assert.That(EcsFormat.ExtWords(op), Is.EqualTo(expected - 1), $"{op} EXT 数据字数");
        }
    }

    [Test]
    public void ExtKind_MatchesDataWordSemantics()
    {
        var expected = new Dictionary<EcsOpcode, EcsExtKind>
        {
            [EcsOpcode.Call] = EcsExtKind.CallTarget,
            [EcsOpcode.CallN] = EcsExtKind.NativeId,
            [EcsOpcode.NewArrV] = EcsExtKind.ElemTypeCode,
            [EcsOpcode.Slice] = EcsExtKind.SliceEndSlot,
            [EcsOpcode.GetFI] = EcsExtKind.FieldElemSlot,
            [EcsOpcode.PutFI] = EcsExtKind.FieldElemSlot,
            [EcsOpcode.StickP] = EcsExtKind.StickDuration,
            [EcsOpcode.StickPv] = EcsExtKind.StickXY,
        };
        foreach (var op in Enum.GetValues<EcsOpcode>())
        {
            var kind = EcsFormat.ExtKind(op);
            if (expected.TryGetValue(op, out var want))
                Assert.That(kind, Is.EqualTo(want), $"{op} EXT 数据字语义");
            else
                Assert.That(kind, Is.EqualTo(EcsExtKind.None), $"{op} 不应有 EXT 语义");
        }
    }

    [Test]
    public void Formats_MatchVm2Section41()
    {
        var abx = new[]
        {
            EcsOpcode.LoadK, EcsOpcode.LoadG, EcsOpcode.StoreG, EcsOpcode.NewArrE,
            EcsOpcode.NewSt, EcsOpcode.WaitI, EcsOpcode.KeyI, EcsOpcode.Img,
        };
        foreach (var op in abx)
            Assert.That(EcsFormat.Get(op), Is.EqualTo(EcsInsFormat.ABx), $"{op}");

        Assert.That(EcsFormat.Get(EcsOpcode.LoadI), Is.EqualTo(EcsInsFormat.AsBx));
        Assert.That(EcsFormat.Get(EcsOpcode.Jpt), Is.EqualTo(EcsInsFormat.AsBx));
        Assert.That(EcsFormat.Get(EcsOpcode.Jpf), Is.EqualTo(EcsInsFormat.AsBx));
        Assert.That(EcsFormat.Get(EcsOpcode.Jmp), Is.EqualTo(EcsInsFormat.IsJ));

        var iabc = new[]
        {
            EcsOpcode.LoadBool, EcsOpcode.Move, EcsOpcode.SetVar, EcsOpcode.AddI,
            EcsOpcode.DivD, EcsOpcode.EqS, EcsOpcode.Conv, EcsOpcode.GetI,
            EcsOpcode.SetI, EcsOpcode.Append, EcsOpcode.Cat, EcsOpcode.Len,
            EcsOpcode.GetF, EcsOpcode.PutF, EcsOpcode.Rand, EcsOpcode.StickSet,
        };
        foreach (var op in iabc)
            Assert.That(EcsFormat.Get(op), Is.EqualTo(EcsInsFormat.Iabc), $"{op}");
    }

    [Test]
    public void ResultSlots_MatchVm2Section42()
    {
        // 写 R[A]：取值/运算/比较/构造类
        var writesA = new[]
        {
            EcsOpcode.LoadI, EcsOpcode.LoadK, EcsOpcode.LoadBool, EcsOpcode.Move,
            EcsOpcode.SetVar, EcsOpcode.LoadG, EcsOpcode.AddI, EcsOpcode.EqS,
            EcsOpcode.Conv, EcsOpcode.NewArrE, EcsOpcode.GetI, EcsOpcode.Slice,
            EcsOpcode.NewSt, EcsOpcode.GetF, EcsOpcode.GetFI, EcsOpcode.Img,
        };
        foreach (var op in writesA)
            Assert.That(EcsFormat.ResultSlot(op), Is.EqualTo(EcsResultSlot.A), $"{op}");

        // 写 R[C]：调用接收槽（C=255 无接收）
        Assert.That(EcsFormat.ResultSlot(EcsOpcode.Call), Is.EqualTo(EcsResultSlot.C));
        Assert.That(EcsFormat.ResultSlot(EcsOpcode.CallN), Is.EqualTo(EcsResultSlot.C));

        // 不写结果槽：访存/副作用/控制流/返回
        var writesNone = new[]
        {
            EcsOpcode.Nop, EcsOpcode.StoreG, EcsOpcode.SetI, EcsOpcode.PutF,
            EcsOpcode.PutFI, EcsOpcode.Jmp, EcsOpcode.Jpt, EcsOpcode.Ret,
            EcsOpcode.Ret0, EcsOpcode.StickP, EcsOpcode.StickPv, EcsOpcode.WaitI,
        };
        foreach (var op in writesNone)
            Assert.That(EcsFormat.ResultSlot(op), Is.EqualTo(EcsResultSlot.None), $"{op}");
    }

    [Test]
    public void Table_CoversEveryOpcode()
    {
        // EcsFormat.BuildTable 的完整性自检（登记数 == 枚举数）在首次触表时抛出；
        // 此处逐值触表显式确认无遗漏、无抛出。
        foreach (var op in Enum.GetValues<EcsOpcode>())
            Assert.DoesNotThrow(() => EcsFormat.Get(op));
    }
}