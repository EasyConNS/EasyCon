using Avalonia.Media;
using System.Text;

namespace EasyCon2.Avalonia.Core.Terminal;

/// <summary>
/// 解析 ANSI/VT100 SGR（Select Graphic Rendition）转义序列，输出 <see cref="TerminalLine"/>。
/// </summary>
public class AnsiParser
{
    // 标准 8 色（适配浅色背景）
    private static readonly Color[] StandardColors =
    [
        Color.FromRgb(0x00, 0x00, 0x00), // 30: Black
        Color.FromRgb(0xCC, 0x00, 0x00), // 31: Red
        Color.FromRgb(0x00, 0x8C, 0x00), // 32: Green
        Color.FromRgb(0x99, 0x88, 0x00), // 33: Yellow
        Color.FromRgb(0x00, 0x00, 0xCC), // 34: Blue
        Color.FromRgb(0xCC, 0x00, 0xCC), // 35: Magenta
        Color.FromRgb(0x00, 0x88, 0x88), // 36: Cyan
        Color.FromRgb(0x66, 0x66, 0x66), // 37: White
    ];

    // 高亮 8 色
    private static readonly Color[] BrightColors =
    [
        Color.FromRgb(0x80, 0x80, 0x80), // 90: Bright Black
        Color.FromRgb(0xFF, 0x00, 0x00), // 91: Bright Red
        Color.FromRgb(0x00, 0xCC, 0x00), // 92: Bright Green
        Color.FromRgb(0xCC, 0xCC, 0x00), // 93: Bright Yellow
        Color.FromRgb(0x00, 0x00, 0xFF), // 94: Bright Blue
        Color.FromRgb(0xFF, 0x00, 0xFF), // 95: Bright Magenta
        Color.FromRgb(0x00, 0xCC, 0xCC), // 96: Bright Cyan
        Color.FromRgb(0x33, 0x33, 0x33), // 97: Bright White
    ];

    /// <summary>
    /// 将包含 ANSI 转义序列的原始文本解析为一行 <see cref="TerminalLine"/>。
    /// 不含 ANSI 序列的纯文本将生成单色单段的行。
    /// </summary>
    public TerminalLine ParseLine(string rawText)
    {
        var line = new TerminalLine();
        var state = new SgrState();
        var text = new StringBuilder();
        var i = 0;

        while (i < rawText.Length)
        {
            // 检测 ESC [ ... m 序列
            if (rawText[i] == '\x1b' && i + 1 < rawText.Length && rawText[i + 1] == '[')
            {
                Flush(line, text, state);
                i += 2;

                var paramsBuf = new StringBuilder();
                while (i < rawText.Length && rawText[i] != 'm')
                    paramsBuf.Append(rawText[i++]);
                if (i < rawText.Length) i++; // skip 'm'

                ApplySgr(paramsBuf.ToString(), state);
            }
            else
            {
                text.Append(rawText[i++]);
            }
        }

        Flush(line, text, state);
        return line;
    }

    private static void Flush(TerminalLine line, StringBuilder text, SgrState state)
    {
        if (text.Length == 0) return;
        line.Segments.Add(new TextSegment(
            text.ToString(),
            state.Foreground,
            state.Background,
            state.Bold,
            state.Underline));
        text.Clear();
    }

    private static void ApplySgr(string paramString, SgrState state)
    {
        if (string.IsNullOrEmpty(paramString)) { state.Reset(); return; }

        var parts = paramString.Split(';');
        for (var idx = 0; idx < parts.Length;)
        {
            if (!int.TryParse(parts[idx], out var code)) { idx++; continue; }

            switch (code)
            {
                case 0: state.Reset(); break;
                case 1: state.Bold = true; break;
                case 4: state.Underline = true; break;
                case 22: state.Bold = false; break;
                case 24: state.Underline = false; break;
                case 38:
                    idx = ParseExtendedColor(parts, idx + 1, c => state.Foreground = c);
                    continue;
                case 39: state.Foreground = null; break;
                case 48:
                    idx = ParseExtendedColor(parts, idx + 1, c => state.Background = c);
                    continue;
                case 49: state.Background = null; break;
                default:
                    if (code is >= 30 and <= 37)
                        state.Foreground = StandardColors[code - 30];
                    else if (code is >= 40 and <= 47)
                        state.Background = StandardColors[code - 40];
                    else if (code is >= 90 and <= 97)
                        state.Foreground = BrightColors[code - 90];
                    else if (code is >= 100 and <= 107)
                        state.Background = BrightColors[code - 100];
                    break;
            }
            idx++;
        }
    }

    private static int ParseExtendedColor(string[] parts, int start, Action<Color?> setter)
    {
        if (start >= parts.Length || !int.TryParse(parts[start], out var type))
            return start + 1;

        // 256 色模式：38;5;N
        if (type == 5 && start + 1 < parts.Length && int.TryParse(parts[start + 1], out var idx))
        {
            setter(Get256Color(idx));
            return start + 2;
        }

        // 真彩色模式：38;2;R;G;B
        if (type == 2 && start + 3 < parts.Length
            && int.TryParse(parts[start + 1], out var r)
            && int.TryParse(parts[start + 2], out var g)
            && int.TryParse(parts[start + 3], out var b))
        {
            setter(Color.FromRgb((byte)r, (byte)g, (byte)b));
            return start + 4;
        }

        return start + 1;
    }

    private static Color Get256Color(int index)
    {
        if (index < 8) return StandardColors[index];
        if (index < 16) return BrightColors[index - 8];
        if (index >= 232)
        {
            // 灰度渐变（232-255）
            var g = (byte)(8 + (index - 232) * 10);
            return Color.FromRgb(g, g, g);
        }
        // 6×6×6 色立方体（16-231）
        index -= 16;
        var ri = index / 36;
        var gi = (index / 6) % 6;
        var bi = index % 6;
        return Color.FromRgb(CubeComponent(ri), CubeComponent(gi), CubeComponent(bi));
    }

    private static byte CubeComponent(int level) => (byte)(level > 0 ? 55 + level * 40 : 0);

    private class SgrState
    {
        public Color? Foreground;
        public Color? Background;
        public bool Bold;
        public bool Underline;

        public void Reset()
        {
            Foreground = null;
            Background = null;
            Bold = false;
            Underline = false;
        }
    }
}