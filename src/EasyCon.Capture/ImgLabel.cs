using OpenCvSharp;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;
using Point = System.Drawing.Point;

namespace EasyCon.Capture;

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

    internal Rect _round => new(RangeX, RangeY, RangeWidth, RangeHeight);
    internal Rect _target => new(TargetX, TargetY, TargetWidth, TargetHeight);

    private Image _image;

    public Image GetImage() => _image ??= Base64StringToImage(ImgBase64);

    public void SetImage(Image img)
    {
        if (!searchMethod.IsImageMethod()) return;
        ImgBase64 = ImageToBase64(img);
        _image = null;
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

    private static Image Base64StringToImage(string basestr)
    {
        if (IsBase64String(basestr))
        {
            byte[] imageBytes = Convert.FromBase64String(basestr);
            using var ms = new MemoryStream(imageBytes, 0, imageBytes.Length);
            ms.Write(imageBytes, 0, imageBytes.Length);
            return Image.FromStream(ms, true);
        }
        else
        {
            //using var txtMat = new Mat(200,200, MatType.CV_8UC3,Scalar.White);
            ////using (new Window("结果1", txtMat))
            ////{
            ////    Cv2.WaitKey();
            ////}
            //txtMat.PutText(basestr, new(5,5), HersheyFonts.HersheyTriplex, 0.8, Scalar.Black);
            //return BitmapConverter.ToBitmap(txtMat);
            var txtImg = new Bitmap(200, 50);
            using Graphics g = Graphics.FromImage(txtImg);
            g.Clear(Color.White);

            // 设置字体和颜色
            var font = new Font("Arial", 20, FontStyle.Bold);
            Brush textBrush = Brushes.Black;

            // 设置字符串及其位置
            var point = new PointF(5, 20); // 文本位置（x, y）

            // 绘制文本
            g.DrawString(basestr, font, textBrush, point);
            return txtImg;
        }
    }

    private static string ImageToBase64(Image bmp)
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
        if (ImgBase64.Length == 0 && searchMethod.IsImageMethod()) return false;
        return true;
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

        if (self.searchMethod.ILTxtType())
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

    public static List<Point> Search(this ImgLabel self, Mat ss, out double md)
    {
        if (self.TargetWidth > self.RangeWidth || self.TargetHeight > self.RangeHeight)
            throw new Exception("搜索图片大于搜索范围");

        try
        {
            // 从原始Bitmap中绘制裁剪区域到新的Bitmap对象
            using var range = new Mat(ss, self._round);
            //#if DEBUG
            //using (new Window("结果1", range))
            //{
            //    Cv2.WaitKey();
            //}
            //#endif
            List<Point> result = new();
            if (self.searchMethod == SearchMethod.TesserDetect)
            {
                using var target = new Mat(ss, self._target);
                var rlttxt = ECSearch.FindOCR(self.ImgBase64, target, out md);
                result = [new Point(self.TargetX - self.RangeX, self.TargetY- self.RangeY)];
            }
            else
            {
                byte[] imageBytes = Convert.FromBase64String(self.ImgBase64);
                using var target = imageBytes.ToMat();
                result = ECSearch.FindPic(range, target, self.searchMethod, out md);
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
        catch (OpenCVException ex)
        {
            throw new Exception($"搜图标签[{self.name}]执行异常：{ex.Message}");
        }
    }
}