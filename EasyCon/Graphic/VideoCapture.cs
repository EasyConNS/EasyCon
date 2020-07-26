using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;

namespace EasyCon.Graphic
{
    public static class VideoCapture
    {
        static readonly object _lock = new object();

        public delegate bool Sampler(int x, int y, Func<int, int, bool> f);
        public static readonly Sampler DefaultSampler;
        public static readonly Func<int, Sampler> LineSampler;
        public static readonly Func<int, Sampler> PointSampler;

        public static VideoCaptureDevice VideoSource { get; private set; }
        public delegate void VideoSourceChangedHandler();
        public static event VideoSourceChangedHandler VideoSourceChanged;
        public static int CameraIndex { get; private set; } = -1;
        public static int ScreenIndex { get; private set; } = -1;
        static Bitmap _imageBuffer;
        static Bitmap _image;
        static int _freezed = 0;

        static VideoCapture()
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

        public static void CaptureCamera(int? index = null)
        {
            lock (_lock)
            {
                VideoSource?.SignalToStop();
                CameraIndex = index ?? CameraIndex + 1;
                ScreenIndex = -1;
                var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (CameraIndex >= videoDevices.Count)
                    CameraIndex = 0;
                VideoSource = new VideoCaptureDevice(videoDevices[CameraIndex].MonikerString);
                // there is a problem,maybe some capture device could not support 1080p
                // it could make the output pic low resolution
                //foreach(var vc in VideoSource.VideoCapabilities)
                //{
                //    Debug.WriteLine(vc.FrameSize.ToString());
                //}
                VideoSource.VideoResolution = VideoSource.VideoCapabilities[0];
                Debug.WriteLine(VideoSource.VideoResolution.FrameSize.ToString());
                //VideoSource.NewFrame += NewFrameHandler;
                VideoSource.Start();
                VideoSourceChanged?.Invoke();
            }
        }

        //public static void CaptureScreen(int? index = null)
        //{
        //    lock (_lock)
        //    {
        //        VideoSource?.SignalToStop();
        //        ScreenIndex = index ?? ScreenIndex + 1;
        //        CameraIndex = -1;
        //        if (ScreenIndex >= Screen.AllScreens.Length)
        //            ScreenIndex = 0;
        //        VideoSource = new ScreenCaptureStream(Screen.AllScreens[ScreenIndex].Bounds);
        //        VideoSource.NewFrame += NewFrameHandler;
        //        VideoSource.Start();
        //        VideoSourceChanged?.Invoke();
        //    }
        //}

        public static List<string> GetCaptureCamera()
        {
            var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            List<string> names = new List<string>();
            foreach(FilterInfo dev in videoDevices)
            {
                names.Add(dev.Name);
                Debug.WriteLine(dev.Name);
                Debug.WriteLine(dev.MonikerString);
            }

            return names;
        }

        static void NewFrameHandler(object sender, NewFrameEventArgs eventArgs)
        {
            if (Monitor.TryEnter(_lock))
            {
                try
                {
                    if (_freezed > 0 && _imageBuffer != null)
                        return;
                    _imageBuffer?.Dispose();
                    _imageBuffer = eventArgs.Frame.Clone() as Bitmap;
                    if (_freezed <= 0)
                    {
                        _image?.Dispose();
                        _image = null;
                    }
                }
                finally
                {
                    Monitor.Exit(_lock);
                }
            }
        }

        static void PrepareImage()
        {
            lock (_lock)
            {
                if (_imageBuffer == null || _image != null)
                    return;
                _image?.Dispose();
                _image = new Bitmap(_imageBuffer);
            }
        }

        public static Bitmap ScreenShot()
        {
            lock (_lock)
            {
                PrepareImage();
                if (_image == null)
                    return null;
                return new Bitmap(_image);
            }
        }

        public static void Freeze()
        {
            lock (_lock)
            {
                _freezed++;
                PrepareImage();
            }
        }

        public static void Unfreeze()
        {
            lock (_lock)
            {
                _freezed--;
            }
        }

        public static Color GetColor(int x, int y)
        {
            lock (_lock)
            {
                PrepareImage();
                return _image.GetPixel(x, y);
            }
        }

        public static Bitmap GetImage(int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0)
                return null;
            lock (_lock)
            {
                PrepareImage();
                Bitmap img = new Bitmap(w, h);
                Rectangle rect = new Rectangle(x, y, w, h);
                if (_image == null)
                    return null;

                using (var g = Graphics.FromImage(img))
                    g.DrawImage(_image, 0, 0, rect, GraphicsUnit.Pixel);
                return img;


            }
        }

        public static List<Color> GetPixels(int x, int y, int w, int h)
        {
            var img = GetImage(x, y, w, h);
            List<Color> list = new List<Color>();
            for (int i = 0; i < img.Width; i++)
                for (int j = 0; j < img.Height; j++)
                    list.Add(img.GetPixel(i, j));
            return list;
        }

        public static List<Point> SearchColor(int x, int y, int w, int h, Color color, double cap = 1)
        {
            lock (_lock)
            {
                PrepareImage();
                List<Point> list = new List<Point>();
                for (int xs = 0; xs < w; xs++)
                    for (int ys = 0; ys < h; ys++)
                        if (_image.GetPixel(x + xs, y + ys).Compare(color) >= cap)
                            list.Add(new Point(x + xs, y + ys));
                return list;
            }
        }

        public static List<Point> SearchImage(int x, int y, int w, int h, Bitmap image, double cap = 1, params Sampler[] samplers)
        {
            lock (_lock)
            {
                PrepareImage();
                if (samplers.Length == 0)
                    samplers = new Sampler[] { DefaultSampler };
                List<Point> list = new List<Point>();
                for (int xs = 0; xs + image.Width - 1 < w; xs++)
                    for (int ys = 0; ys + image.Height - 1 < h; ys++)
                    {
                        double c = 1;
                        bool success = true;
                        foreach (var f in samplers)
                        {
                            success = f.Invoke(image.Width, image.Height, (xt, yt) =>
                            {
                                double _c = _image.GetPixel(x + xs + xt, y + ys + yt).Compare(image.GetPixel(xt, yt));
                                if (_c < c)
                                    c = _c;
                                return c >= cap;
                            });
                            if (!success)
                                break;
                        }
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

        public static bool Match(int x, int y, Bitmap image, double cap = 1, params Sampler[] samplers)
        {
            return SearchImage(x, y, image.Width, image.Height, image, cap, samplers).Count > 0;
        }

        public static Shadow[] GetMatchedShadows()
        {
            return Shadow.Find(new Shadow(GetImage(Shadow.X, Shadow.Y, Shadow.W, Shadow.H)));
        }
    }
}
