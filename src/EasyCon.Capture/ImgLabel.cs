using EzCv;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Capture;

public record ImgLabel
{
    public SearchMethod searchMethod { get; set; } = SearchMethod.CCoeffNormed;

    private string _imgBase64 = string.Empty;
    public string ImgBase64
    {
        get => _imgBase64;
        set
        {
            _imgBase64 = value;
            InvalidateTargetCache();
        }
    }

    /// <summary>缓存的 BGR 目标 Mat，惰性解码，生命周期由 ImgLabel 管理。</summary>
    [JsonIgnore]
    private Mat? _cachedMat;

    /// <summary>缓存的 RGBA 目标 Mat（MaskedSqDiffNormed 路径使用），惰性解码。</summary>
    [JsonIgnore]
    private Mat? _cachedMatRGBA;

    public int RangeX { get; set; } = 0;
    public int RangeY { get; set; } = 0;
    public int RangeWidth { get; set; } = 0;
    public int RangeHeight { get; set; } = 0;

    public int TargetX { get; set; } = 0;
    public int TargetY { get; set; } = 0;
    public int TargetWidth { get; set; } = 0;
    public int TargetHeight { get; set; } = 0;

    public bool UseGrayscale { get; set; } = false;
    public bool UseBinary { get; set; } = false;
    public bool UseGaussianBlur { get; set; } = false;
    public bool UseOther { get; set; } = false;

    [JsonIgnore]
    public string name { get; set; } = "5号路蛋屋主人";

    [JsonIgnore]
    public string path { get; set; } = "";

    internal Rect _round => new(RangeX, RangeY, RangeWidth, RangeHeight);
    internal Rect _target => new(TargetX, TargetY, TargetWidth, TargetHeight);

    private Image _image;

    public Image GetImage() => _image ??= Base64StringToImage(ImgBase64, searchMethod);

    public void SetImage(Image img)
    {
        if (!searchMethod.IsImageMethod()) return;
        ImgBase64 = ImageToBase64(img);
        _image = null;
    }

    /// <summary>
    /// 获取缓存的 BGR 目标 Mat。首次调用时从 ImgBase64 解码，后续直接返回缓存。
    /// </summary>
    internal Mat GetCachedTargetMat()
    {
        if (_cachedMat is { } cached)
            return cached;
        byte[] imageBytes = Convert.FromBase64String(ImgBase64);
        _cachedMat = imageBytes.ToMat(); // ImreadModes.Color → BGR
        return _cachedMat;
    }

    /// <summary>
    /// 获取缓存的 RGBA 目标 Mat（MaskedSqDiffNormed 路径使用）。
    /// </summary>
    internal Mat GetCachedTargetMatRGBA()
    {
        if (_cachedMatRGBA is { } cached)
            return cached;
        byte[] imageBytes = Convert.FromBase64String(ImgBase64);
        _cachedMatRGBA = Cv2.ImDecode(imageBytes, ImreadModes.Unchanged);
        return _cachedMatRGBA;
    }

    /// <summary>
    /// 释放缓存的目标 Mat。ImgBase64 变更时自动调用。
    /// </summary>
    internal void InvalidateTargetCache()
    {
        _cachedMat?.Dispose();
        _cachedMat = null;
        _cachedMatRGBA?.Dispose();
        _cachedMatRGBA = null;
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

    private static Image Base64StringToImage(string basestr, SearchMethod method)
    {
        if (IsBase64String(basestr) && method.IsImageMethod())
        {
            byte[] imageBytes = Convert.FromBase64String(basestr);
            using var ms = new MemoryStream(imageBytes);
            using var image = Image.FromStream(ms, true, true);
            return new Bitmap(image);
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
        var temp = JsonSerializer.Deserialize<ImgLabel>(File.ReadAllText(path)) ?? throw new Exception("标签解析失败");
        temp.name = Path.GetFileNameWithoutExtension(path);
        temp.path = Path.GetDirectoryName(path) ?? string.Empty;
        return temp;
    }
}

public static class ILExt
{
    public static void Save(this ImgLabel self, string path)
    {
        if (self.path != "")
        {
            path = self.path;
        }
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

        File.WriteAllText($"{Path.Combine(path, self.name)}.IL", JsonSerializer.Serialize(self));
    }

    private static bool ILTxtType(this SearchMethod method)
    {
        return method == SearchMethod.TesserDetect;
    }
}