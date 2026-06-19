namespace EzCv;

/// <summary>
/// OpenCV 异常（对应 OpenCvSharp.OpenCVException）。
/// </summary>
public sealed class OpenCVException : Exception
{
    public OpenCVException(string message) : base(message) { }
    public OpenCVException(string message, Exception innerException) : base(message, innerException) { }
}