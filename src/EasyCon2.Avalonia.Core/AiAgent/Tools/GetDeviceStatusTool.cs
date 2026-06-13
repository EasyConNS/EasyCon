using System.Text.Json;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// get_device_status 工具：返回设备/视频源/手柄连接状态及脚本运行状态。
/// </summary>
public class GetDeviceStatusTool : IAiTool
{
    private readonly IToolCallService _service;

    public GetDeviceStatusTool(IToolCallService service) => _service = service;

    public string Name => "get_device_status";

    public string Description => "获取当前设备连接状态：单片机、视频源、虚拟手柄是否连接，以及脚本是否正在运行。";

    public JsonSchema Parameters => new() { Type = "object" };

    public Task<string> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var s = _service.GetDeviceStatus();
        var text = string.Join("\n",
            $"单片机: {(s.IsDeviceConnected ? "已连接" : "未连接")}",
            $"视频源: {(s.IsCaptureConnected ? "已连接" : "未连接")}",
            $"虚拟手柄: {(s.IsControllerConnected ? "已连接" : "未连接")}",
            $"脚本运行: {(s.IsScriptRunning ? "运行中" : "未运行")}");
        return Task.FromResult(text);
    }
}
