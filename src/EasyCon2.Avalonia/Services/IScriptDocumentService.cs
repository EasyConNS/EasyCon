using System.ComponentModel;
using EasyCon2.Avalonia.Markup;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// 脚本文档状态机：路径/文本/脏标记/保存中四件套 + 打开/新建/关闭的语义变更 + 落盘。
/// 从 MainWindowViewModel 抽出（Phase 3）；文件对话框、文件树同步与编辑器初始化编排仍留在 VM。
/// 所有 setter 在返回前同步 raise PropertyChanged（无 Dispatcher、无异步）。
/// </summary>
public interface IScriptDocumentService : INotifyPropertyChanged
{
    /// <summary>当前脚本路径。哨兵值见 <see cref="ScriptPathSentinels"/>。</summary>
    string CurrentPath { get; set; }

    /// <summary>编辑区文本。setter 内处理脏标记（未抑制时置脏，语义同原 OnEditorTextChanged）。</summary>
    string Text { get; set; }

    /// <summary>当前脚本内容是否有未保存的修改。</summary>
    bool IsModified { get; set; }

    /// <summary>保存闸门：保存流程进行中为 true。</summary>
    bool IsSaving { get; }

    /// <summary>是否已选中真实脚本路径（非哨兵、非空白），语义同原 HasSelectedScriptPath 三条件。</summary>
    bool HasSelectedPath { get; }

    /// <summary>保存时是否需要先弹对话框（未选中真实路径），语义同原 SaveScriptCoreAsync 的 needsSaveDialog 判定。</summary>
    bool NeedsSaveDialog { get; }

    /// <summary>
    /// 落盘（原 SaveEditorText）：UTF-8 无 BOM 写盘；失败记日志并返回 false，且 IsModified 不变（防"假成功"丢数据）。
    /// </summary>
    bool WriteToFile(string path);

    /// <summary>程序化设置文本（打开/新建/关闭脚本）且不触发修改标记（原 SetEditorTextWithoutDirtyTracking）。</summary>
    void SetTextSilently(string text);

    /// <summary>
    /// 从路径打开：设路径 → 静默载文本 → 清脏（顺序照抄原 OpenScriptFromPath 状态段）。
    /// </summary>
    void OpenFromPath(string path, string fileText);

    /// <summary>新建未命名脚本：置 Untitled 哨兵、清空文本、清脏（顺序照抄原 NewScript 状态行）。</summary>
    void NewUntitled();

    /// <summary>关闭脚本：清空文本、清脏、置 NoScript 哨兵（顺序照抄原 CloseScript 状态行）。</summary>
    void Close();

    /// <summary>进入保存流程。幂等：已在保存中则返回 false 且不重复通知。</summary>
    bool TryBeginSave();

    /// <summary>退出保存流程（幂等，未在保存中时无操作）。</summary>
    void EndSave();
}

/// <summary>
/// 路径哨兵值：与原 MainWindowViewModel 的 NoScriptPathText/UntitledScriptText 同键同机制，
/// L10n 运行时取值，类型初始化时求值一次。
/// </summary>
public static class ScriptPathSentinels
{
    public static readonly string NoScriptPath = L10n.T("Text.Status.NoScriptSelected");

    public static readonly string UntitledScript = L10n.T("Text.Status.UntitledScript");
}
