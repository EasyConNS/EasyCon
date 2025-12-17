using System.Drawing.Imaging;
using System.IO;

namespace EasyCon2.Graphic;

internal static class ImgLabelExt
{
    public static List<SearchMethod> GetAllSearchMethod()
    {
        var list = new List<SearchMethod>
        {
            SearchMethod.SqDiffNormed,
            SearchMethod.CCorrNormed,
            SearchMethod.CCoeffNormed,
            SearchMethod.EdgeDetectXY
        };
        return list;
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

    public static Bitmap Base64StringToImage(this ImgLabel self, string basestr)
    {
        byte[] imageBytes = Convert.FromBase64String(basestr);
        var ms = new MemoryStream(imageBytes, 0, imageBytes.Length);
        ms.Write(imageBytes, 0, imageBytes.Length);
        Image image = Image.FromStream(ms, true);
        return (Bitmap)image;
    }
}
