using EasyCon.Script.Bytecode;
using EasyScript;
using System.Text;

namespace EasyCon.Tests.Support;

/// <summary>记录型 IO 宿主桩：Print 缓冲按行落 <see cref="Lines"/>，Alert 记录不透传。</summary>
internal sealed class RecordingIo : IIoAdapter
{
    public readonly List<string> Lines = new();
    public readonly List<string> Alerts = new();
    readonly StringBuilder _buf = new();

    public void Print(string message, bool newline = true)
    {
        _buf.Append(message);
        if (newline)
        {
            Lines.Add(_buf.ToString());
            _buf.Clear();
        }
    }

    public void Alert(string message) => Alerts.Add(message);
    public string ReadLine() => "";
    public bool TryReadLine(out string line) { line = ""; return false; }
}

/// <summary>记录型手柄桩：按键/摇杆/amiibo 事件按金标准 mock 格式记录（与 .events/TSV 对拍序一致）。</summary>
internal sealed class RecordingPad : ICGamePad
{
    public readonly List<string> Events = new();

    public DelayType DelayMethod => DelayType.Normal;
    public void ClickButtons(GamePadKey key, int duration, CancellationToken token)
        => Events.Add($"KEY {(int)key} {duration}");
    public void PressButtons(GamePadKey key) => Events.Add($"KEYST {(int)key} 1");
    public void ReleaseButtons(GamePadKey key) => Events.Add($"KEYST {(int)key} 0");
    public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token)
        => Events.Add($"STICKC {(int)key - 32} {x} {y} {duration}");
    public void SetStick(GamePadKey key, byte x, byte y)
        => Events.Add($"STICK {(int)key - 32} {x} {y}");
    public void ChangeAmiibo(uint index) => Events.Add($"AMIIBO {index}");
    public void Reset() { }
}

/// <summary>
/// EcxHost 录制装配 + 事件日志归组（docs/Pipeline.md）。
/// 事件组序与存储序恒为 KEY → KEYST → STICK → STICKC → WAIT → AMIIBO → BEEP，
/// 与 .events 语料文件及 <see cref="CvmRunner.ParseEventLogs"/> 的 TSV 分组一致。
/// </summary>
internal static class EcsTestHost
{
    /// <summary>录制型 EcxHost（EnableRecording 装配，输出行与事件日志供对拍）。</summary>
    public static EcxHost CreateRecording()
    {
        var host = new EcxHost();
        host.EnableRecording();
        return host;
    }

    /// <summary>宿主事件日志按标签分组拼接（与 .events 存储序一致）。</summary>
    public static List<string> AllEvents(EcxHost host)
    {
        var all = new List<string>();
        foreach (var group in EventLogGroups(host))
            all.AddRange(group);
        return all;
    }

    /// <summary>七组事件日志（序 = AllEvents 的组序；供与 CvmRunner.ParseEventLogs 逐组对拍）。</summary>
    public static List<List<string>> EventLogGroups(EcxHost host)
        => new()
        {
            host.KeyLog, host.KeyStateLog, host.StickSetLog, host.StickClickLog,
            host.WaitLog, host.AmiiboLog, host.BeepLog,
        };
}