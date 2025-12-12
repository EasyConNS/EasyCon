using System.IO;

namespace EasyCon2;

class TextManager(string path = "")
{
    private const string defaultName = "未命名脚本";

    public string CurrentFilePath { get; private set; } = path;
    public bool IsModified { get; private set; } = false;
    public bool IsFileOpened => !string.IsNullOrEmpty(CurrentFilePath);
    public string FileName => CurrentFilePath == "" ? defaultName : Path.GetFileName(CurrentFilePath);
    public string ShowFileText => IsModified ? $"{FileName}(已编辑)" : FileName;

    public void TextChanged()
    {
        IsModified = true;
    }

    public string FileOpen(string path)
    {
        CurrentFilePath = path;
        IsModified = false;

        return File.ReadAllText(CurrentFilePath);
    }

    public bool FileSave(string content)
    {
        return true;
    }

    public bool SaveAs()
    {
        return true;
    }

    public bool FileClose()
    {
        return true;
    }
}