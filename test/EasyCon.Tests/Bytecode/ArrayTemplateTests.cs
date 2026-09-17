using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;
using System.Text;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 全常量数组字面量的常量模板降级（BytecodeEncoder.ArrayTemplate）：
/// 大字面量不再把元素物化进帧槽（19199 槽超 ISA 255 上限的成因形态），
/// 改为「构建一次进隐藏全局模板 + 初始化点深拷贝」，仅用既有指令；
/// 小字面量保持内联 staging+NewArrV 路径。
/// 注意脚本语言约束（docs/Script.md）：数组字面量元素只能是立即数；行首 PRINT
/// 走特殊词法（仅 $变量/&/字符串），下标读取需先落入临时变量。
/// </summary>
[TestFixture]
public class ArrayTemplateTests
{
    static CompileResult Compile(string source)
    {
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(),
            Is.Empty, "编译诊断：" + string.Join("\n", result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null);
        return result;
    }

    static List<string> RunChain(CompileResult result, RecordingIo io)
    {
        EcxVm.Run(result.Image!, EcsTestHost.Capabilities(io, new RecordingPad()),
            new CancellationTokenSource().Token, [], result.NativeSymbols);
        return io.Lines;
    }

    static List<string> Run(CompileResult result)
    {
        var io = new RecordingIo();
        RunChain(result, io);
        return io.Lines;
    }

    static int HiddenGlobalCount(CompileResult result)
        => result.Image!.Globals.Count(g => g.Name.StartsWith("#tpl", StringComparison.Ordinal));

    static string Literal(int count, int first = 0)
        => "[" + string.Join(", ", Enumerable.Range(first, count)) + "]";

    // ---------- 槽位：大字面量不再撞 ISA 255 上限 ----------

    [Test]
    public void LargeLiteral_SlotsStaySmall()
    {
        // 19199 事故形态：单函数 9600 元素字面量曾产生 19199 槽
        var sb = new StringBuilder();
        sb.Append("$big = ").AppendLine(Literal(9600));
        var result = Compile(sb.ToString());
        Assert.That(result.Image!.MaxSlots, Is.LessThanOrEqualTo(255),
            "帧槽位与字面量长度无关（staging 分块区固定 ~128+尾槽）");
        Assert.That(EcxWriter.Write(result.Image), Is.Not.Empty, "镜像应可过 MCU 冻结格式序列化");
    }

    [Test]
    public void ThresholdBoundary_EightElementsUseTemplate_SevenStayInline()
    {
        var eight = Compile($"$a = {Literal(8)}");
        Assert.That(HiddenGlobalCount(eight), Is.GreaterThanOrEqualTo(2), "≥8 全常量元素走模板路径");

        var seven = Compile($"$a = {Literal(7)}");
        Assert.That(HiddenGlobalCount(seven), Is.EqualTo(0), "<8 元素保持内联路径");
    }

    // ---------- 语义：深拷贝隔离与内容正确 ----------

    [Test]
    public void Template_FreshMutableCopyPerCall()
    {
        const string source = """
            FUNC probe(): INT
                $a = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
                $first = $a[0]
                $a[0] = 100
                RETURN $first
            ENDFUNC
            $r1 = probe()
            PRINT $r1
            $r2 = probe()
            PRINT $r2
            $b = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
            $c = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
            $b[0] = 77
            $t = $c[0]
            PRINT $t
            """;
        var lines = Run(Compile(source));
        Assert.That(lines, Is.EqualTo(new[] { "1", "1", "1" }), "每次初始化得到独立可变副本（模板共享不可外泄）");
    }

    [Test]
    public void Template_InLoop_EachIterationIndependent()
    {
        const string source = """
            $i = 0
            WHILE $i < 3
                $a = [5, 6, 7, 8, 9, 10, 11, 12]
                $t = $a[0]
                PRINT $t
                $a[0] = 0
                $i += 1
            END
            """;
        var lines = Run(Compile(source));
        Assert.That(lines, Is.EqualTo(new[] { "5", "5", "5" }), "循环体内每次声明都是全新副本");
    }

    [Test]
    public void Template_ContentAndTypes_StringDoubleElements()
    {
        const string source = """
            $strs = ["a", "b", "c", "d", "e", "f", "g", "h", "i"]
            $dbl = [1.5, 2.5, 3.5, 4.5, 5.5, 6.5, 7.5, 8.5]
            $head = $strs[0] & $strs[8]
            $mid = $dbl[1]
            PRINT $head & "|" & $mid
            $s = $dbl[0] + $dbl[7]
            PRINT $s
            """;
        var lines = Run(Compile(source));
        Assert.That(lines[0], Is.EqualTo("ai|2.5"));
        Assert.That(lines[1], Is.EqualTo("10"));
    }

    [Test]
    public void Template_LargeLiteral_SumMatches()
    {
        var sb = new StringBuilder();
        sb.Append("$big = ").AppendLine(Literal(9600));
        sb.Append("""
            $sum = 0
            $i = 0
            WHILE $i < 9600
                $sum += $big[$i]
                $i += 1
            END
            PRINT $sum
            """);
        var lines = Run(Compile(sb.ToString()));
        Assert.That(lines[0], Is.EqualTo("46075200"), "9600 元素 0..9599 求和");
    }

    [Test]
    public void Template_LargeLiteral_RaggedTailChunk_SumMatches()
    {
        // 130 = 128 + 2：覆盖多分块 + 残块尾的缝合路径（count = min(chunkSize, remaining)）
        var sb = new StringBuilder();
        sb.Append("$big = ").AppendLine(Literal(130));
        sb.Append("""
            $sum = 0
            $i = 0
            WHILE $i < 130
                $sum += $big[$i]
                $i += 1
            END
            PRINT $sum
            """);
        var lines = Run(Compile(sb.ToString()));
        Assert.That(lines[0], Is.EqualTo("8385"), "130 元素 0..129 求和（128 整块 + 2 残块）");
    }

    // ---------- 路径分离与模板复用 ----------

    [Test]
    public void IdenticalLiterals_ShareOneTemplate()
    {
        const string source = """
            $a = [1, 2, 3, 4, 5, 6, 7, 8]
            $b = [1, 2, 3, 4, 5, 6, 7, 8]
            $c = [9, 8, 7, 6, 5, 4, 3, 2]
            $t = $a[7] + $b[7] + $c[0]
            PRINT $t
            """;
        var result = Compile(source);
        Assert.That(HiddenGlobalCount(result), Is.EqualTo(4), "相同内容共用一对模板/守卫全局，不同内容各一对");
        var lines = Run(result);
        Assert.That(lines[0], Is.EqualTo("25"));
    }

    // ---------- ECM 缓存往返：隐藏全局与构建段跨序列化存活 ----------

    [Test]
    public void Template_SurvivesEcmRoundtrip()
    {
        const string source = """
            FUNC probe(): INT
                $a = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
                $first = $a[0]
                $a[0] = 100
                RETURN $first
            ENDFUNC
            $r = probe()
            PRINT $r
            """;
        var result = Compile(source);
        var restored = result.Artifacts.Select(a => EcmFormat.Read(EcmFormat.Write(a))).ToList();
        var image = EcxPipeline.Link(restored, restored.Any(a => a.KeyAction), restored.Any(a => a.NeedIL));
        var io = new RecordingIo();
        var host = new EcxHost();
        host.Print = (s, _) => io.Lines.Add(s);
        var code = EcxInterpreter.Run(image, host);
        Assert.That(code, Is.EqualTo(0), $"往返后执行失败：ECS 错误码 {code}");
        Assert.That(io.Lines, Is.EqualTo(new[] { "1" }));
    }

    // ---------- EcxWriter 守卫：槽位超上限响亮失败（不静默截断） ----------

    [Test]
    public void EcxWriter_RejectsOversizedSlots()
    {
        var image = new EcxImage
        {
            Functions =
            [
                new EcsFunction
                {
                    Name = "big",
                    Module = "m",
                    NParams = 0,
                    NSlots = 300,
                    HasReturn = false,
                    Code = [(uint)EcsOpcode.Ret0],
                },
            ],
            Entry = 0,
        };
        Assert.Multiple(() =>
        {
            Assert.That(() => EcxWriter.Write(image), Throws.TypeOf<BytecodeException>(),
                "FuncDef.nslots 为 u8，超上限必须拒绝");
            image.MaxSlots = 300;
            Assert.That(() => EcxWriter.Write(image), Throws.TypeOf<BytecodeException>(),
                "头部 max_slots 为 u8，超上限必须拒绝");
        });
    }
}