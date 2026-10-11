using System.ComponentModel;
using System.Text;
using ILogService = EasyCon.Core.Services.ILogService;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// <see cref="IScriptDocumentService"/> 默认实现：纯状态机，不依赖任何 UI 类型、不依赖 IDialogService。
/// 脏标记判定、哨兵比较、落盘编码（UTF-8 无 BOM）与失败日志语义均照抄原 MainWindowViewModel。
/// 所有 setter 在返回前同步 raise PropertyChanged，且与 ObservableProperty 生成代码一致：值未变化时不触发任何事件/副作用。
/// </summary>
public sealed class ScriptDocumentService : IScriptDocumentService
{
    private readonly ILogService _logService;
    private string _currentPath = ScriptPathSentinels.NoScriptPath;
    private string _text = string.Empty;
    private bool _isModified;
    private bool _isSaving;

    /// <summary>程序化设置文本（打开/新建/关闭脚本）时抑制修改标记。</summary>
    private bool _suppressDirtyTracking;

    public ScriptDocumentService(ILogService logService)
    {
        _logService = logService;
    }

    /// <inheritdoc />
    public string CurrentPath
    {
        get => _currentPath;
        set
        {
            if (EqualityComparer<string>.Default.Equals(_currentPath, value))
                return;
            _currentPath = value;
            RaiseChanged(nameof(CurrentPath));
        }
    }

    /// <inheritdoc />
    public string Text
    {
        get => _text;
        set
        {
            if (EqualityComparer<string>.Default.Equals(_text, value))
                return;
            _text = value;
            // 原 OnEditorTextChanged：非抑制时置脏（值未变时不重复通知，同 ObservableProperty 生成 setter 的相等护栏）；
            // 无论是否抑制，文本变化本身都要通知（命令重估由订阅方完成）。
            if (!_suppressDirtyTracking && !_isModified)
            {
                _isModified = true;
                RaiseChanged(nameof(IsModified));
            }
            RaiseChanged(nameof(Text));
        }
    }

    /// <inheritdoc />
    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (_isModified == value)
                return;
            _isModified = value;
            RaiseChanged(nameof(IsModified));
        }
    }

    /// <inheritdoc />
    public bool IsSaving => _isSaving;

    /// <inheritdoc />
    public bool HasSelectedPath =>
        !string.IsNullOrWhiteSpace(_currentPath)
        && _currentPath != ScriptPathSentinels.NoScriptPath
        && _currentPath != ScriptPathSentinels.UntitledScript;

    /// <inheritdoc />
    public bool NeedsSaveDialog => !HasSelectedPath;

    /// <inheritdoc />
    public bool WriteToFile(string path)
    {
        try
        {
            File.WriteAllText(path, _text, new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex)
        {
            // 保存失败必须显式暴露：保持 IsModified，避免"假成功"导致数据丢失
            _logService.AddLog($"保存脚本失败({path}): {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public void SetTextSilently(string text)
    {
        _suppressDirtyTracking = true;
        try
        {
            Text = text;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
    }

    /// <inheritdoc />
    public void OpenFromPath(string path, string fileText)
    {
        CurrentPath = path;
        SetTextSilently(fileText);
        IsModified = false;
    }

    /// <inheritdoc />
    public void NewUntitled()
    {
        SetTextSilently(string.Empty);
        IsModified = false;
        CurrentPath = ScriptPathSentinels.UntitledScript;
    }

    /// <inheritdoc />
    public void Close()
    {
        SetTextSilently(string.Empty);
        IsModified = false;
        CurrentPath = ScriptPathSentinels.NoScriptPath;
    }

    /// <inheritdoc />
    public bool TryBeginSave()
    {
        if (_isSaving)
            return false;
        _isSaving = true;
        RaiseChanged(nameof(IsSaving));
        return true;
    }

    /// <inheritdoc />
    public void EndSave()
    {
        if (!_isSaving)
            return;
        _isSaving = false;
        RaiseChanged(nameof(IsSaving));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
