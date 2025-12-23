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
    public string name { get; set; } = "5号路蛋屋主人";

    private Rectangle _round => new(RangeX, RangeY, RangeWidth, RangeHeight);
    private Rectangle _target => new(TargetX, TargetY, TargetWidth, TargetHeight);

    public Bitmap GetBitmap()
    {
        return Base64StringToImage(ImgBase64);
    }

    public void SetImage(Bitmap bitmap)
    {
        ImgBase64 = ImageToBase64(bitmap);
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

    private static Bitmap Base64StringToImage(string basestr)
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

    private static string ImageToBase64(Bitmap bmp)
    {
        try
        {
            var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
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

    public bool Valid()
    {
        if (ImgBase64.Length == 0) return false;
        return true;
    }

    public delegate Bitmap GetNewFrame();
    [NonSerialized]
    public GetNewFrame GetFrame;
    private Bitmap sourcePic;
    public Bitmap GetResultImg(Point p)
    {
        return sourcePic.Clone(new Rectangle(p.X, p.Y, TargetWidth, TargetHeight), sourcePic.PixelFormat);
    }
    public List<Point> Search(out double md)
    {
        if (TargetWidth > RangeWidth || TargetHeight > RangeHeight)
            throw new Exception("搜索图片大于搜索范围");

        using var ss = GetFrame();
        sourcePic?.Dispose();
        sourcePic = ss.Clone(_round, ss.PixelFormat);

        List<Point> result = new();
        if (searchMethod == SearchMethod.TesserDetect)
        {
            using var targetBmp = sourcePic.Clone(new Rectangle(TargetX - RangeX, TargetY - RangeY, TargetWidth, TargetHeight), sourcePic.PixelFormat);
            result = ECSearch.FindOCR(ImgBase64, targetBmp,out var rlttxt, out md);
        }
        else
        {
            result = ECSearch.FindPic(0, 0, TargetWidth, TargetHeight, sourcePic, GetBitmap(), searchMethod, out md);
        }
        md *= 100;

        // update the search pic
        //if (md >= _matchDegree)
        //{
        //    Debug.WriteLine("update img");
        //    searchImg = sourcePic.Clone(new Rectangle(result[0].X, result[0].Y, TargetWidth, TargetHeight), sourcePic.PixelFormat);
        //}

        return result;
    }

    public static ImgLabel Load(string path)
    {
        var temp = JsonSerializer.Deserialize<ImgLabel>(File.ReadAllText(path)) ?? throw new Exception();
        temp.name = Path.GetFileNameWithoutExtension(path);
        return temp;
    }
}

public static class ILExt
{
    public static void Save(this ImgLabel self, string path)
    {
        // save the imglabel to loc
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        if(self.searchMethod.ILTxtType())
        {
            // 文字标签手动编辑
            self.ImgBase64 = "";
        }

        File.WriteAllText($"{path}{self.name}.IL", JsonSerializer.Serialize(self));
    }

    private static bool ILTxtType(this SearchMethod method)
    {
        return method == SearchMethod.TesserDetect;
    }
}