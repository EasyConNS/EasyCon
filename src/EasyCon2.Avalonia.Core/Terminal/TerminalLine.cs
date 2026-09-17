using Avalonia.Media;

namespace EasyCon2.Avalonia.Core.Terminal;

/// <summary>
/// 一段同色文本（最小渲染单元）。
/// </summary>
public record TextSegment(
    string Text,
    Color? Foreground = null,
    Color? Background = null,
    bool Bold = false,
    bool Underline = false);

/// <summary>
/// 终端的一行输出，由多个 <see cref="TextSegment"/> 组成。
/// </summary>
public class TerminalLine
{
    public List<TextSegment> Segments { get; } = new();

    // 由 TerminalControl 缓存的测量宽度（行宽仅依赖字体，与前景色无关）。
    // 字体变更时通过 WidthMetricsVersion 与控件的 _metricsVersion 对比失效。
    internal double Width = -1;
    internal int WidthMetricsVersion = -1;

    private int? _textLength;

    /// <summary>所有段的文本总长度（缓存在首次访问后）。</summary>
    public int TextLength
    {
        get
        {
            if (_textLength == null)
            {
                var len = 0;
                foreach (var s in Segments) len += s.Text.Length;
                _textLength = len;
            }
            return _textLength.Value;
        }
    }

    /// <summary>拼接所有段为纯文本。</summary>
    public string GetText() => string.Concat(Segments.Select(s => s.Text));
}