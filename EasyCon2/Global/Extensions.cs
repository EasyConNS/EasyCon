using EasyCon2.Graphic;
using System.IO;

namespace EasyCon2.Global
{
    static class Extensions
    {
        public static double Compare(this Color self, Color color)
        {
            return 1 - (Math.Abs(self.R - color.R) + Math.Abs(self.G - color.G) + Math.Abs(self.B - color.B)) / 255.0 / 3;
        }

        public static string GetName(this Enum self)
        {
            return Enum.GetName(self.GetType(), self);
        }

        public static string ImageToBase64(this ImgLabel self, Bitmap bmp)
        {
            try
            {
                var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
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
}
