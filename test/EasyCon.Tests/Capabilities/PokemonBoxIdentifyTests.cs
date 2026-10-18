using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Tests.Support;
using OpenCvSharp;
using System.Collections.Immutable;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// projects/pokemon-box 真实脚本项目端到端（Manual；OpenCV 5 alpha macOS 原生间歇崩溃
/// 同 NetPpocrRealModelTests——点名 filter 跑）。验证图经环境变量注入，仓库零硬编码用户路径：
///
///   POKEMON_BOX_IMAGES  分号分隔的 png 路径列表（必需，缺失 → Ignore）
///   例：POKEMON_BOX_IMAGES="/a/pokemon.png;/b/QQ20261018-171958.png"
///
/// 脚本模型/字典为项目相对路径（models/ppocr/*，运行约定 = CWD 在脚本项目目录），
/// 帧固定 1920x1080、脚本不接收参数。测试把 CWD 切到项目目录编译执行，结束恢复；
/// lib/ 隐式导入以源文件路径为基准解析（FromSource 带 fileName 重载）。
/// </summary>
[TestFixture]
[Category("Manual")]
[Explicit]
public class PokemonBoxIdentifyTests
{
    static string ProjectDir => Path.GetFullPath(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "examples", "pokemon-box"));

    [Test]
    public void Identify_FromEnvImages()
    {
        var env = Environment.GetEnvironmentVariable("POKEMON_BOX_IMAGES");
        if (string.IsNullOrWhiteSpace(env))
            Assert.Ignore("未设置 POKEMON_BOX_IMAGES（分号分隔 png 路径），跳过");

        var images = env.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.That(images, Is.Not.Empty, "POKEMON_BOX_IMAGES 为空");

        var recPath = Path.Combine(ProjectDir, "models", "ppocr", "PP-OCRv6_medium_rec.onnx");
        var dictPath = Path.Combine(ProjectDir, "models", "ppocr", "ppocrv6_dict.txt");
        if (!File.Exists(recPath) || !File.Exists(dictPath))
            Assert.Ignore($"缺脚本项目模型资产（projects/pokemon-box/models/README.md）：{recPath}");

        var scriptPath = Path.Combine(ProjectDir, "main.ecs");
        var source = File.ReadAllText(scriptPath);

        // 脚本内模型/字典均为项目相对路径——CWD 切到项目目录（finally 恢复）
        var oldCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(ProjectDir);
        try
        {
            foreach (var imagePath in images)
            {
                using var frame = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (frame.Empty())
                {
                    TestContext.Out.WriteLine($"[SKIP] {imagePath} 解码失败");
                    continue;
                }

                // 脚本按固定 1920x1080 采集帧设计——验证图统一缩放到该尺寸模拟真实采集
                Cv2.Resize(frame, frame, new Size(1920, 1080));

                var engine = new EasyScriptEngine();
                // fileName 传真实路径：lib/ 自动加载与导入基准 = 源文件所在目录
                var session = engine.FromSource(source, scriptPath, new ScriptHostOptions
                {
                    Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false },
                });
                Assert.That(session.Info.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
                    $"{Path.GetFileName(imagePath)} 脚本编译失败");

                var io = new RecordingIo();
                using var inference = new DnnInference();
                var caps = new CapabilitySet
                {
                    Console = new ConsoleIoAdapter(io),
                    Environment = new HostEnvironment([], ProjectDir),
                    Inference = inference,
                    Capture = new DelegateCaptureSource(FrameDelegateFactory.CreateFrame(() => frame.Clone())),
                };
                session.Run(new CancellationTokenSource().Token, caps);

                TestContext.Out.WriteLine($"=== {Path.GetFileName(imagePath)} ({frame.Width}x{frame.Height}) ===");
                TestContext.Out.WriteLine(string.Join("\n", io.Lines));
                Assert.That(io.Lines, Does.Contain("done"), $"{imagePath} 脚本应正常跑完");
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(oldCwd);
        }
    }
}