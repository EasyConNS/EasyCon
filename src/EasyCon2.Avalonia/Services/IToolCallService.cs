using EasyCon.Core.Capabilities;

namespace EasyCon2.Avalonia.Services;

// ── 窄端口 ─────────────────────────────────
// 设计约定（runtime knows capabilities, not integrations）：
// 每个工具只依赖它需要的那一个窄端口；IToolCallService 只是 GUI 内部的组合视图。
// 编辑器端口是纯集成词汇（Core 的词汇表里没有"编辑器"），因此永远留在 GUI，
// 不会也不应下沉 Core。

/// <summary>脚本编辑区端口：读/写/精确替换编辑器缓冲区，编译与格式化。</summary>
public interface IScriptEditorPort
{
    /// <summary>获取当前编辑区的完整脚本文本。</summary>
    string GetScriptContent();

    /// <summary>写入脚本内容到编辑区。append=true 追加到末尾，false 替换全部。</summary>
    void WriteScriptContent(string content, bool append);

    /// <summary>查找并替换编辑区中的文本。返回实际替换次数，-1 表示未找到匹配。</summary>
    int EditScriptContent(string oldString, string newString, int count);

    /// <summary>获取当前脚本文件路径（未保存时为 null）。</summary>
    string? GetScriptPath();

    /// <summary>编译当前脚本，返回是否成功。</summary>
    Task<bool> CompileScriptAsync();

    /// <summary>格式化当前脚本并写入编辑区，返回格式化后的文本。</summary>
    Task<string> FormatScriptAsync();
}

/// <summary>
/// 脚本运行端口：编译并运行当前编辑区脚本、停止、运行状态。
/// 今天经由编辑区内容启动（GUI 会话语义）；待 Core 暴露可装配的脚本运行控制端口
/// 后再迁移（决策记录见 PROJECT_OUTLINE）。
/// </summary>
public interface IScriptRunPort
{
    /// <summary>编译并运行当前编辑区脚本。返回是否成功启动。</summary>
    Task<bool> RunScriptAsync();

    /// <summary>停止正在运行的脚本。</summary>
    void StopScript();

    /// <summary>脚本是否正在运行。</summary>
    bool IsScriptRunning { get; }
}

/// <summary>工作区信息端口：项目目录与目录树。</summary>
public interface IWorkspaceInfoPort
{
    /// <summary>获取当前项目的目录结构树（文本形式）。返回 null 表示未打开项目。</summary>
    string? GetProjectTree();

    /// <summary>获取当前项目根目录的绝对路径。未打开项目时返回 null。</summary>
    string? GetProjectDirectory();
}

/// <summary>可观测端口：设备连接状态与最近日志。</summary>
public interface IObservabilityPort
{
    /// <summary>获取设备/视频源/手柄连接状态及脚本运行状态。</summary>
    DeviceStatusInfo GetDeviceStatus();

    /// <summary>获取最近 N 条日志（按行）。</summary>
    string GetRecentLogs(int maxLines);
}

/// <summary>
/// 设备运行时端口：惰性提供 Core 能力实例（未连接返回 null）。
/// 能力实例本身按 Core 端口约束（IPadInput/ICaptureSource/IOcrService）——
/// 工具直接消费能力端口，本接口只负责"按连接状态供给实例"，不再承载动作方法。
/// </summary>
public interface IDeviceRuntimePort
{
    /// <summary>手柄输入能力（IPadInput）。设备未连接时返回 null。</summary>
    IPadInput? GetPadInput();

    /// <summary>实时采集源（帧委托）。视频源未就绪时返回 null。</summary>
    ICaptureSource? GetCaptureSource();

    /// <summary>宿主 OCR 服务（OCR_INIT/OCR 通路）。未装配返回 null。</summary>
    IOcrService? GetOcrService();
}

/// <summary>GUI agent 工具服务的组合视图（全部窄端口）。工具代码请依赖窄端口，勿依赖本接口。</summary>
public interface IToolCallService : IScriptEditorPort, IScriptRunPort, IWorkspaceInfoPort, IObservabilityPort, IDeviceRuntimePort
{
}

/// <summary>设备连接与运行状态快照。</summary>
public record DeviceStatusInfo(
    bool IsDeviceConnected,
    bool IsCaptureConnected,
    bool IsControllerConnected,
    bool IsScriptRunning
);