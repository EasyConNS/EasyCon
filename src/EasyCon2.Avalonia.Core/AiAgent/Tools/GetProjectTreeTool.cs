using System.Text;
using System.Text.Json;
using EasyCon.Core.LLM.Tools;
using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// get_project_tree 工具：返回当前项目的目录结构树（文本形式）。
/// 标签文件（.IL/.ILX）和库文件（lib/ 目录下的 .ecs）会被标记。
/// </summary>
public class GetProjectTreeTool : IAiTool
{
    private readonly IToolCallService _service;

    public GetProjectTreeTool(IToolCallService service) => _service = service;

    public string Name => "get_project_tree";

    public string Description => "获取当前项目的目录结构树。标签文件（.IL/.ILX）标记为 [标签]，库文件（lib/ 目录下的 .ecs）标记为 [库]。";

    public JsonSchema Parameters => new() { Type = "object" };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var tree = _service.GetProjectTree();
        return Task.FromResult(ToolResult.Ok(tree ?? "(未打开项目)"));
    }
}
