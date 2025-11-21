using EasyCapture;
using OpenCvSharp.Extensions;
using System.Diagnostics;

namespace EasyCon2.Capture;

public partial class ClasicCapture
{
    private readonly object _lock = new();
    private int dev_index = 0;
    private int apiRefs = 0;
    private OpenCVCapture capture = new();

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

                    capture.Release();
                    // reopen it 
                    capture.Open(dev_index, apiRefs);

                    capture.SetProperties( _curResolution.X, _curResolution.Y);
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
        apiRefs = typeId;

        try
        {
            if (!capture.Open(dev_index, typeId))
            {
                MessageBox.Show("当前采集卡已经被其他程序打开，请先关闭后再尝试");
                Close();
            }

            capture.SetProperties(_curResolution.X, _curResolution.Y);

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
            capture?.Release();
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
                    using var frameMat = capture.GetFrame();
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
