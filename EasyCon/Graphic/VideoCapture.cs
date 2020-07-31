using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AForge.Video.DirectShow;
using OpenCvSharp;

namespace EasyCon.Graphic
{
    public static class VideoCapture
    {
        static readonly object _lock = new object();
        static Bitmap _image;
        private static OpenCvSharp.VideoCapture capture;
        private static Thread capture_run_handler;
        private static System.Drawing.Point curResolution = new System.Drawing.Point(1920, 1080);

        public static void CaptureCamera(int index = 0)
        {

            capture = new OpenCvSharp.VideoCapture(index, VideoCaptureAPIs.DSHOW);
            capture.Set(VideoCaptureProperties.FrameWidth, curResolution.X);
            capture.Set(VideoCaptureProperties.FrameHeight, curResolution.Y);
            capture.Set(VideoCaptureProperties.Fps, 60);

            capture_run_handler = new Thread(Capture_Frame);
            capture_run_handler.Start();
        }

        public static void Close()
        {
            capture_run_handler?.Abort();
            if (!capture.IsDisposed)
            {
                capture?.Release();
                capture?.Dispose();
            }
        }

        public static List<string> GetCaptureCamera()
        {
            var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            List<string> devs = new List<string>();
            foreach (FilterInfo dev in videoDevices)
            {
                devs.Add(dev.Name);
                Debug.WriteLine(dev.Name);
                Debug.WriteLine(dev.MonikerString);
            }

            return devs;
        }

        public static void SetResolution(System.Drawing.Point res)
        {
            if(curResolution.X != res.X || curResolution.Y != res.Y)
            {
                lock (_lock)
                {
                    curResolution = res;
                    capture_run_handler.Suspend();
                    capture.Set(VideoCaptureProperties.FrameWidth, curResolution.X);
                    capture.Set(VideoCaptureProperties.FrameHeight, curResolution.Y);
                    Thread.Sleep(500);
                    capture_run_handler.Resume();
                }
            }
        }

        static void Capture_Frame()
        {
            while (true)
            {
                if (Monitor.TryEnter(_lock))
                {
                    try
                    {
                        using (var frameMat = capture.RetrieveMat())
                        {
                            if (!frameMat.Empty())
                            {
                                _image?.Dispose();
                                _image = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frameMat);
                            }
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_lock);
                    }
                }
                Thread.Sleep(16);
            }
        }

        public static Bitmap GetImage(Rectangle range)
        {
            if (range.Width <= 0 || range.Height <= 0)
                return null;

            lock (_lock)
            {
                Bitmap img = new Bitmap(range.Width,range.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(img))
                    g.DrawImage(_image, 0, 0, range, GraphicsUnit.Pixel);
                return img;
            }
        }
        public static Bitmap GetImage()
        {
            lock (_lock)
            {
                Bitmap img = new Bitmap(_image.Width, _image.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(img))
                    g.DrawImage(_image, 0, 0, new Rectangle(0,0, _image.Width, _image.Height), GraphicsUnit.Pixel);
                return img;
            }
        }

    }
}
