using EasyScript;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Diagnostics;

namespace EasyCon2.Capture
{
    public partial class OpenCVCapture
    {
        private readonly object _lock = new();
        private int dev_index = 0;
        private int dev_type = 0;
        private VideoCapture capture;

        private CancellationTokenSource source;
        private CancellationToken token;

        private Bitmap _image;
        private Control displayUI;

        private static readonly Stopwatch runTime = new();

        private System.Drawing.Point _curResolution = new(1920, 1080);

        public System.Drawing.Point CurResolution
        {
            get
            {
                return _curResolution;
            }
            set
            {
                if (_curResolution.X != value.X || _curResolution.Y != value.Y)
                {
                    lock (_lock)
                    {
                        _curResolution = value;
                        if (source != null)
                        {
                            source.Cancel();
                        }

                        capture?.Release();
                        capture?.Dispose();
                        // reopen it 
                        capture = new VideoCapture(dev_index, (VideoCaptureAPIs)dev_type);

                        capture.Set(VideoCaptureProperties.FrameWidth, _curResolution.X);
                        capture.Set(VideoCaptureProperties.FrameHeight, _curResolution.Y);
                        Thread.Sleep(300);

                        source = new CancellationTokenSource();
                        token = source.Token;
                        Task.Run(()=> {
                            Capture_Frame();
                        }, token);
                    }
                }
            }
        }

        public void CaptureCamera(Control pictureBox, int index = 0, int typeId = 0)
        {
            displayUI = pictureBox;
            dev_index = index;
            dev_type = typeId;

            try
            {
                capture = new VideoCapture(dev_index, (VideoCaptureAPIs)dev_type);
                if (!capture.IsOpened())
                {
                    MessageBox.Show("当前采集卡已经被其他程序打开，请先关闭后再尝试");
                    Close();
                }

                capture.Set(VideoCaptureProperties.FrameWidth, _curResolution.X);
                capture.Set(VideoCaptureProperties.FrameHeight, _curResolution.Y);
                capture.Set(VideoCaptureProperties.Fps, 60);

                Debug.WriteLine(capture.Get(VideoCaptureProperties.Mode));
                Debug.WriteLine(capture.Get(VideoCaptureProperties.FourCC));
                Debug.WriteLine(capture.Get(VideoCaptureProperties.Backend));
                Debug.WriteLine(capture.Get(VideoCaptureProperties.Fps));

                source = new CancellationTokenSource();
                token = source.Token;
                Task.Run(() => {
                    Capture_Frame();
                }, token);
            }
            catch(Exception ce)
            {
                MessageBox.Show("打开失败，出现错误:" + ce.Message);
                return;
            }
        }

        public void Close()
        {
            if (capture == null)
                return;
            lock (_lock)
            {
                if (source != null)
                {
                    source.Cancel();
                }
                if (!capture.IsDisposed)
                {
                    capture?.Release();
                    capture?.Dispose();
                }
            }
        }

        private void Capture_Frame()
        {
            runTime.Start();
            while (true)
            {
                if (token.IsCancellationRequested) break;
                if (Monitor.TryEnter(_lock))
                {
                    try
                    {
                        using var frameMat = capture.RetrieveMat();
                        if (!frameMat.Empty())
                        {
                            _image?.Dispose();
                            _image = BitmapConverter.ToBitmap(frameMat);
                            displayUI?.Invalidate();
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_lock);
                    }
                }
                Thread.Sleep(16); // 60 fps enough
            }
            runTime.Stop();
        }

        public Bitmap? GetImage()
        {
            if (capture == null)
                throw new Exception("采集卡设备读取失败");
            lock (_lock)
            {
                if (_image == null)
                    return null;

                return _image.Clone(new Rectangle(0, 0, _image.Width, _image.Height), _image.PixelFormat);
            }
        }
    }
}
