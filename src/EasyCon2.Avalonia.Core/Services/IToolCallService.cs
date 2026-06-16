namespace EasyCon2.Avalonia.Core.Services;

/// <summary>
/// AI 工具调用服务抽象，作为工具与应用状态之间的统一数据入口。
/// 所有 AI 工具通过此接口获取所需数据或执行只读分析动作，避免直接耦合 ViewModel。
/// 涉及副作用且需用户确认的操作（运行、停止、写入脚本）不在此提供，由用户主动触发。
/// </summary>
public interface IToolCallService
{
    // ── 编辑区 ──────────────────────────────

    /// <summary>
    /// 获取当前编辑区的完整脚本文本。
    /// </summary>
    string GetScriptContent();

    /// <summary>
    /// 写入脚本内容到编辑区。
    /// </summary>
    /// <param name="content">要写入的脚本文本。</param>
    /// <param name="append">true 追加到末尾，false 替换全部。</param>
    void WriteScriptContent(string content, bool append);

    /// <summary>
    /// 查找并替换编辑区中的文本。
    /// </summary>
    /// <param name="oldString">要查找的文本（精确匹配）。</param>
    /// <param name="newString">替换为的文本（可为空字符串表示删除）。</param>
    /// <param name="count">最大替换次数，0 表示全部替换。</param>
    /// <returns>实际替换的次数，-1 表示未找到匹配。</returns>
    int EditScriptContent(string oldString, string newString, int count);

    /// <summary>
    /// 获取当前脚本文件路径（未保存时为 null）。
    /// </summary>
    string? GetScriptPath();

    /// <summary>
    /// 编译当前脚本，返回是否成功。
    /// </summary>
    Task<bool> CompileScriptAsync();

    /// <summary>
    /// 格式化当前脚本并写入编辑区，返回格式化后的文本。
    /// </summary>
    Task<string> FormatScriptAsync();

    // ── 状态 ────────────────────────────────

    /// <summary>
    /// 获取设备/视频源/手柄连接状态及脚本运行状态。
    /// </summary>
    DeviceStatusInfo GetDeviceStatus();

    // ── 项目 ────────────────────────────────

    /// <summary>
    /// 获取当前项目的目录结构树（文本形式）。
    /// 返回 null 表示未打开项目。
    /// </summary>
    string? GetProjectTree();

    // ── 脚本执行 ──────────────────────────────

    /// <summary>
    /// 编译并运行当前编辑区脚本。返回是否成功启动。
    /// </summary>
    Task<bool> RunScriptAsync();

    /// <summary>
    /// 停止正在运行的脚本。
    /// </summary>
    void StopScript();

    /// <summary>
    /// 脚本是否正在运行。
    /// </summary>
    bool IsScriptRunning { get; }

    // ── 视觉 ────────────────────────────────

    /// <summary>
    /// 获取当前视频帧的 base64 PNG 字符串（半分辨率）。
    /// 视频源未连接或帧获取失败时返回 null。
    /// </summary>
    string? GetCurrentFrameBase64();

    // ── 日志 ────────────────────────────────

    /// <summary>
    /// 获取最近 N 条日志（按行）。
    /// </summary>
    string GetRecentLogs(int maxLines);
}

/// <summary>
/// 设备连接与运行状态快照。
/// </summary>
public record DeviceStatusInfo(
    bool IsDeviceConnected,
    bool IsCaptureConnected,
    bool IsControllerConnected,
    bool IsScriptRunning
);