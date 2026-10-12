using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;
using OpenCvSharp;
using System.Collections.Immutable;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// 真模型端到端：ResNet18（ImageNet-1k，onnx-community/resnet-18-ONNX）× OpenCV DNN 后端。
/// V1 分类链路真机验证：NET_IMAGE_FILE("rgbc") → NET_SCALE(1/255) → NET_NORM(ImageNet
/// 均值方差) → NET_RUNH → NET_ARGMAX（[1,1000] 二维修正）→ NET_LABELS/NET_LABEL → 类名。
/// 测试图复用 yolo26/bus.jpg（黄色校巴，ImageNet class 779 = "school bus"）。
/// 依赖 test/assets/resnet/（约定见 test/assets/README.md；缺失 → 跳过）。
/// </summary>
[TestFixture]
[Category("Manual")]
[Explicit]   // OpenCV 5 alpha macOS 原生间歇崩溃——点名 filter 跑
public class NetResnetRealModelTests
{
    [Test]
    public void Resnet18_BusImage_ClassifiesWithLabels()
    {
        var assets = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "test", "assets"));
        var modelPath = Path.Combine(assets, "resnet", "resnet18.onnx");
        var labelsPath = Path.Combine(assets, "resnet", "imagenet_classes.txt");
        var imagePath = Path.Combine(assets, "yolo26", "bus.jpg");
        if (!File.Exists(modelPath) || !File.Exists(labelsPath) || !File.Exists(imagePath))
            Assert.Ignore($"缺真机资产（test/assets/README.md）：{modelPath} / {labelsPath} / {imagePath}");

        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-resnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(appDir, "models", "resnet"));
        var link = Path.Combine(appDir, "models", "resnet", "resnet18.onnx");
        try
        {
            if (!File.Exists(link)) File.CreateSymbolicLink(link, modelPath);
        }
        catch (UnauthorizedAccessException e)
        {
            Assert.Ignore($"无法创建 symlink（Windows 需开发者模式/管理员权限）：{e.Message}");
        }

        // 图像/标签路径经 ARG(0)/ARG(1) 注入（路径含空格不内插）；bus.jpg 竖图走 rgbc cover 裁剪
        const string source = """
            $net = NET_LOAD(__APP__ + "/models/resnet/resnet18.onnx")
            PRINT $net
            $t = NET_SCALE(NET_IMAGE_FILE(ARG(0), 224, 224, "rgbc"), 1.00 / 255, 0)
            $t = NET_NORM($t, [0.485, 0.456, 0.406], [0.229, 0.224, 0.225])
            $n = NET_RUNH($net, $t)
            PRINT $n
            $rows = NET_ROWS()
            PRINT $rows
            $cls = NET_ARGMAX(0)
            PRINT $cls
            $cnt = NET_LABELS(ARG(1))
            PRINT $cnt
            $name = NET_LABEL($cls)
            PRINT $name
            $sc = INT(NET_OUT($cls) * 10.00 + 0.5)
            PRINT $sc
            """;

        var engine = new EasyScriptEngine();
        var session = engine.FromSource(source,
            new ScriptHostOptions
            {
                Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false },
            });
        Assert.That(session.Info.Diagnostics.Where(d => d.IsError), Is.Empty, "脚本必须编译通过");

        var io = new RecordingIo();
        using var inference = new DnnInference();
        var caps = new CapabilitySet
        {
            Console = new ConsoleIoAdapter(io),
            Environment = new HostEnvironment([imagePath, labelsPath], appDir),
            Inference = inference,
        };
        session.Run(new CancellationTokenSource().Token, caps);

        string[] lines = io.Lines.ToArray();
        TestContext.Out.WriteLine("脚本输出：" + string.Join(" | ", lines));

        // top-1 允许 minibus(654)/school bus(779) 之一——bus.jpg 的黄色大巴两种语义均合理，
        // 不同 ResNet 导出的偏好不同；关键在类名与 class id 经标签表一致对应。
        int cls = int.Parse(lines[3]);
        var plausible = new Dictionary<int, string> { [654] = "minibus", [779] = "school bus" };

        Assert.Multiple(() =>
        {
            Assert.That(int.Parse(lines[0]), Is.GreaterThanOrEqualTo(0), "NET_LOAD（DNN 解析 transformers 导出图）");
            Assert.That(lines[1], Is.EqualTo("1000"), "输出元素数 [1,1000]");
            Assert.That(lines[2], Is.EqualTo("1"), "NET_ROWS 末维修正：向量输出 1 行");
            Assert.That(plausible.ContainsKey(cls), Is.True, $"top-1 class 应为校巴语义（实得 {cls}）");
            Assert.That(lines[4], Is.EqualTo("1000"), "标签表 1000 条（行号即索引，无尾换行无幻影）");
            Assert.That(lines[5], Is.EqualTo(plausible[cls]), "NET_LABEL(cls) → 类名与 top-1 一致");
            Assert.That(int.Parse(lines[6]), Is.GreaterThan(50),
                "清晰校巴图 top-1 logit 应显著（>5.0）");
        });
    }
}
