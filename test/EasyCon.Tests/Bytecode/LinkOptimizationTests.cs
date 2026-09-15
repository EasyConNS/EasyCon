using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 链接期优化回归（EcmEcxFormat §6.1-3 方向的落地）：
/// 1. &lt;main&gt; 壳消除——init 调用序列前插 $eval 本体，入口函数命名 &lt;main&gt;（$eval 为 v1 占位名）；
/// 2. 死函数消除——自入口 BFS 调用图，stdlib/vision 未调用函数体不进镜像（fid 重映射）；
/// 3. 死存储清扫——SSA φ 降级/变量落槽的边副本与无人读取的常量物化不进镜像（防线 1 编码期
///    死 φ 免槽位 + 防线 3 反向活跃性清扫，见 DeadStoreSweep）。
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
        // L2 全集编号化：PRINT/ALERT/ARG 全走 syscall 编号，原生名表为空；
        // 采集洞/FFI（未引用）不进表。语义经 EcxHost 参考处理器不变。
        File.WriteAllText(Path.Combine(_dir, "main.ecs"),
            "ALERT(\"boot\")\n$r = ARG(0)\nPRINT $r\n");

        var result = Compilation.CompileFile(Path.Combine(_dir, "main.ecs"));
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics));
        var image = result.Image!;

        // 名表只承载 L3（采集洞 "__xxx__" / FFI "库!导出名" / ENCODE / JQ）——本脚本全不引用
        Assert.That(image.Natives, Is.Empty, "L2 全集编号化后，纯内建脚本名表应为空");
        Assert.That(image.Features & EcsImageFeatures.File, Is.Not.Zero, "文件族 syscall → FILE");
        Assert.That(image.Features & (EcsImageFeatures.Ffi | EcsImageFeatures.Capture | EcsImageFeatures.Il),
            Is.Zero, "无 FFI/采集洞/IL 需求");

        // syscall 编号调用在镜像中直传旗标：FWRITE=1 / ALERT=10 / ARG=11
        var seen = new HashSet<int>();
        foreach (var f in image.Functions)
        {
            for (int w = 0; w < f.Code.Count; w++)
            {
                if ((EcsOpcode)(f.Code[w] & 0xFF) != EcsOpcode.CallN)
                    continue;
                if (w + 1 >= f.Code.Count)
                    break;
                uint ext = f.Code[w + 1];
                if ((ext & EcsSyscall.CallFlag) != 0)
                    seen.Add((int)(ext & 0x7FFFFFFFu));
                w++;
            }
        }
        Assert.That(seen, Is.SupersetOf(new[] { EcsSyscall.FWrite, EcsSyscall.Alert, EcsSyscall.Arg }),
            "PRINT/ALERT/ARG 应发对应编号的 syscall CallN");

        // 常量池不大于被引用数（每条至少被一处 LoadK/Img 引用）
        int refs = 0;
        foreach (var f in image.Functions)
            for (int w = 0; w < f.Code.Count; w++)
                if ((EcsOpcode)(f.Code[w] & 0xFF) is EcsOpcode.LoadK or EcsOpcode.Img)
                    refs++;
        Assert.That(image.Consts.Count, Is.LessThanOrEqualTo(refs),
            "常量池每条都应被至少一处引用（死常量已消除）");

        var host = RecordedHost();
        host.Args = new[] { "x" };
        Assert.That(EcxInterpreter.Run(image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Is.EqualTo(new[] { "x" }), "syscall 与名表原生混用语义不变");
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

    [Test]
    public void DeadStore_Guangshu_NoDeadHomes_NoDeadPhiSlots()
    {
        // 防线 1 + 防线 3 以真实例程锁定（docs/Pipeline.md）：
        // 出口边死 φ 免槽位分配；FOR 迭代变量 home（SetVar）与死 φ 边副本被反向活跃性清扫
        var path = CorpusAssert.ExamplePath("光速过帧v1.4精准版.txt");
        if (path.Length == 0)
            Assert.Ignore("例程文件不存在");
        var result = Compilation.CompileFile(path, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var image = result.Image!;
        var main = image.Functions.Single(f => f.Name == "<main>");

        Assert.That(image.MaxSlots, Is.LessThanOrEqualTo(19), "死 φ 槽应免分配（22 基线 − 3 出口边死 φ）");

        int setVar = 0, move = 0;
        for (int w = 0; w < main.Code.Count;)
        {
            var op = (EcsOpcode)(main.Code[w] & 0xFF);
            if (op == EcsOpcode.SetVar) setVar++;
            if (op == EcsOpcode.Move) move++;
            w += EcsFormat.WordCount(op);
        }
        Assert.That(setVar, Is.EqualTo(0), "FOR 迭代变量 home 槽全函数无读取者，SetVar 应被清扫");
        Assert.That(move, Is.LessThanOrEqualTo(15), "出口边死副本与无人读取的落槽应被清扫（编码期 23 条基线）");

        Assert.That(EcxWriter.Write(image).Length, Is.LessThan(600), "死存储清除后镜像应 < 600B（清扫前 604B）");

        // 语义不变由 FullChain/CvmCross 双端对拍锁定；此处锁解释器可执行到底
        var host = RecordedHost();
        Assert.That(EcxInterpreter.Run(image, host), Is.EqualTo(0));
    }
}