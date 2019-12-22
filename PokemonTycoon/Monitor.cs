using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonTycoon
{
    static class Monitor
    {
        static readonly object _lock = new object();
        static readonly Bitmap _unitBuffer = new Bitmap(1, 1);
        static Bitmap _captured = null;
        
        public delegate bool Sampler(int x, int y, Func<int, int, bool> f);
        public static readonly Sampler DefaultSampler;
        public static readonly Func<int, Sampler> LineSampler;
        public static readonly Func<int, Sampler> PointSampler;

        public static int ScreenIndex { get; private set; }

        static Monitor()
        {
            ScreenIndex = Array.FindIndex(Screen.AllScreens, sc => sc == Screen.PrimaryScreen);
            DefaultSampler = (w, h, f) =>
            {
                bool b = true;
                for (int x = 0; x < w && b; x++)
                    for (int y = 0; y < h && b; y++)
                        b = b && f(x, y);
                return b;
            };
            LineSampler = n =>
            {
                return (w, h, f) =>
                {
                    int sx = w >= n ? w / n : 1;
                    int sy = h >= n ? h / n : 1;
                    bool b = true;
                    for (int x = w / n; x < w && b; x += sx)
                        for (int y = 0; y < h && b; y++)
                            b = b && f(x, y);
                    for (int x = 0; x < w && b; x++)
                        for (int y = h / n; y < h && b; y += sy)
                            b = b && f(x, y);
                    return b;
                };
            };
            PointSampler = n =>
            {
                return (w, h, f) =>
                {
                    int sx = w >= n ? w / n : 1;
                    int sy = h >= n ? h / n : 1;
                    bool b = true;
                    for (int x = w / n; x < w && b; x += sx)
                        for (int y = h / n; y < h && b; y += sy)
                            b = b && f(x, y);
                    return b;
                };
            };
        }

        public static void ChangeScreen()
        {
            lock (_lock)
            {
                ScreenIndex++;
                if (ScreenIndex >= Screen.AllScreens.Length)
                    ScreenIndex = 0;
            }
        }

        public static Bitmap ScreenShot()
        {
            Bitmap image;
            var scn = Screen.AllScreens[ScreenIndex];
            image = new Bitmap(scn.Bounds.Width, scn.Bounds.Height);
            using (var g = Graphics.FromImage(image))
                g.CopyFromScreen(scn.Bounds.Location, Point.Empty, scn.Bounds.Size);
            return image;
        }

        public static void Capture()
        {
            lock (_lock)
            {
                var scn = Screen.AllScreens[ScreenIndex];
                _captured = new Bitmap(scn.Bounds.Width, scn.Bounds.Height);
                using (var g = Graphics.FromImage(_captured))
                    g.CopyFromScreen(scn.Bounds.Location, Point.Empty, scn.Bounds.Size);
            }
        }

        public static void Release()
        {
            lock (_lock)
            {
                _captured = null;
            }
        }

        static Bitmap _GetImage(int x, int y, int w, int h)
        {
            lock (_lock)
            {
                Bitmap buffer;
                if (w == 1 && h == 1)
                    buffer = _unitBuffer;
                else
                    buffer = new Bitmap(w, h);
                Rectangle rect = new Rectangle(x, y, w, h);
                using (var g = Graphics.FromImage(buffer))
                {
                    if (_captured != null)
                        g.DrawImage(_captured, 0, 0, rect, GraphicsUnit.Pixel);
                    else
                        g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
                }
                return buffer;
            }
        }

        public static Color GetColor(int x, int y)
        {
            return _GetImage(x, y, 1, 1).GetPixel(0, 0);
        }

        public static Bitmap GetImage(int x, int y, int w, int h)
        {
            return _GetImage(x, y, w, h);
        }

        public static Shadow GetShadow()
        {
            return new Shadow(GetImage(Shadow.X, Shadow.Y, Shadow.W, Shadow.H));
        }

        public static List<Point> SearchColor(int x, int y, int w, int h, Color color, double cap = 1)
        {
            lock (_lock)
            {
                List<Point> list = new List<Point>();
                var src = _GetImage(x, y, w, h);
                for (int xs = 0; xs < w; xs++)
                    for (int ys = 0; ys < h; ys++)
                        if (src.GetPixel(xs, ys).Compare(color) >= cap)
                            list.Add(new Point(x + xs, y + ys));
                return list;
            }
        }

        public static List<Point> SearchImage(int x, int y, int w, int h, Bitmap image, double cap = 1, Sampler sampler = null)
        {
            lock (_lock)
            {
                sampler = sampler ?? DefaultSampler;
                List<Point> list = new List<Point>();
                var src = _GetImage(x, y, w, h);
                for (int xs = 0; xs + image.Width - 1 < w; xs++)
                    for (int ys = 0; ys + image.Height - 1 < h; ys++)
                    {
                        double c = 1;
                        bool success = sampler.Invoke(image.Width, image.Height, (xt, yt) =>
                        {
                            double _c = src.GetPixel(xs + xt, ys + yt).Compare(image.GetPixel(xt, yt));
                            if (_c < c)
                                c = _c;
                            return c >= cap;
                        });
                        if (success)
                            list.Add(new Point(x + xs, y + ys));
                    }
                return list;
            }
        }

        public static bool Match(int x, int y, Color color, double cap = 1)
        {
            return SearchColor(x, y, 1, 1, color, cap).Count > 0;
        }

        public static bool Match(int x, int y, Bitmap image, double cap = 1, Sampler sampler = null)
        {
            return SearchImage(x, y, image.Width, image.Height, image, cap, sampler).Count > 0;
        }

        public static Shadow MatchShadow()
        {
            return Shadow.Find(GetShadow());
        }
    }
}
