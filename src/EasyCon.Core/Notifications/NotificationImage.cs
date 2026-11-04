#nullable enable

using OpenCvSharp;

namespace EasyCon.Core.Notifications;

/// <summary>将最新视频帧转换为通知图片；无视频帧时使用内嵌 EasyCon Logo。</summary>
public static class NotificationImage
{
    private static readonly Lazy<byte[]> _logo = new(CreateLogo);

    public static byte[] FromFrame(Mat? frame)
    {
        if (frame == null || frame.Empty())
            return _logo.Value;

        if (Math.Max(frame.Width, frame.Height) <= 1600)
            return Encode(frame);

        double scale = 1600.0 / Math.Max(frame.Width, frame.Height);
        using Mat resized = new();
        Cv2.Resize(frame, resized, new Size(), scale, scale, InterpolationFlags.Area);
        return Encode(resized);
    }

    private static byte[] Encode(Mat frame)
    {
        if (!Cv2.ImEncode(".jpg", frame, out byte[] image, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 88)]) || image.Length == 0)
            throw new InvalidOperationException("无法转换 QQ 通知图片。");
        return image;
    }

    private static byte[] CreateLogo()
    {
        using Stream stream = typeof(NotificationImage).Assembly.GetManifestResourceStream("EasyCon.Core.NotificationLogo.ico")
            ?? throw new InvalidOperationException("QQ 通知图片不可用，请检查 EasyCon Logo 资源。");
        using BinaryReader reader = new(stream);
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1)
            throw new InvalidOperationException("EasyCon Logo 资源格式无效。");

        int count = reader.ReadUInt16();
        byte[]? png = null;
        int largestArea = 0;
        for (int i = 0; i < count; i++)
        {
            stream.Position = 6 + (16 * i);
            int width = reader.ReadByte();
            int height = reader.ReadByte();
            stream.Position += 6;
            uint length = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();
            int area = (width == 0 ? 256 : width) * (height == 0 ? 256 : height);
            if (area <= largestArea || length < 8 || (long)offset + length > stream.Length)
                continue;

            stream.Position = offset;
            byte[] entry = reader.ReadBytes(checked((int)length));
            if (!entry.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                continue;
            png = entry;
            largestArea = area;
        }

        if (png == null)
            throw new InvalidOperationException("EasyCon Logo 中缺少通知所需的 PNG 图片。");
        using Mat logo = Cv2.ImDecode(png, ImreadModes.Color);
        return Encode(logo);
    }
}