using EasyCon.Core.Flow;
using System.CommandLine;

namespace EasyCon2.CLI;

/// <summary>
/// serve：启动 Flow 节点服务（HTTP，loopback）。
/// 前端（Python 画布）与外部 agent 经此访问：节点目录、设备管理、编排图运行。
/// </summary>
public static class ServeCommand
{
    public static Command Create()
    {
        var command = new Command("serve", "启动 Flow 节点服务（HTTP；前端画布/外部 agent 的后端）");
        var portOption = new Option<int>("--port")
        {
            Description = "监听端口",
            DefaultValueFactory = _ => 19391,
        };
        command.Options.Add(portOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var port = parseResult.GetValue(portOption);
            return await RunAsync(port, cancellationToken);
        });
        return command;
    }

    private static async Task<int> RunAsync(int port, CancellationToken ct)
    {
        var state = new FlowServiceState();
        await using var http = new FlowServiceHttp(state, port);
        try
        {
            await http.StartAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: Flow 服务启动失败（端口 {port}）: {ex.Message}");
            state.Dispose();
            return 1;
        }

        Console.WriteLine($"EasyCon Flow 服务已启动: {http.Url}");
        Console.WriteLine("  GET  /api/health                 健康检查");
        Console.WriteLine("  GET  /api/nodes                  节点目录（画布渲染的单一事实源）");
        Console.WriteLine("  GET  /api/options                服务选项（OCR 后端等）");
        Console.WriteLine("  POST /api/options                {ocrBackend, ocrModelDir}");
        Console.WriteLine("  GET  /api/device/video           视频源列表/连接状态");
        Console.WriteLine("  POST /api/device/video/connect   {index, api}");
        Console.WriteLine("  POST /api/device/video/disconnect");
        Console.WriteLine("  GET  /api/device/mcu             单片机端口/连接状态");
        Console.WriteLine("  POST /api/device/mcu/connect     {port}（mock = 虚拟手柄）");
        Console.WriteLine("  POST /api/device/mcu/disconnect");
        Console.WriteLine("  POST /api/flow/run               {json} 或 {path}");
        Console.WriteLine("  POST /api/flow/stop              {runId}");
        Console.WriteLine("  GET  /api/flow/status?runId=     运行状态/事件/节点计时");
        Console.WriteLine("  POST /api/node/run               单节点试跑 {type,params,inputs} 或 {graph,nodeId}");
        Console.WriteLine("按 Ctrl+C 退出。");

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        finally
        {
            state.Dispose();
        }
        return 0;
    }
}