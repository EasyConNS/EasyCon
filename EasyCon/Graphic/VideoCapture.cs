using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using SharpDX.MediaFoundation;

namespace EasyCon.Graphic
{
    public static class VideoCapture
    {
        static readonly object _lock = new object();
        static Bitmap _image;
        private static OpenCvSharp.VideoCapture capture;
        private static Thread capture_run_handler;
        private static System.Drawing.Point curResolution = new System.Drawing.Point(1920, 1080);
        private static List<string> devs = new List<string>();
        private static int dev_index = 0;
        private static int dev_type = 0;
        private static Dictionary<string, int> captureTypes = new Dictionary<string, int>();
        private static PictureBox displayUI;
        public static void CaptureCamera(PictureBox pictureBox, int index = 0, int typeId = 0)
        {
            displayUI = pictureBox;
            dev_index = index;
            dev_type = typeId;
            //if (devs[index].Equals("TCUB90"))
            //    capture = new OpenCvSharp.VideoCapture(dev_index, VideoCaptureAPIs.DSHOW);
            //else
            capture = new OpenCvSharp.VideoCapture(dev_index, (VideoCaptureAPIs)dev_type);
            if (!capture.IsOpened())
            {
                Close();
                MessageBox.Show("当前采集卡已经被其他程序打开，请先关闭后再尝试");
                return;
            }

            capture.Set(VideoCaptureProperties.FrameWidth, curResolution.X);
            capture.Set(VideoCaptureProperties.FrameHeight, curResolution.Y);
            capture.Set(VideoCaptureProperties.Fps, 60);

            Debug.WriteLine(capture.Get(VideoCaptureProperties.Mode));
            Debug.WriteLine(capture.Get(VideoCaptureProperties.FourCC));
            Debug.WriteLine(capture.Get(VideoCaptureProperties.Backend));
            Debug.WriteLine(capture.Get(VideoCaptureProperties.Fps));

            capture_run_handler = new Thread(Capture_Frame);
            capture_run_handler.Start();
        }

        public static void Close()
        {
            if (capture == null)
                return;
            lock (_lock)
            {
                capture_run_handler?.Abort();
                if (!capture.IsDisposed)
                {
                    capture?.Release();
                    capture?.Dispose();
                }
            }
        }

        public static Dictionary<string, int> GetCaptureTypes()
        {
            System.Array values = System.Enum.GetValues(typeof(VideoCaptureAPIs));
            foreach (var value in values)
            {
                Debug.WriteLine(value + "--" + (int)value);//获取名称和值
                if (captureTypes.ContainsKey(value.ToString()))
                    continue;
                captureTypes.Add(value.ToString(), (int)value);
            }
            return captureTypes;
        }

        public static List<string> GetCaptureCamera()
        {
            devs.Clear();
            var attributes = new MediaAttributes(1);
            attributes.Set(CaptureDeviceAttributeKeys.SourceType.Guid, CaptureDeviceAttributeKeys.SourceTypeVideoCapture.Guid);
            var devices = MediaFactory.EnumDeviceSources(attributes);
            for (var i = 0; i < devices.Count(); i++)
            {
                var friendlyName = devices[i].Get(CaptureDeviceAttributeKeys.FriendlyName);
                devs.Add(friendlyName);
                Debug.WriteLine(friendlyName);
            }
            return devs;
        }

        public static void SetResolution(System.Drawing.Point res)
        {
            if (curResolution.X != res.X || curResolution.Y != res.Y)
            {
                lock (_lock)
                {
                    curResolution = res;
                    capture_run_handler?.Suspend();

                    capture?.Release();
                    capture?.Dispose();

                    // reopen it 
                    capture = new OpenCvSharp.VideoCapture(dev_index, (VideoCaptureAPIs)dev_type);

                    capture.Set(VideoCaptureProperties.FrameWidth, curResolution.X);
                    capture.Set(VideoCaptureProperties.FrameHeight, curResolution.Y);
                    Thread.Sleep(500);
                    capture_run_handler?.Resume();
                }
            }
        }

        private static ulong frameCount = 0;
        public static System.Diagnostics.Stopwatch runTime = new System.Diagnostics.Stopwatch();
        static void Capture_Frame()
        {
            runTime.Start();
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
                                frameCount++;
                                //Debug.WriteLine("capture fps: " + (frameCount / (runTime.ElapsedMilliseconds / 1000.0)).ToString("#.00"));

                                displayUI.Invalidate();
                            }
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_lock);
                    }
                }
                Thread.Sleep(6);
            }
        }

        public static Bitmap GetImage(Rectangle range)
        {
            if (range.Width <= 0 || range.Height <= 0)
                return null;

            lock (_lock)
            {
                if (_image == null)
                    return null;

                Bitmap img = new Bitmap(range.Width, range.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(img))
                    g.DrawImage(_image, 0, 0, range, GraphicsUnit.Pixel);
                return img;
            }
        }
        public static Bitmap GetImage()
        {
            lock (_lock)
            {
                if (_image == null)
                    return null;
                //Bitmap img = new Bitmap(_image.Width, _image.Height, PixelFormat.Format24bppRgb);
                Bitmap img = _image.Clone(new Rectangle(0, 0, _image.Width, _image.Height), _image.PixelFormat);
                //using (var g = Graphics.FromImage(img))
                //    g.DrawImage(_image, 0, 0, new Rectangle(0,0, _image.Width, _image.Height), GraphicsUnit.Pixel);
                return img;
            }
        }

    }
}
