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
    public const int CV_8UC1 = 0;
    public const int CV_8UC3 = 16;
    public const int CV_8UC4 = 24;
    public const int CV_16S = 3;
    public const int CV_16SC1 = 3;
    public const int CV_32F = 5;
    public const int CV_32FC1 = 5;
    public const int CV_64F = 6;
}

public enum ColorConversionCodes
{
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