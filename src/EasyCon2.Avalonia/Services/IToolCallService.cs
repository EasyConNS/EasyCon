using EasyCon.Core.Capabilities;



namespace EasyCon2.Avalonia.Services;

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

    /// <summary>
    /// 获取当前项目根目录的绝对路径。未打开项目时返回 null。
    /// 用于构建项目级 AI 技能搜索路径（&lt;项目&gt;/skills）。
    /// </summary>
    string? GetProjectDirectory();

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

    // ── 原子输入（直接驱动手柄，不经脚本）──

    /// <summary>
    /// 按键点击：按下并释放，可重复多次。
    /// 设备未连接或按键名非法时返回失败描述。
    /// </summary>
    PadActionResult PressButton(string key, int durationMs, int times, int intervalMs);

    /// <summary>
    /// 摇杆偏转：设置指定摇杆的 (x,y)（0-255，128 为中心），
    /// 持续 durationMs 后回中；durationMs 为 0 表示不复位（由调用方负责）。
    /// </summary>
    PadActionResult SetStick(string key, int x, int y, int durationMs);

    // ── 原子感知 ─────────────────────────────

    /// <summary>
    /// 对当前视频帧的指定区域做 OCR（全分辨率）。x=y=w=h=0 表示整图。
    /// 返回识别文本；视频源未连接或识别失败返回 null。
    /// </summary>
    OcrFrameResult? OcrFrame(string? language, int x, int y, int width, int height);

    // ── eval_ecs 能力供给 ────────────────────

    /// <summary>
    /// 实时采集源（帧委托），供 eval_ecs 片段经 __CAPTURE__/NET_IMAGE 取帧。
    /// 视频源未就绪时返回 null。
    /// </summary>
    ICaptureSource? GetCaptureSource();

    /// <summary>
    /// 宿主 OCR 服务（eval_ecs 片段的 OCR_INIT/OCR 通路）；未装配返回 null。
    /// </summary>
    IOcrService? GetOcrService();

    // ── 日志 ────────────────────────────────

    /// <summary>
    /// 获取最近 N 条日志（按行）。
    /// </summary>
    string GetRecentLogs(int maxLines);
}

/// <summary>原子手柄输入的执行结果。</summary>
public record PadActionResult(bool Success, string Message);

/// <summary>帧 OCR 结果。</summary>
public record OcrFrameResult(string Text, int Confidence, string Backend);

/// <summary>
/// 设备连接与运行状态快照。
/// </summary>
public record DeviceStatusInfo(
    bool IsDeviceConnected,
    bool IsCaptureConnected,
    bool IsControllerConnected,
    bool IsScriptRunning
);