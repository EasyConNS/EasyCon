using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.IO;

namespace PokemonTycoon.Graphic
{
    public static class ImageRes
    {
        public const string ImagePath = @"Image\";
        public const string ImageExtension = ".png";

        static readonly Dictionary<string, Bitmap> _dict = new Dictionary<string, Bitmap>();

        public static readonly Bitmap PictureBoxBackground;

        static ImageRes()
        {
            PictureBoxBackground = _GetPictureBoxBackground();
        }

        static Bitmap _GetPictureBoxBackground()
        {
            int w = 20, h = 20;
            Bitmap img = new Bitmap(w, h);
            using (var g = Graphics.FromImage(img))
            {
                g.Clear(Color.Black);
                var b = new SolidBrush(Color.FromArgb(40, 40, 40));
                g.FillRectangle(b, 0, 0, w / 2, h / 2);
                g.FillRectangle(b, w / 2, h / 2, w, h);
            }
            return img;
        }

        public static string GetImagePath(string name)
        {
            return $"{ImagePath}{name}.png";
        }

        public static IEnumerable<FileInfo> GetImages()
        {
            return new DirectoryInfo(ImagePath).GetFiles().Where(fi => fi.Extension.Equals(ImageExtension, StringComparison.OrdinalIgnoreCase));
        }

        public static Bitmap Get(string name)
        {
            try
            {
                if (!_dict.ContainsKey(name))
                    _dict[name] = Image.FromFile(GetImagePath(name)) as Bitmap;
                return _dict[name];
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }
}
