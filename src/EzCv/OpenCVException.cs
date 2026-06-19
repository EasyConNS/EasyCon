using System.Text.RegularExpressions;

namespace EzCv;

/// <summary>
/// OpenCV 异常（对应 OpenCvSharp.OpenCVException）。
/// <para>
/// 由 <see cref="Interop.EzCvError.ThrowIfAny"/> 在 native last_error 非空时抛出。
/// message 取自 native <c>cv::Exception::what()</c>，标准格式：
/// <c>OpenCV(5.x) [/abs/path/file.cpp:123] funcName: actual error text</c>
/// 本类解析出 File/Line/Func/Status，便于上层定位。
/// </para>
/// </summary>
public sealed class OpenCVException : Exception
{
    private static readonly Regex Pattern =
        new(@"OpenCV\(([^)]*)\)\s*\[([^:]+):(\d+)\]\s*([^:]*?):\s*(.*)",
            RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>OpenCV 版本字符串（如 "5.0.0"）。无法解析时为 null。</summary>
    public string? Status { get; }

    /// <summary>源文件路径。无法解析时为 null。</summary>
    public string? File { get; }

    /// <summary>源行号。无法解析时为 0。</summary>
    public int Line { get; }

    /// <summary>出错的函数名。无法解析时为 null。</summary>
    public string? Func { get; }

    /// <summary>去掉 OpenCV 前缀后的实际错误信息。无法解析时退回完整 message。</summary>
    public string ErrText { get; }

    public OpenCVException(string message) : base(message)
    {
        var m = Pattern.Match(message ?? string.Empty);
        if (m.Success)
        {
            Status = m.Groups[1].Value;
            File = m.Groups[2].Value;
            Line = int.TryParse(m.Groups[3].Value, out var ln) ? ln : 0;
            Func = m.Groups[4].Value;
            ErrText = m.Groups[5].Value;
        }
        else
        {
            ErrText = message ?? string.Empty;
        }
    }

    public OpenCVException(string message, Exception innerException) : base(message, innerException)
    {
        ErrText = message ?? string.Empty;
    }

    public override string ToString()
    {
        var loc = File is not null ? $" [{System.IO.Path.GetFileName(File)}:{Line}]" : string.Empty;
        var func = Func is not null ? $" {Func}: " : string.Empty;
        return $"OpenCVException{loc}{func}{ErrText}";
    }
}
