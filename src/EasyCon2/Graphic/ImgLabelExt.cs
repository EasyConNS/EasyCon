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

    public static IEnumerable<SearchMethod> GetAllSearchMethod()
    {
        return
        [
            SearchMethod.SqDiffNormed,
            SearchMethod.CCorrNormed,
            SearchMethod.CCoeffNormed,
            //SearchMethod.EdgeDetectXY,
            //SearchMethod.EdgeDetectLaplacian,
        ];
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
