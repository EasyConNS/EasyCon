using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Flow;
using EasyCon.Core.Hosting;
using EasyCon.Script;
using EasyDevice;
using EasyScript;
using OpenCvSharp;
using System.CommandLine;

namespace EasyCon2.CLI;

/// <summary>
/// flow：执行编排图（*.flow.json）。图只在 PC 执行；
/// 采集源/单片机经 --video/--port 接入，能力装配走 ScriptHostAssembler 唯一装配点。
/// </summary>
public static class FlowCommand
{
    public static Command Create(NintendoSwitch ns)
    {
        var command = new Command("flow", "执行编排图（*.flow.json；节点式流程，PC 执行）");
        var fileArgument = new Argument<string>("file")
        {
            Description = "flow.json 图文件路径"
        };
        var portOption = new Option<string>("--port", "-p")
        {
            Description = "单片机端口（COM22 等；\"mock\" = 虚拟单片机）",
            DefaultValueFactory = _ => "mock",
        };
        var videoOption = new Option<int>("--video", "-v")
        {
            Description = "视频采集设备索引（-1 = 不接采集源）",
            DefaultValueFactory = _ => -1,
        };
        var videoTypeOption = new Option<VideoCaptureAPIs>("--videotype", "-vt")
        {
            DefaultValueFactory = _ => VideoCaptureAPIs.ANY
        };
        var maxStepsOption = new Option<int>("--max-steps")
        {
            Description = "看门狗：节点执行步数上限（覆盖图内配置）",
            DefaultValueFactory = _ => 0,
        };
        var timeoutOption = new Option<int>("--timeout-sec")
        {
            Description = "看门狗：总时长上限秒（覆盖图内配置）",
            DefaultValueFactory = _ => 0,
        };
        command.Arguments.Add(fileArgument);
        command.Options.Add(portOption);
        command.Options.Add(videoOption);
        command.Options.Add(videoTypeOption);
        command.Options.Add(maxStepsOption);
        command.Options.Add(timeoutOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var file = parseResult.GetValue(fileArgument)!;
            var port = parseResult.GetValue(portOption)!;
            var videoId = parseResult.GetValue(videoOption);
            var videoType = parseResult.GetValue(videoTypeOption);
            var maxSteps = parseResult.GetValue(maxStepsOption);
            var timeoutSec = parseResult.GetValue(timeoutOption);

            return await RunAsync(ns, file, port, videoId, videoType, maxSteps, timeoutSec, cancellationToken);
        });
        return command;
    }

    private static async Task<int> RunAsync(
        NintendoSwitch ns, string file, string port, int videoId, VideoCaptureAPIs videoType,
        int maxSteps, int timeoutSec, CancellationToken ct)
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"错误: 图文件不存在: {file}");
            return 1;
        }

        FlowGraph graph;
        try
        {
            graph = FlowGraph.LoadFile(file);
        }
        catch (FlowParseException ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }
        if (maxSteps > 0) graph.MaxSteps = maxSteps;
        if (timeoutSec > 0) graph.TimeoutSec = timeoutSec;

        // 采集源（可选）：与 run 命令同款 OpenCVCapture + FrameProducer
        OpenCVCapture? cvcap = null;
        FrameProducer? producer = null;
        if (videoId >= 0)
        {
            cvcap = new OpenCVCapture(videoId, videoType);
            if (!cvcap.Open(videoId, (int)videoType))
            {
                Console.Error.WriteLine($"错误: 视频源连接失败: [{videoId}] {videoType}");
                return 1;
            }
            cvcap.SetProperties(1920, 1080);
            producer = new FrameProducer(cvcap);
            producer.Start();
            Console.WriteLine($"视频源已连接: [{videoId}]");
        }

        // 单片机（可选）：mock = 虚拟手柄，否则连接真实端口
        var isMock = port.Equals("mock", StringComparison.OrdinalIgnoreCase);
        ICGamePad pad;
        if (isMock)
        {
            pad = new MockGamePad();
            Console.WriteLine("单片机: Mock 模式");
        }
        else
        {
            if (ns.TryConnect(port) != NintendoSwitch.ConnectResult.Success)
            {
                Console.Error.WriteLine($"错误: 单片机连接失败: {port}");
                producer?.Dispose();
                cvcap?.Dispose();
                return 1;
            }
            pad = new GamePadAdapter(ns);
            Console.WriteLine($"单片机已连接: {port}");
        }

        // 能力装配（唯一装配点）：OCR 默认值/释放由租约承担
        FrameDelegate? frameDelegate = producer != null
            ? FrameDelegateFactory.CreateFrame(() => producer.Store.AcquireLatest())
            : null;
        using var lease = ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            Pad = pad,
            Frame = frameDelegate,
            AppDir = AppDomain.CurrentDomain.BaseDirectory,
        });

        var context = new FlowHostContext
        {
            Capture = lease.Capabilities.Capture,
            Ocr = lease.Capabilities.Ocr,
            Pad = lease.Capabilities.Input,
            AppDir = AppDomain.CurrentDomain.BaseDirectory,
        };

        Console.WriteLine($"开始执行编排图: {graph.Name}（节点 {graph.Nodes.Count}，看门狗 {graph.MaxSteps} 步 / {graph.TimeoutSec}s）");
        var executor = new FlowExecutor(graph, context);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        FlowRunReport report;
        try
        {
            report = executor.Run(ct);
        }
        finally
        {
            producer?.Dispose();
            cvcap?.Dispose();
        }

        // 报告输出
        Console.WriteLine();
        Console.WriteLine($"== 执行{(report.Completed ? "完成" : report.WatchdogTriggered ? "被看门狗中止" : "出错")} ==");
        Console.WriteLine($"步数: {report.Steps}  总耗时: {report.TotalMs}ms");
        foreach (var r in report.Records.Values.OrderByDescending(r => r.TotalMs))
            Console.WriteLine($"  {r.NodeId,-14} {r.Type,-14} ×{r.ExecCount,-3} {r.TotalMs,8:0}ms");
        if (report.Error != null)
        {
            Console.Error.WriteLine($"错误节点: {report.ErrorNodeId}");
            Console.Error.WriteLine($"错误: {report.Error}");
            return 1;
        }
        return report.WatchdogTriggered ? 2 : 0;
    }
}