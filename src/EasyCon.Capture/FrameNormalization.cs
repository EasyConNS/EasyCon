using OpenCvSharp;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Capture;

/// <summary>明确的采集变换。非 16:9 输入需要配置裁剪，不隐式拉伸或猜测黑边。</summary>
public sealed record FrameNormalization([property: JsonConverter(typeof(FrameCropJsonConverter))] Rect? Crop = null, int Rotation = 0,
    InterpolationFlags Interpolation = InterpolationFlags.Nearest)
{
    public const int Width = 1920;
    public const int Height = 1080;
    public string Id => FormattableString.Invariant($"bgr8-1080p-v1:{Crop?.X},{Crop?.Y},{Crop?.Width},{Crop?.Height}:{Rotation}:{(int)Interpolation}");

    public Mat Apply(Mat input)
    {
        if (input.Empty()) throw new ArgumentException("采集帧为空", nameof(input));
        if (Rotation is not (0 or 90 or 180 or 270))
            throw new ArgumentException("旋转只支持 0/90/180/270 度");
        Rect crop = Crop ?? new Rect(0, 0, input.Width, input.Height);
        if (crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 ||
            (long)crop.X + crop.Width > input.Width || (long)crop.Y + crop.Height > input.Height)
            throw new ArgumentException("采集裁剪范围超出输入画面");
        using Mat region = new(input, crop);
        using Mat converted = new();
        Mat bgr = region;
        if (input.Type() == MatType.CV_8UC3) { }
        else if (input.Type() == MatType.CV_8UC1) { Cv2.CvtColor(region, converted, ColorConversionCodes.GRAY2BGR); bgr = converted; }
        else if (input.Type() == MatType.CV_8UC4) { Cv2.CvtColor(region, converted, ColorConversionCodes.BGRA2BGR); bgr = converted; }
        else throw new ArgumentException("采集只接受 8 位灰度、BGR 或 BGRA 帧");
        using Mat rotated = new();
        if (Rotation != 0) Cv2.Rotate(bgr, rotated, Rotation switch
        {
            90 => RotateFlags.Rotate90Clockwise,
            180 => RotateFlags.Rotate180,
            _ => RotateFlags.Rotate90Counterclockwise,
        });
        Mat canonical = Rotation == 0 ? bgr : rotated;
        if ((long)canonical.Width * Height != (long)canonical.Height * Width)
            throw new ArgumentException("采集画面不是 16:9，请明确配置裁剪或旋转");
        if (canonical.Width == Width && canonical.Height == Height) return canonical.Clone();
        Mat result = new();
        try
        {
            Cv2.Resize(canonical, result, new Size(Width, Height), 0, 0, Interpolation);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}

/// <summary>OpenCvSharp.Rect 使用字段，配置显式序列化，避免默认 JSON 丢失裁剪坐标。</summary>
public sealed class FrameCropJsonConverter : JsonConverter<Rect?>
{
    public override Rect? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement value = document.RootElement;
        int Get(string camel, string pascal) => (value.TryGetProperty(camel, out JsonElement field) ? field : value.GetProperty(pascal)).GetInt32();
        return new Rect(Get("x", "X"), Get("y", "Y"), Get("width", "Width"), Get("height", "Height"));
    }

    public override void Write(Utf8JsonWriter writer, Rect? value, JsonSerializerOptions options)
    {
        if (value == null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteNumber("x", value.Value.X);
        writer.WriteNumber("y", value.Value.Y);
        writer.WriteNumber("width", value.Value.Width);
        writer.WriteNumber("height", value.Value.Height);
        writer.WriteEndObject();
    }
}