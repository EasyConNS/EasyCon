using EasyCon.Core.LLM.Agent;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Services;
using System.Text.Json;

namespace EasyCon2.Avalonia.AiAgent.Tools;

/// <summary>
/// run_script 工具：编译并运行当前编辑区的脚本。
/// </summary>
public class RunScriptTool : IAiTool
{
    /// <summary>脚本会驱动真实硬件，需要人工确认。</summary>
    public bool RequiresConfirmation => true;

    private readonly IScriptRunPort _run;
    private readonly IObservabilityPort _observability;

    public RunScriptTool(IScriptRunPort run, IObservabilityPort observability)
    {
        _run = run;
        _observability = observability;
    }

    public string Name => "run_script";

    public string Description => "编译并运行当前编辑区的脚本。脚本在后台运行，不会阻塞对话。";

    public JsonSchema Parameters => new() { Type = "object" };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        // 连接前置检查：脚本会驱动真实硬件，设备不在线时直接给模型可行动的反馈
        var status = _observability.GetDeviceStatus();
        if (!status.IsDeviceConnected)
            return ToolResult.Retryable("单片机未连接，无法运行脚本。", "请先在连接页连接单片机后重试。");

        if (_run.IsScriptRunning)
            return ToolResult.Retryable("脚本已在运行中。", "请先调用 stop_script 停止当前脚本，再重新运行。");

        var ok = await _run.RunScriptAsync();
        return ok
            ? ToolResult.Ok("脚本已启动运行。")
            : ToolResult.Retryable("编译失败，无法运行。", "请调用 get_logs 查看错误信息，修复后重新编译运行。");
    }
}