namespace EasyCon.Core.LLM.Agent.Tools;

/// <summary>
/// 注册默认 AI 工具集。
/// 新增工具只需在此方法中添加一行 Register 调用。
/// 装配约定：设备/取帧类工具经能力供给（GetPadInput/GetCaptureSource/GetOcrService）
/// 接 Core 端口实现；编辑器/运行/观测类工具接宿主窄端口（由调用方按端口分别供给，
/// 本方法不感知 GUI 的 IToolCallService 组合视图）。
/// </summary>
public static class DefaultTools
{
    /// <summary>按窄端口逐一装配默认工具集；宿主的组合视图（IToolCallService）在调用侧拆开传入。</summary>
    public static void RegisterAll(
        ToolRegistry registry,
        IScriptEditorPort editor,
        IScriptRunPort run,
        IWorkspaceInfoPort workspace,
        IObservabilityPort observability,
        IDeviceRuntimePort device)
    {
        // 编辑器缓冲区（宿主集成词汇，工具只依赖窄端口）
        registry.Register(new ReadScriptTool(editor));
        registry.Register(new WriteScriptTool(editor));
        registry.Register(new EditScriptTool(editor));
        registry.Register(new GrepScriptTool(editor));
        registry.Register(new CompileScriptTool(editor));
        registry.Register(new FormatScriptTool(editor));

        // 运行控制与可观测
        registry.Register(new RunScriptTool(run));
        registry.Register(new StopScriptTool(run));
        registry.Register(new GetDeviceStatusTool(observability));
        registry.Register(new GetLogsTool(observability));
        registry.Register(new GetProjectTreeTool(workspace));

        // 原子运行时与取帧（Core 能力端口实现，宿主只供给能力实例）
        registry.Register(new GetFrameTool(device.GetCaptureSource));
        AtomicRuntimeTools.RegisterAll(
            registry,
            padProvider: device.GetPadInput,
            captureProvider: device.GetCaptureSource,
            ocrProvider: device.GetOcrService);
        registry.Register(new EvalEcsTool(
            captureProvider: () => device.GetCaptureSource(),
            ocrProvider: () => device.GetOcrService()));
    }
}