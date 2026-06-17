namespace EzTesseract;

/// <summary>Tesseract 引擎初始化或运行时错误。对应原 TesseractOCR.TesseractException。</summary>
public sealed class TesseractException : Exception
{
    public TesseractException(string message) : base(message) { }
    public TesseractException(string message, Exception innerException) : base(message, innerException) { }
}
