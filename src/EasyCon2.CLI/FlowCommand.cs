using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Flow;
using EasyCon.Core.Hosting;
using EasyCon.Core.Maa;
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
    public static Command Create(MaaHostSession host)
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

            return await RunAsync(host, file, port, videoId, videoType, maxSteps, timeoutSec, cancellationToken);
        });
        return command;
    }

    private static async Task<int> RunAsync(
        MaaHostSession host, string file, string port, int videoId, VideoCaptureAPIs videoType,
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

        if (videoId >= 0 && !host.Frames.Connect(videoId, videoType))
        {
            Console.Error.WriteLine($"错误: 视频源连接失败: [{videoId}] {videoType}");
            return 1;
        }
        bool isMock = port.Equals("mock", StringComparison.OrdinalIgnoreCase);
        bool requiresInput = FlowRequirements.RequiresInput(graph, AppDomain.CurrentDomain.BaseDirectory);
        if (requiresInput && !isMock && host.Controller.TryConnect(port) != ControllerConnectResult.Success)
        {
            Console.Error.WriteLine($"错误: 单片机连接失败: {port}");
            return 1;
        }
        Console.WriteLine($"开始执行编排图: {graph.Name}（节点 {graph.Nodes.Count}，看门狗 {graph.MaxSteps} 步 / {graph.TimeoutSec}s）");
        FlowRunReport report = null!;
        host.Runs.Run("flow", Path.GetFullPath(file), requiresInput && !isMock, ct, scope =>
        {
            using CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
            {
                PadInput = isMock ? new PadInputAdapter(new MockGamePad()) : scope.CreatePad(),
                CaptureSource = host.Frames.CaptureConnected ? scope.WrapCapture(new FrameHubCaptureSource(host.Frames)) : null,
                AppDir = AppDomain.CurrentDomain.BaseDirectory,
            });
            FlowHostContext context = new()
            {
                Capture = lease.Capabilities.Capture,
                Ocr = lease.Capabilities.Ocr,
                Inference = lease.Capabilities.Inference,
                Pad = lease.Capabilities.Input,
                AppDir = AppDomain.CurrentDomain.BaseDirectory,
            };
            report = new FlowExecutor(graph, context).Run(scope.Token);
            if (report.Error != null) scope.MarkFailed("FlowError", report.ErrorNodeId);
            if (report.WatchdogTriggered) scope.MarkFailed("Deadline", report.ErrorNodeId);
        });
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