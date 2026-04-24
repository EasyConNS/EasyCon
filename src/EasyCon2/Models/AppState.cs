using System.IO;

namespace EasyCon2.Models;

public class AppState
{
    public bool ScriptRunning { get; set; }
    public bool DeviceConnected { get; set; }
    public bool CaptureConnected { get; set; }
    public string? CurrentFilePath { get; set; }
    public DateTime ScriptStartTime { get; set; }

    public string DisplayName =>
        CurrentFilePath is null ? "未命名脚本" : Path.GetFileName(CurrentFilePath);

    public bool IsModified { get; set; }

    public string TitleText
    {
        get
        {
            var name = DisplayName;
            if (IsModified) name += "(已编辑)";
            return name;
        }
    }

    public string TimerText =>
        ScriptStartTime == DateTime.MinValue
            ? "00:00:00"
            : (DateTime.Now - ScriptStartTime).ToString(@"hh\:mm\:ss");
}