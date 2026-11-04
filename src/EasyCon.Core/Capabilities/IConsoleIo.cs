namespace EasyCon.Core.Capabilities;

/// <summary>
/// 控制台输入输出能力（PRINT/ALERT/FREAD stdin/BEEP）。
/// ReadLine/Beep 提供默认实现（回落进程 Console）；GUI 宿主可覆写以接入终端控件。
/// </summary>
public interface IConsoleIo
{
    /// <summary>输出消息（FWRITE stdout 行断协议落点）。</summary>
    void Print(string message, bool newline = true);

    /// <summary>发送警告（ALERT syscall 落点）。</summary>
    void Alert(string message);

    /// <summary>读取一行 stdin（FREAD 行读落点）；EOF 返回空串。</summary>
    virtual string ReadLine() => Console.ReadLine() ?? "";

    /// <summary>蜂鸣（BEEP syscall 落点）；GUI 宿主可覆写为音频后端。</summary>
    virtual void Beep(int frequency, int durationMs) => Console.Beep(frequency, durationMs);
}