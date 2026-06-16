using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// compile_script 工具：编译当前编辑区脚本，返回成功与否。
/// 编译错误会写入日志，可通过 get_logs 获取详情。
/// </summary>
public class CompileScriptTool : IAiTool
{
    private readonly IToolCallService _service;

    public CompileScriptTool(IToolCallService service) => _service = service;

    public string Name => "compile_script";

    public string Description => "编译当前编辑区的脚本，检查语法错误。返回编译是否成功。若失败，错误详情见日志（可调用 get_logs 获取）。";

    public JsonSchema Parameters => new() { Type = "object" };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var ok = await _service.CompileScriptAsync();
        return ok
            ? ToolResult.Ok("✅ 编译成功，无错误。")
            : ToolResult.Retryable("编译失败。", "请调用 get_logs 查看错误详情，修复语法错误后重新编译。");
    }
}