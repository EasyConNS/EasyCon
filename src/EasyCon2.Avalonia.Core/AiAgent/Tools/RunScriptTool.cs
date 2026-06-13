using System.Text.Json;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// run_script 工具：编译并运行当前编辑区的脚本。
/// </summary>
public class RunScriptTool : IAiTool
{
    private readonly IToolCallService _service;

    public RunScriptTool(IToolCallService service) => _service = service;

    public string Name => "run_script";

    public string Description => "编译并运行当前编辑区的脚本。脚本在后台运行，不会阻塞对话。";

    public JsonSchema Parameters => new() { Type = "object" };

    public async Task<string> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var status = _service.GetDeviceStatus();
        if (!status.IsDeviceConnected)
            return "单片机未连接，请先连接设备后再运行脚本。";

        if (_service.IsScriptRunning)
            return "脚本已在运行中，请先调用 stop_script 停止。";

        var ok = await _service.RunScriptAsync();
        return ok
            ? "脚本已启动运行。"
            : "编译失败，请调用 get_logs 查看错误信息。";
    }
}
