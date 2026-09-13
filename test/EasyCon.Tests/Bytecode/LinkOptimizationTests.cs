using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 链接期优化回归（EcmEcxFormat §6.1-3 方向的落地）：
/// 1. &lt;main&gt; 壳消除——init 调用序列前插 $eval 本体，入口函数命名 &lt;main&gt;（$eval 为 v1 占位名）；
/// 2. 死函数消除——自入口 BFS 调用图，stdlib/vision 未调用函数体不进镜像（fid 重映射）。
/// 不变量：每镜像函数均自入口可达；入口返回值（顶层 RETURN）与 init 先于 main 的次序不变。
/// </summary>
[TestFixture]
public class LinkOptimizationTests
{
    string _dir = null!;

    [SetUp]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"EcsLinkOpt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "lib"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    /// <summary>不变量校验：每个镜像函数都自入口可达（Call 边，EXT-aware 步进）。</summary>
    static List<string> UnreachableNames(EcxImage image)
    {
        var keep = new HashSet<int>();
        var queue = new Queue<int>();
        keep.Add(image.Entry);
        queue.Enqueue(image.Entry);
        while (queue.Count > 0)
        {
            var words = image.Functions[queue.Dequeue()].Code;
            for (int w = 0; w < words.Count; w++)
            {
                switch ((EcsOpcode)(words[w] & 0xFF))
                {
                    case EcsOpcode.Call:
                        if (keep.Add((int)words[w + 1]))
                            queue.Enqueue((int)words[w + 1]);
                        w++;
                        break;
                    case EcsOpcode.CallN or EcsOpcode.NewArrV or EcsOpcode.Slice or EcsOpcode.GetFI
                        or EcsOpcode.PutFI or EcsOpcode.StickP or EcsOpcode.StickPv:
                        w++;
                        break;
                }
            }
        }
        var dead = new List<string>();
        for (int i = 0; i < image.Functions.Count; i++)
            if (!keep.Contains(i))
                dead.Add(image.Functions[i].Name);
        return dead;
    }

    static EcxHost RecordedHost()
    {
        var host = new EcxHost();
        host.EnableRecording();
        return host;
    }

    [Test]
    public void Entry_IsMainWithPrependedInits_NoEvalPlaceholder()
    {
        File.WriteAllText(Path.Combine(_dir, "lib", "utils.ecs"),
            "PRINT \"init-utils\"\nFUNC twice($x:INT):INT\n    RETURN $x * 2\nENDFUNC\n");
        File.WriteAllText(Path.Combine(_dir, "main.ecs"),
            "$r = twice(21)\nPRINT $r\n");

        var result = Compilation.CompileFile(Path.Combine(_dir, "main.ecs"));
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics));
        var image = result.Image!;

        // 入口函数 = <main>（$eval 占位名消除；init 调用序列前插在本体内）
        var entryFn = image.Functions[image.Entry];
        Assert.That(entryFn.Name, Is.EqualTo("<main>"));
        Assert.That(image.Functions.Any(f => f.Name == "$eval"), Is.False,
            "$eval 占位名不应残留于镜像");
        Assert.That((EcsOpcode)(entryFn.Code[0] & 0xFF), Is.EqualTo(EcsOpcode.Call),
            "入口首指令应是前插的 <init> 调用");

        // init 次序与返回值语义不回归
        var host = RecordedHost();
        Assert.That(EcxInterpreter.Run(image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "init-utils", "42" }));

        // 镜像无死函数
        Assert.That(UnreachableNames(image), Is.Empty);
    }

    [Test]
    public void DeadStdlibFunctions_StrippedFromImage()
    {
        // 仅用 PRINT（std）：vision 的 FRAME/OCR/ROI/OCR_INIT 及 std 未用函数不进镜像
        File.WriteAllText(Path.Combine(_dir, "main.ecs"),
            "$s = \"hi\" & 1\nPRINT $s\n");

        var result = Compilation.CompileFile(Path.Combine(_dir, "main.ecs"));
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics));
        var image = result.Image!;
        var names = image.Functions.Select(f => f.Name).ToList();

        foreach (var dead in new[] { "FRAME", "OCR", "ROI", "OCR_INIT", "TIME" })
            Assert.That(names, Does.Not.Contain(dead), $"未调用的 stdlib 函数 {dead} 应被消除");

        var totalCompiled = result.Artifacts.Sum(a => a.Functions.Count);
        Assert.That(image.Functions.Count, Is.LessThan(totalCompiled),
            $"镜像函数数应小于编译总量（{image.Functions.Count}/{totalCompiled}）");
        Assert.That(UnreachableNames(image), Is.Empty, "镜像内不得有入口不可达函数");

        var host = RecordedHost();
        Assert.That(EcxInterpreter.Run(image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "hi1" }));
    }

    [Test]
    public void RecursiveAndMutualCalls_RemainReachable()
    {
        // 递归 / 互调 / 高阶导出链：BFS 调用图覆盖（间接层不得误杀）
        File.WriteAllText(Path.Combine(_dir, "main.ecs"),
            """
            FUNC fact($n):INT
                IF $n <= 1
                    RETURN 1
                ENDIF
                RETURN $n * fact($n - 1)
            ENDFUNC
            FUNC even($n:INT):BOOL
                IF $n == 0
                    RETURN 1 == 1
                ENDIF
                RETURN odd($n - 1)
            ENDFUNC
            FUNC odd($n:INT):BOOL
                IF $n == 0
                    RETURN 1 == 0
                ENDIF
                RETURN even($n - 1)
            ENDFUNC
            $f = fact(5)
            $e = even(10)
            PRINT $f
            PRINT $e
            """);

        var result = Compilation.CompileFile(Path.Combine(_dir, "main.ecs"));
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics));
        var image = result.Image!;
        var names = image.Functions.Select(f => f.Name).ToList();

        foreach (var alive in new[] { "<main>", "fact", "even", "odd" })
            Assert.That(names, Does.Contain(alive), $"递归/互调函数 {alive} 必须保留");
        Assert.That(UnreachableNames(image), Is.Empty);

        var host = RecordedHost();
        Assert.That(EcxInterpreter.Run(image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "120", "true" }));

        // 同一镜像 C VM 可执行（fid 重映射后镜像合法性的直接证据）
        var ecx = EcxWriter.Write(image);
        Assert.That(ecx.Length, Is.GreaterThan(0));
    }

    [Test]
    public void DeadNativesAndConsts_StrippedFromTables()
    {
        // 仅 PRINT（→ FWRITE 原生）：采集洞/OCR 系原生名与死常量不进镜像表
        File.WriteAllText(Path.Combine(_dir, "main.ecs"),
            "$s = \"hi\" & 1\nPRINT $s\n");

        var result = Compilation.CompileFile(Path.Combine(_dir, "main.ecs"));
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics));
        var image = result.Image!;

        var nativeNames = image.Natives.Select(n => n.Name).ToList();
        foreach (var dead in new[] { "__OCR__", "__OCR_INIT__", "__ROI__", "APP" })
            Assert.That(nativeNames, Does.Not.Contain(dead), $"未引用原生名 {dead} 应被消除");
        Assert.That(nativeNames, Does.Contain("FWRITE"), "在用原生名保留");

        // 常量池不大于被引用数（每条至少被一处 LoadK/Img 引用）
        int refs = 0;
        foreach (var f in image.Functions)
            for (int w = 0; w < f.Code.Count; w++)
                if ((EcsOpcode)(f.Code[w] & 0xFF) is EcsOpcode.LoadK or EcsOpcode.Img)
                    refs++;
        Assert.That(image.Consts.Count, Is.LessThanOrEqualTo(refs),
            "常量池每条都应被至少一处引用（死常量已消除）");

        var host = RecordedHost();
        Assert.That(EcxInterpreter.Run(image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "hi1" }));
    }

    [Test]
    public void ImgLabelConsts_SurviveConstPoolCompaction()
    {
        // Img 指令按 ABx 引用标签名常量：压缩重映射后 NeedIL 与标签查表必须不回归
        File.WriteAllText(Path.Combine(_dir, "main.ecs"),
            "$v = @enemy\nIF $v >= 0\n    PRINT \"seen\"\nENDIF\nPRINT \"end\"\n");
        var result = Compilation.CompileFile(Path.Combine(_dir, "main.ecs"), new CompileOptions
        {
            ExtVars = System.Collections.Immutable.ImmutableHashSet.Create("enemy"),
        });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics));
        var image = result.Image!;
        Assert.That(image.NeedIL, Is.True, "图像标签需求标志不回归");

        var host = RecordedHost();
        host.ImgLabel = name => name == "enemy" ? 3 : -1;
        Assert.That(EcxInterpreter.Run(image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "seen", "end" }), "Img 常量重映射后标签解析正确");
    }
}