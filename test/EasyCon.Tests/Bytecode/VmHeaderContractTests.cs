using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;
using System.Text.RegularExpressions;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// ecs_vm.h ↔ EcsOpcode/EcsSyscall/EcsImageFeatures 契约锁定（无需 cc，全平台可跑）：
/// C 侧枚举按声明顺序隐式对齐 C# 数值（无显式赋值、无编译期联动），历史上只靠 AbiContract
/// 差分测试锁值——而那组测试依赖 cc，在无编译器环境恒为 Ignore。本测试直接解析头文件
/// 声明序与 #define 值做机械比对，opcode/syscall/特征位任何一端漂移都显式失败。
/// </summary>
[TestFixture]
public class VmHeaderContractTests
{
    static string HeaderText()
    {
        var nativeDir = CvmRunner.FindNativeDir();
        Assert.That(nativeDir, Is.Not.Null, "未找到 ecs_vm.h 源目录（仓库布局变更？）");
        return File.ReadAllText(Path.Combine(nativeDir!, "ecs_vm.h"));
    }

    /// <summary>截取首个 enum { ... }; 块文本（enum 体内无嵌套花括号）。</summary>
    static string FirstEnumBody(string header, string startMarker)
    {
        var start = header.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"未找到 {startMarker}");
        var open = header.IndexOf("enum {", start, StringComparison.Ordinal);
        Assert.That(open, Is.GreaterThanOrEqualTo(0), $"{startMarker} 后未找到 enum");
        var close = header.IndexOf("};", open, StringComparison.Ordinal);
        Assert.That(close, Is.GreaterThanOrEqualTo(0), "enum 未闭合");
        return header[open..close];
    }

    static List<string> TokensInOrder(string body, string prefix)
    {
        var tokens = new List<string>();
        foreach (Match m in Regex.Matches(body, Regex.Escape(prefix) + @"[A-Za-z0-9_]+"))
            tokens.Add(m.Value[prefix.Length..]);
        return tokens;
    }

    [Test]
    public void Opcode_DeclarationOrder_MatchesCSharp()
    {
        var body = FirstEnumBody(HeaderText(), "操作码（与 C# EcsOpcode");
        var cNames = TokensInOrder(body, "OP_");
        var csNames = new List<string>();
        foreach (var value in Enum.GetValues<EcsOpcode>())
            csNames.Add(value.ToString());

        Assert.That(cNames, Is.Not.Empty, "头文件未解析出操作码");
        Assert.That(cNames, Is.EqualTo(csNames),
            "ecs_vm.h 的 OP_* 声明序与 C# EcsOpcode 漂移：两份枚举按声明顺序隐式对齐，" +
            "请同步修改两侧（新增指令时先加 C# EcsOpcode，再在同序位置加 C 侧 OP_*）。");
        Assert.That((int)Enum.GetValues<EcsOpcode>().Max(), Is.EqualTo(cNames.Count - 1),
            "EcsOpcode 最大数值应等于声明数-1（隐式连续编号）");
    }

    [Test]
    public void Opcode_ExtSet_MatchesCSharpFormatTable()
    {
        var body = FirstEnumBody(HeaderText(), "操作码（与 C# EcsOpcode");
        var cNames = TokensInOrder(body, "OP_");
        // C 侧 ecs_op_words 的 2 字指令白名单（与 C# EcsFormat.ExtWords 对应）
        var cExt = new List<string>();
        var wordsBody = HeaderText()[HeaderText().IndexOf("ecs_op_words", StringComparison.Ordinal)..];
        foreach (Match m in Regex.Matches(wordsBody[..wordsBody.IndexOf("default:", StringComparison.Ordinal)], @"case OP_([A-Za-z0-9_]+)"))
            cExt.Add(m.Groups[1].Value);

        var csExt = new List<string>();
        foreach (var value in Enum.GetValues<EcsOpcode>())
            if (EcsFormat.ExtWords(value) > 0)
                csExt.Add(cNames[(int)value]);

        Assert.That(cExt, Is.EqualTo(csExt), "EXT 后随数据字集合双端不一致");
    }

    [Test]
    public void Syscall_Numbering_MatchesCSharp()
    {
        var body = FirstEnumBody(HeaderText(), "文件族 syscall 编号");
        var cNames = TokensInOrder(body, "ECS_SYSCALL_");
        // C# 侧 Names[1..] 与编号 1..N 一一对应（Names[0] 为空占位）
        var csNames = EcsSyscall.Names.Skip(1).ToList();

        Assert.That(cNames, Is.Not.Empty, "头文件未解析出 syscall");
        Assert.That(cNames, Is.EqualTo(csNames),
            "ecs_vm.h 的 ECS_SYSCALL_* 声明序与 C# EcsSyscall.Names 漂移：ABI 编号 = 声明序（1 起），同步时不得改变既有序。");
    }

    static uint ParseHexDefine(Match m)
        => uint.Parse(m.Groups[1].Value.TrimEnd('u').Replace("0x", ""), System.Globalization.NumberStyles.HexNumber);

    [Test]
    public void Syscall_CallFlag_MatchesCSharp()
    {
        var header = HeaderText();
        var m = Regex.Match(header, @"#define\s+ECS_SYSCALL_FLAG\s+(0x[0-9A-Fa-f]+u?)");
        Assert.That(m.Success, Is.True, "未找到 ECS_SYSCALL_FLAG 定义");
        Assert.That(ParseHexDefine(m), Is.EqualTo(EcsSyscall.CallFlag), "syscall 旗标位不一致");
    }

    [Test]
    public void ImageFeatures_MatchCSharp()
    {
        var header = HeaderText();
        var csFeats = new (string CName, uint CsValue)[]
        {
            ("ECS_FEAT_IL", EcsImageFeatures.Il),
            ("ECS_FEAT_CAPTURE", EcsImageFeatures.Capture),
            ("ECS_FEAT_FFI", EcsImageFeatures.Ffi),
            ("ECS_FEAT_FILE", EcsImageFeatures.File),
        };
        foreach (var (cName, csValue) in csFeats)
        {
            var m = Regex.Match(header, @"#define\s+" + cName + @"\s+(0x[0-9A-Fa-f]+u?)");
            Assert.That(m.Success, Is.True, $"未找到 {cName} 定义");
            Assert.That(ParseHexDefine(m), Is.EqualTo(csValue),
                $"{cName} 数值与 C# EcsImageFeatures 漂移");
        }
    }
}