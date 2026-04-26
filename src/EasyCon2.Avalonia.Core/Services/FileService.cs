using EasyCon.Core.Services;
using System.IO;

namespace EasyCon2.Avalonia.Core.Services;

public class FileService : IFileService
{
    private readonly ILogService _logService;
    private string? _fileName;
    private const string ScriptPath = @"Script\";

    private Func<string> _getText = () => "";
    private Action<string> _setText = _ => { };
    private Func<bool> _getModified = () => false;
    private Action<bool> _setModified = _ => { };

    public string? CurrentFileName => _fileName;
    public bool IsModified => _getModified();

    public event Action<string?>? FileNameChanged;
    public event Action? ScriptModifiedChanged;

    public FileService(ILogService logService)
    {
        _logService = logService;
    }

    public void SetEditorAccess(Func<string> getText, Action<string> setText, Func<bool> getModified, Action<bool> setModified)
    {
        _getText = getText;
        _setText = setText;
        _getModified = getModified;
        _setModified = setModified;
    }

    public string GetText() => _getText();
    public void SetText(string text) => _setText(text);

    public bool FileOpen(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        Directory.CreateDirectory(ScriptPath);
        var text = File.ReadAllText(path);
        _setText(text);
        _setModified(false);
        _fileName = path;
        _logService.AddLog("文件已打开");
        FileNameChanged?.Invoke(path);
        return true;
    }

    public void SetFileName(string path)
    {
        _fileName = path;
        FileNameChanged?.Invoke(path);
    }

    public bool FileSave()
    {
        if (string.IsNullOrEmpty(_fileName)) return false;

        Directory.CreateDirectory(ScriptPath);
        File.WriteAllText(_fileName, _getText());
        _setModified(false);
        _logService.AddLog("文件已保存");
        ScriptModifiedChanged?.Invoke();
        return true;
    }

    public void FileClose()
    {
        _fileName = null;
        _setText("");
        _setModified(false);
        _logService.AddLog("文件已关闭");
        FileNameChanged?.Invoke(null);
    }

    public void MarkSaved()
    {
        _setModified(false);
        ScriptModifiedChanged?.Invoke();
    }
}