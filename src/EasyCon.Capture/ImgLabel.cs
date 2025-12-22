using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCapture;

public record ImgLabel
{
    public SearchMethod searchMethod { get; set; } = SearchMethod.CCoeffNormed;
    public string ImgBase64 { get; set; } = string.Empty;

    public int RangeX { get; set; } = 0;
    public int RangeY { get; set; } = 0;
    public int RangeWidth { get; set; } = 0;
    public int RangeHeight { get; set; } = 0;

    public int TargetX { get; set; } = 0;
    public int TargetY { get; set; } = 0;
    public int TargetWidth { get; set; } = 0;
    public int TargetHeight { get; set; } = 0;

    [JsonIgnore]
    public string name { get; set; } = string.Empty;

    private Rectangle _round => new(RangeX, RangeY, RangeWidth, RangeHeight);
    private Rectangle _target => new(TargetX, TargetY, TargetWidth, TargetHeight);

    public Bitmap GetBitmap()
    {
        return this.Base64StringToImage(ImgBase64);
    }
}

static class ILExt
{
    public static ImgLabel Load(string path)
    {
        var temp = JsonSerializer.Deserialize<ImgLabel>(File.ReadAllText(path)) ?? throw new Exception();
        temp.name = Path.GetFileNameWithoutExtension(path);
        return temp;
    }

    public static void Save(this ImgLabel self, string path)
    {
        // save the imglabel to loc
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        File.WriteAllText(path + self.name + ".IL", JsonSerializer.Serialize(self));
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
        if (IsBase64String(basestr))
        {
            byte[] imageBytes = Convert.FromBase64String(basestr);
            using var ms = new MemoryStream(imageBytes, 0, imageBytes.Length);
            ms.Write(imageBytes, 0, imageBytes.Length);
            Image image = Image.FromStream(ms, true);
            return (Bitmap)image;
        }
        else
        {
            var bitmap = new Bitmap(200, 50);
            using Graphics g = Graphics.FromImage(bitmap);
            g.Clear(Color.White);

            // 设置字体和颜色
            var font = new Font("Arial", 20, FontStyle.Bold);
            Brush textBrush = Brushes.Black;

            // 设置字符串及其位置
            var point = new PointF(5, 20); // 文本位置（x, y）

            // 绘制文本
            g.DrawString(basestr, font, textBrush, point);
            return bitmap;
        }
    }
}
