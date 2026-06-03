using System;

namespace EasyScript;

/// &lt;summary&gt;
/// 统一的输入输出适配器接口，合并了输入和输出功能。
/// &lt;/summary&gt;
public interface IIoAdapter
{
    // ---- 输出方法 ----

    /// &lt;summary&gt;
    /// 输出消息到标准输出。
    /// &lt;/summary&gt;
    /// &lt;param name="message"&gt;要输出的消息。&lt;/param&gt;
    /// &lt;param name="newline"&gt;是否在消息后添加换行符。&lt;/param&gt;
    void Print(string message, bool newline = true);

    /// &lt;summary&gt;
    /// 发送警告消息。
    /// &lt;/summary&gt;
    /// &lt;param name="message"&gt;要发送的警告消息。&lt;/param&gt;
    void Alert(string message);

    // ---- 输入方法 ----

    /// &lt;summary&gt;
    /// 从标准输入读取一行文本。
    /// &lt;/summary&gt;
    /// &lt;returns&gt;读取到的文本行，如果读取失败则返回空字符串。&lt;/returns&gt;
    string ReadLine();

    /// &lt;summary&gt;
    /// 尝试从标准输入读取一行文本。
    /// &lt;/summary&gt;
    /// &lt;param name="line"&gt;读取到的文本行。&lt;/param&gt;
    /// &lt;returns&gt;如果成功读取则返回true，否则返回false。&lt;/returns&gt;
    bool TryReadLine(out string line);
}