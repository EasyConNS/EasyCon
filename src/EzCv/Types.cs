namespace EzCv;

public struct Size
{
    public int Width;
    public int Height;

    public Size(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public override string ToString() => $"Size({Width}, {Height})";
}

public struct Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override string ToString() => $"Point({X}, {Y})";
}

public struct Rect
{
    public int X;
    public int Y;
    public int Width;
    public int Height;

    public Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public override string ToString() => $"Rect({X}, {Y}, {Width}, {Height})";
}

public struct Scalar
{
    public double Val0, Val1, Val2, Val3;

    public Scalar(double v0, double v1 = 0, double v2 = 0, double v3 = 0)
    {
        Val0 = v0; Val1 = v1; Val2 = v2; Val3 = v3;
    }

    public static Scalar Black => new(0, 0, 0);
    public static Scalar White => new(255, 255, 255);
}

public static class MatType
{
    // Depth constants
    public const int CV_8U = 0;
    public const int CV_8S = 1;
    public const int CV_16U = 2;
    public const int CV_16S = 3;
    public const int CV_32S = 4;
    public const int CV_32F = 5;
    public const int CV_64F = 6;
    public const int CV_16F = 7;
    public const int CV_16BF = 8;    // bfloat16 (OpenCV 5)
    public const int CV_Bool = 9;    // bool 1-byte (OpenCV 5)
    public const int CV_64U = 10;    // uint64 (OpenCV 5)
    public const int CV_64S = 11;    // int64 (OpenCV 5)
    public const int CV_32U = 12;    // uint32 (OpenCV 5)

    // 8-bit unsigned multi-channel
    public const int CV_8UC1 = 0;
    public const int CV_8UC2 = 8;
    public const int CV_8UC3 = 16;
    public const int CV_8UC4 = 24;

    // 8-bit signed multi-channel
    public const int CV_8SC1 = 1;
    public const int CV_8SC2 = 9;
    public const int CV_8SC3 = 17;
    public const int CV_8SC4 = 25;

    // 16-bit unsigned multi-channel
    public const int CV_16UC1 = 2;
    public const int CV_16UC2 = 10;
    public const int CV_16UC3 = 18;
    public const int CV_16UC4 = 26;

    // 16-bit signed multi-channel
    public const int CV_16SC1 = 3;
    public const int CV_16SC2 = 11;
    public const int CV_16SC3 = 19;
    public const int CV_16SC4 = 27;

    // 32-bit signed multi-channel
    public const int CV_32SC1 = 4;
    public const int CV_32SC2 = 12;
    public const int CV_32SC3 = 20;
    public const int CV_32SC4 = 28;

    // 32-bit float multi-channel
    public const int CV_32FC1 = 5;
    public const int CV_32FC2 = 13;
    public const int CV_32FC3 = 21;
    public const int CV_32FC4 = 29;

    // 64-bit float multi-channel
    public const int CV_64FC1 = 6;
    public const int CV_64FC2 = 14;
    public const int CV_64FC3 = 22;
    public const int CV_64FC4 = 30;

    // 16-bit float multi-channel
    public const int CV_16FC1 = 7;
    public const int CV_16FC2 = 15;
    public const int CV_16FC3 = 23;
    public const int CV_16FC4 = 31;

    // 16-bit bfloat16 multi-channel (OpenCV 5)
    public const int CV_16BFC1 = 8;
    public const int CV_16BFC2 = 16;
    public const int CV_16BFC3 = 24;
    public const int CV_16BFC4 = 32;

    // Bool multi-channel (OpenCV 5)
    public const int CV_BoolC1 = 9;
    public const int CV_BoolC2 = 17;
    public const int CV_BoolC3 = 25;
    public const int CV_BoolC4 = 33;

    // 64-bit unsigned multi-channel (OpenCV 5)
    public const int CV_64UC1 = 10;
    public const int CV_64UC2 = 18;
    public const int CV_64UC3 = 26;
    public const int CV_64UC4 = 34;

    // 64-bit signed multi-channel (OpenCV 5)
    public const int CV_64SC1 = 11;
    public const int CV_64SC2 = 19;
    public const int CV_64SC3 = 27;
    public const int CV_64SC4 = 35;

    // 32-bit unsigned multi-channel (OpenCV 5)
    public const int CV_32UC1 = 12;
    public const int CV_32UC2 = 20;
    public const int CV_32UC3 = 28;
    public const int CV_32UC4 = 36;

    /// <summary>
    /// Constructs a multi-channel Mat type from depth and channel count.
    /// Equivalent to OpenCV's CV_MAKETYPE macro.
    /// </summary>
    /// <param name="depth">Element depth (e.g., CV_8U, CV_32F, CV_64S)</param>
    /// <param name="channels">Number of channels (1-4)</param>
    /// <returns>Combined type value</returns>
    public static int MakeType(int depth, int channels)
    {
        if (channels < 1 || channels > 512)
            throw new ArgumentOutOfRangeException(nameof(channels), "Channels must be between 1 and 512");
        return (depth & 7) + ((channels - 1) << 3);
    }
}

public enum ColorConversionCodes
{
    BGRA2BGR = 1,
    BGR2GRAY = 6,
    RGB2GRAY = 7,
    BGR2RGB = 4,
    BGRA2RGBA = 3,
    BGR2RGBA = 12,
    BGR2BGRA = 0,
    GRAY2BGR = 8,
}

public enum ThresholdTypes
{
    Binary = 0,
    BinaryInv = 1,
    Trunc = 2,
    ToZero = 3,
    ToZeroInv = 4,
    Otsu = 8,
}

public enum InterpolationFlags
{
    Nearest = 0,
    Linear = 1,
    Cubic = 2,
    Area = 3,
    Lanczos4 = 4,
}

public enum TemplateMatchModes
{
    SqDiff = 0,
    SqDiffNormed = 1,
    CCorr = 2,
    CCorrNormed = 3,
    CCoeff = 4,
    CCoeffNormed = 5,
}

public enum BorderTypes
{
    Constant = 0,
    Replicate = 1,
    Reflect = 2,
    Wrap = 3,
    Reflect101 = 4,
    Default = 4,
}

public enum RetrievalModes
{
    External = 0,
    List = 1,
    CComp = 2,
    Tree = 3,
    FloodFill = 4,
}

public enum ContourApproximationModes
{
    None = 1,
    ApproxSimple = 2,
    ApproxTC89L1 = 3,
    ApproxTC89KCOS = 4,
}

public enum ImreadModes
{
    Unchanged = -1,
    Grayscale = 0,
    Color = 1,
    AnyDepth = 2,
    AnyColor = 4,
}

/// <summary>
/// Hershey 矢量字体（cv::HersheyFonts），用于 PutText/GetTextSize。
/// </summary>
public enum HersheyFonts
{
    HersheySimplex = 0,
    HersheyPlain = 1,
    HersheyDuplex = 2,
    HersheyComplex = 3,
    HersheyTriplex = 4,
    HersheyComplexSmall = 5,
    HersheyScriptSimplex = 6,
    HersheyScriptComplex = 7,
    /// <summary>常用别名，等价于 HersheySimplex。</summary>
    Simplex = 0,
}

public enum VideoCaptureAPIs
{
    ANY = 0,
    DSHOW = 700,
    MSMF = 1400,
    DC1394 = 300,
    V4L2 = 200,
    AVFOUNDATION = 1200,
}

public enum VideoCaptureProperties
{
    PosMsec = 0,
    PosFrames = 1,
    PosAVIRatio = 2,
    FrameWidth = 3,
    FrameHeight = 4,
    Fps = 5,
    FourCC = 6,
    FrameCount = 7,
    Format = 8,
    Mode = 9,
    Brightness = 10,
    Contrast = 11,
    Saturation = 12,
    Hue = 13,
    Gain = 14,
    Exposure = 15,
    ConvertRgb = 16,
    Backend = 42,
}

public static class FourCC
{
    public static int MJPG => MakeFourCC('M', 'J', 'P', 'G');

    public static int MakeFourCC(char c1, char c2, char c3, char c4)
    {
        return ((int)c1) | (((int)c2) << 8) | (((int)c3) << 16) | (((int)c4) << 24);
    }
}