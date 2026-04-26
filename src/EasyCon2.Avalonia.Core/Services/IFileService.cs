namespace EasyCon2.Avalonia.Core.Services;

public interface IFileService
{
    string? CurrentFileName { get; }
    bool IsModified { get; }
    event Action<string?>? FileNameChanged;
    event Action? ScriptModifiedChanged;

    /// <summary>Wire editor text getter/setter (called by view code-behind)</summary>
    void SetEditorAccess(Func<string> getText, Action<string> setText, Func<bool> getModified, Action<bool> setModified);

    string GetText();
    void SetText(string text);
    bool FileOpen(string path);
    void SetFileName(string path);
    bool FileSave();
    void FileClose();
    void MarkSaved();
}