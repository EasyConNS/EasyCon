using System.Collections.Immutable;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;

namespace EasyCon2.Graphic;

internal static class ImgLabelExt
{
    public static IEnumerable<ImgLabel> LoadIL(string dir)
    {
        var imgLabels = ImmutableArray.CreateBuilder<ImgLabel>();

        foreach (var file in Directory.GetFiles(dir, "*.IL"))
        {
            try
            {
                var il = ImgLabel.Load(file);
                //il.Refresh(() => GetImage());
                imgLabels.Add(il);
            }
            catch
            {
                Debug.WriteLine("无法加载标签:", file);
            }
        }
        return imgLabels.ToImmutable();
    }

    public static string ImageToBase64(this ImgLabel self, Bitmap bmp)
    {
        try
        {
            var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Bmp);
            byte[] arr = new byte[ms.Length];
            ms.Position = 0;
            ms.Read(arr, 0, (int)ms.Length);
            ms.Close();
            string strbaser64 = Convert.ToBase64String(arr);
            return strbaser64;
        }
        catch (Exception ex)
        {
            return "err!!" + ex.Message;
        }
    }
    private static bool IsBase64String(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Length % 4 != 0) return false;

        try
        {
            // 尝试转换验证
            _ = Convert.FromBase64String(s);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Bitmap Base64StringToImage(this ImgLabel self, string basestr)
    {
        if(IsBase64String(basestr))
        {
            byte[] imageBytes = Convert.FromBase64String(basestr);
            using var ms = new MemoryStream(imageBytes, 0, imageBytes.Length);
            ms.Write(imageBytes, 0, imageBytes.Length);
            Image image = Image.FromStream(ms, true);
            return (Bitmap)image;
        }
        else
        {
            var bitmap = new Bitmap(200,50);
            using Graphics g = Graphics.FromImage(bitmap);
            g.Clear(Color.White);

            // 设置字体和颜色
            var font = new Font("Arial", 12, FontStyle.Bold);
            Brush textBrush = Brushes.Black;

            // 设置字符串及其位置
            var point = new PointF(5, 20); // 文本位置（x, y）

            // 绘制文本
            g.DrawString(basestr, font, textBrush, point);
            return bitmap;
        }
    }
}
