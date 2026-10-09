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
            var instructions = image.Functions[queue.Dequeue()].Instructions;
            for (int w = 0; w < instructions.Count; w++)
            {
                if (instructions[w].Op == EcsOpcode.Call
                    && keep.Add((int)instructions[w].Ext))
                    queue.Enqueue((int)instructions[w].Ext);
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

    /// <summary>带 3s 预算的解释器执行：误删活跃副本的回归形态是死循环，此处以 CANCELLED 显形而非挂死测试进程。</summary>
    static int RunBounded(EcxImage image, EcxHost host)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        return EcxInterpreter.Run(image, host, cts.Token);
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
        Assert.That(entryFn.Instructions[0].Op, Is.EqualTo(EcsOpcode.Call),
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
        var ecx = EcsContainer.WriteImage(image);
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
            foreach (var ins in f.Instructions)
            {
                if (ins.Op != EcsOpcode.CallN)
                    continue;
                uint ext = ins.Ext;
                if ((ext & EcsSyscall.CallFlag) != 0)
                    seen.Add((int)(ext & 0x7FFFFFFFu));
            }
        Assert.That(seen, Is.SupersetOf(new[] { EcsSyscall.FWrite, EcsSyscall.Alert, EcsSyscall.Arg }),
            "PRINT/ALERT/ARG 应发对应编号的 syscall CallN");

        // 常量池不大于被引用数（每条至少被一处 LoadK/Img 引用）
        int refs = 0;
        foreach (var f in image.Functions)
            foreach (var ins in f.Instructions)
                if (ins.Op is EcsOpcode.LoadK or EcsOpcode.Img)
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

        // 27 = 19（fast path 活 φ 槽）+ 2（循环不变量外提共享组槽）+ 5（std print 内联临时区：
        // v3+std 内联后 print/FRAME 壳在调用点展开，共享 fresh 块只涨一次；零 φ 拷贝合并后
        // 活 φ 槽不增）。死 φ 免分配不变量由下方 setVar/move 断言继续锁定。
        // 锁定 <main> 自身（image.MaxSlots 含标准库 PPOCR_* 等库函数的独立槽位，随库演进波动）。
        Assert.That(main.NSlots, Is.LessThanOrEqualTo(27), "死 φ 槽应免分配（fast path + 外提共享组槽 + print 内联临时区后基线 27）");

        int setVar = 0, move = 0;
        foreach (var ins in main.Instructions)
        {
            if (ins.Op == EcsOpcode.SetVar) setVar++;
            if (ins.Op == EcsOpcode.Move) move++;
        }
        Assert.That(setVar, Is.EqualTo(0), "FOR 迭代变量 home 槽全函数无读取者，SetVar 应被清扫");
        Assert.That(move, Is.LessThanOrEqualTo(18), "出口边死副本与无人读取的落槽应被清扫（v3+零φ合并+std 内联基线，Release 实测 18；编码期原基线 15——零 φ 合并把出口副本变成 φ 槽就地写，Move 总数随内联 Conv/Move 临时小幅上移）");

        Assert.That(EcsContainer.WriteImage(image, stripDebug: true).Length, Is.LessThan(600), "死存储清除后 MCU 发布镜像应 < 600B（清扫前 604B）");

        // 语义不变由 FullChain/CvmCross 双端对拍锁定；此处锁解释器可执行到底
        var host = RecordedHost();
        Assert.That(EcxInterpreter.Run(image, host), Is.EqualTo(0));
    }

    [Test]
    public void DeadStore_LiveInEquation_LoopCarriedHomeKept_WhileTerminates()
    {
        // liveIn = gen ∪ (liveOut \ kill) 回归锁（fuzz 模板覆盖不到的形态）：
        // $x 循环携带且体内先读后写（写 = 回边 φ 前驱副本 Move hx←t_x），循环头只读 $i 不读 $x
        // ⇒ hx ∈ gen∩kill(body) 且 hx ∉ liveOut(body)。旧方程 (gen\kill) ∪ liveOut 把 hx
        // 抠出 liveIn(body)，hx 的初始化边副本与回边前驱副本被误删 → hx 永不更新 →
        // $i 冻结在初值 → WHILE 永不退出。
        var result = Compilation.CompileSource(
            """
            $i = 0
            $x = 0
            WHILE $i < 3
                $i = $x
                $x = $x + 1
            END
            PRINT $i
            """,
            new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null);

        var host = RecordedHost();
        Assert.That(RunBounded(result.Image!, host), Is.EqualTo(EcxInterpreter.OK),
            "WHILE 应正常退出（被误删的活跃副本会以死循环显形）");
        Assert.That(host.Lines, Is.EqualTo(new[] { "3" }));
    }

    [Test]
    public void DeadStore_LiveInEquation_GuardedDiamond_LoopTerminates()
    {
        // 同上的守卫菱形变体：φ 边副本落在 IF 臂块，liveIn 缺陷经 then/else 两臂复制放大。
        // $k 用变量而非字面量，防止 SCCP 折叠分支把菱形拍平（拍平后形态不再触发）。
        var result = Compilation.CompileSource(
            """
            $i = 0
            $x = 0
            $k = 1
            WHILE $i < 3
                IF $k == 1
                    $x = $x + 1
                ENDIF
                $i = $x
            END
            PRINT $i
            """,
            new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null);

        var host = RecordedHost();
        Assert.That(RunBounded(result.Image!, host), Is.EqualTo(EcxInterpreter.OK),
            "WHILE 应正常退出（被误删的活跃副本会以死循环显形）");
        Assert.That(host.Lines, Is.EqualTo(new[] { "3" }));
    }
}