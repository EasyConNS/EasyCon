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

    public RunScriptTool(IScriptRunPort run)
    {
        _run = run;
    }

    public string Name => "run_script";

    public string Description =>
        "编译并运行当前编辑区的脚本。脚本在后台运行，不会阻塞对话。" +
        "是否需要单片机由脚本内容判定：含按键/摇杆操作时运行服务会自动尝试连接单片机，" +
        "纯识别/计算脚本无需连接即可运行。启动结果与连接/编译信息请用 get_logs 跟进。";

    public JsonSchema Parameters => new() { Type = "object" };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        // 不在工具层做连接前置拦截：是否需要单片机由编译产物判定（ScriptService 按
        // KeyAction 决定自动连接/装配 Pad），纯感知脚本无单片机也应可运行；
        // 自动连接的成败是异步写日志的，模型经 get_logs 获知。
        if (_run.IsScriptRunning)
            return ToolResult.Retryable("脚本已在运行中。", "请先调用 stop_script 停止当前脚本，再重新运行。");

        var ok = await _run.RunScriptAsync();
        return ok
            ? ToolResult.Ok("脚本已启动运行。连接/编译等运行期信息以 get_logs 为准。")
            : ToolResult.Retryable("编译失败，无法运行。", "请调用 get_logs 查看错误信息，修复后重新编译运行。");
    }
}