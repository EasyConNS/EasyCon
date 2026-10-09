using EasyCon.Core.LLM.Agent;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.AiAgent.Tools;

/// <summary>
/// 注册默认 AI 工具集。
/// 新增工具只需在此方法中添加一行 Register 调用。
/// 装配约定：设备/取帧类工具经能力供给（GetPadInput/GetCaptureSource/GetOcrService）
/// 接 Core 端口实现；编辑器/运行/观测类工具接 GUI 窄端口。
/// </summary>
public static class DefaultTools
{
    public static void RegisterAll(ToolRegistry registry, IToolCallService service)
    {
        // 编辑器缓冲区（GUI 集成词汇，永不下沉 Core）
        registry.Register(new ReadScriptTool(service));
        registry.Register(new WriteScriptTool(service));
        registry.Register(new EditScriptTool(service));
        registry.Register(new GrepScriptTool(service));
        registry.Register(new CompileScriptTool(service));
        registry.Register(new FormatScriptTool(service));

        // 运行控制与可观测
        registry.Register(new RunScriptTool(service, service));
        registry.Register(new StopScriptTool(service));
        registry.Register(new GetDeviceStatusTool(service));
        registry.Register(new GetLogsTool(service));
        registry.Register(new GetProjectTreeTool(service));

        // 原子运行时与取帧（Core 能力端口实现，GUI 只供给能力实例）
        registry.Register(new GetFrameTool(service.GetCaptureSource));
        AtomicRuntimeTools.RegisterAll(
            registry,
            padProvider: service.GetPadInput,
            captureProvider: service.GetCaptureSource,
            ocrProvider: service.GetOcrService);
        registry.Register(new EvalEcsTool(
            captureProvider: () => service.GetCaptureSource(),
            ocrProvider: () => service.GetOcrService()));
    }
}