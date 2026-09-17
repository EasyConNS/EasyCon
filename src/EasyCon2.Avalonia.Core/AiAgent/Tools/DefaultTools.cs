using EasyCon2.Avalonia.Core.Services;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// 注册默认 AI 工具集。
/// 新增工具只需在此方法中添加一行 Register 调用。
/// </summary>
public static class DefaultTools
{
    public static void RegisterAll(ToolRegistry registry, IToolCallService service)
    {
        registry.Register(new ReadScriptTool(service));
        registry.Register(new WriteScriptTool(service));
        registry.Register(new EditScriptTool(service));
        registry.Register(new GrepScriptTool(service));
        registry.Register(new CompileScriptTool(service));
        registry.Register(new FormatScriptTool(service));
        registry.Register(new GetDeviceStatusTool(service));
        registry.Register(new GetLogsTool(service));
        registry.Register(new GetProjectTreeTool(service));
        registry.Register(new RunScriptTool(service));
        registry.Register(new StopScriptTool(service));
        registry.Register(new GetFrameTool(service));
        registry.Register(new GetWeatherTool());
    }
}