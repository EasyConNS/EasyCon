namespace EasyCon.Core.Capabilities;

/// <summary>
/// 控制台输出能力（PRINT/ALERT）。
/// stdin 在桌面参考语义中走进程 Console（EcxHost.ReadLine），不经本接口。
/// </summary>
public interface IConsoleIo
{
    /// <summary>输出消息（FWRITE stdout 行断协议落点）。</summary>
    void Print(string message, bool newline = true);

    /// <summary>发送警告（ALERT syscall 落点）。</summary>
    void Alert(string message);
}