using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EasyCon2.Avalonia.Converters;
using EasyCon2.Avalonia.Models;
using System.Globalization;

namespace EasyCon2.Avalonia.UiTests;

/// <summary>画板预览：已解码像素直接拷进位图，不经 PNG 编解码。</summary>
[TestFixture]
public class PixelImageToBitmapConverterTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (Application.Current == null)
            TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
    }

    [Test]
    public void Convert_CopiesEveryPixelRow()
    {
        byte[] rgba = new byte[3 * 2 * 4];
        for (int i = 0; i < rgba.Length; i++)
            rgba[i] = (byte)(i * 7 + 1);
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        PixelImage image = new(3, 2, rgba);

        object? result = PixelImageToBitmapConverter.Instance.Convert(image, typeof(Bitmap), null, CultureInfo.InvariantCulture);

        Assert.That(result, Is.InstanceOf<WriteableBitmap>());
        WriteableBitmap bitmap = (WriteableBitmap)result!;
        Assert.That(bitmap.PixelSize, Is.EqualTo(new PixelSize(3, 2)));
        using ILockedFramebuffer buffer = bitmap.Lock();
        byte[] row = new byte[3 * 4];
        for (int y = 0; y < 2; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, row.Length);
            Assert.That(row, Is.EqualTo(rgba.Skip(y * row.Length).Take(row.Length).ToArray()), $"第 {y} 行");
        }
    }

    [Test]
    public void Convert_NonImage_ReturnsNull()
    {
        Assert.That(PixelImageToBitmapConverter.Instance.Convert(null, typeof(Bitmap), null, CultureInfo.InvariantCulture), Is.Null);
    }
}