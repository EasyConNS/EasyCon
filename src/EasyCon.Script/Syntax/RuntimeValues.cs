namespace EasyCon.Script.Syntax;

/// <summary>
/// 运行时值魔名的单一事实源（Parser 识别、Binder 绑定、BytecodeEncoder 落码共用）。
/// </summary>
public static class RuntimeValues
{
    /// <summary>脚本运行时长（毫秒；syscall TIME）。</summary>
    public const string Time = "__TIME__";

    /// <summary>应用目录（syscall APP）。</summary>
    public const string App = "__APP__";
}