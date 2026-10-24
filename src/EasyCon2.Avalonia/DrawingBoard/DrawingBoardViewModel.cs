using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Maa;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using EasyDevice;
using ILogService = EasyCon.Core.Services.ILogService;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>
/// 喷射战士画板（移植自 1.6.3 WinForms 版 DrawingBoard 窗体）。
/// 输入框默认值、提示文案与按钮启用时机均与旧版一致；算法见 <see cref="SplatoonDrawing"/>。
/// </summary>
public partial class DrawingBoardViewModel : ViewModelBase, IDisposable
{
    private const string MessageTitle = "画板";

    private readonly IDrawingPad _pad;
    private readonly IDialogService _dialogService;
    private readonly IImageProcessor _imageProcessor;
    private readonly IUiDispatcher _ui;
    private readonly ILogService _logService;
    private PixelImage? _image;
    private CancellationTokenSource? _source;

    [ObservableProperty]
    private byte[]? _picturePng;

    [ObservableProperty]
    private string _startPosXText = "0";

    [ObservableProperty]
    private string _startPosYText = "0";

    [ObservableProperty]
    private string _widthText = "320";

    [ObservableProperty]
    private string _heightText = "120";

    [ObservableProperty]
    private string _durationText = "50";

    [ObservableProperty]
    private string _delayText = "45";

    [ObservableProperty]
    private bool _reverse;

    [ObservableProperty]
    private bool _isStartEnabled = true;

    [ObservableProperty]
    private string _evaluateText = "耗时：";

    [ObservableProperty]
    private string _curPosText = "当前坐标：100，40";

    public DrawingBoardViewModel(IDrawingPad pad, IDialogService dialogService, IImageProcessor imageProcessor, IUiDispatcher ui, ILogService logService)
    {
        _pad = pad;
        _dialogService = dialogService;
        _imageProcessor = imageProcessor;
        _ui = ui;
        _logService = logService;
    }

    /// <summary>最近一次开始画图的后台任务（测试用于等待绘制结束）。</summary>
    internal Task? DrawTask { get; private set; }

    [RelayCommand]
    private async Task LoadPictureAsync()
    {
        IReadOnlyList<string> files = await _dialogService.OpenFilesAsync("加载图片",
            [new FileDialogFilter("Files", ["*.png", "*.jpg"])],
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        if (files.Count == 0)
            return;
        await LoadPictureFileAsync(files[0]);
    }

    internal async Task LoadPictureFileAsync(string filepath)
    {
        PixelImage? image = _imageProcessor.LoadPixels(filepath);
        if (image == null)
        {
            await _dialogService.ShowMessageAsync(MessageTitle, "图片加载失败");
            return;
        }
        // 旧版用 && 判断（宽高都不对才提示），且提示后仍会用该图绘制，小图会在绘制中途越界
        if (image.Width != 320 || image.Height != 120)
        {
            await _dialogService.ShowMessageAsync(MessageTitle, "图片不是320*120大小，请重新修改");
            return;
        }

        _image = image;
        PicturePng = _imageProcessor.LoadAsPng(filepath);
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!_pad.IsConnected())
        {
            await _dialogService.ShowMessageAsync(MessageTitle, "串口未连接");
            return;
        }
        if (DrawTask is { IsCompleted: false })
            return;

        _source = new CancellationTokenSource();
        CancellationToken token = _source.Token;
        PixelImage? image = _image;
        string[] texts = [DelayText, DurationText, StartPosYText, StartPosXText, WidthText, HeightText];
        bool reverse = Reverse;
        //开启一个task执行任务
        DrawTask = Task.Run(() => DrawTaskOpt(image, texts, reverse, token));
        IsStartEnabled = false;
    }

    private void DrawTaskOpt(PixelImage? image, string[] texts, bool reverse, CancellationToken token)
    {
        if (image == null)
        {
            _ui.Post(() =>
            {
                IsStartEnabled = true;
                _ = _dialogService.ShowMessageAsync(MessageTitle, "未加载图片");
            });
            return;
        }

        try
        {
            DrawingBoardSettings settings = ParseSettings(texts, reverse);
            List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(image, settings);
            SplatoonDrawing drawing = new(_pad, settings.Wait, settings.Duration);
            if (drawing.Draw(rows, token))
                _ui.Post(() => IsStartEnabled = true);
        }
        catch (Exception ex)
        {
            // 旧版任务异常被静默吞掉，开始按钮一直禁用到点击停止，用户无从得知原因
            string message = $"画图中断: {ex.Message}";
            _logService.AddLog(message);
            // 出错时 A 键可能仍按着；设备本身不可用时释放也会失败，只能忽略
            try
            {
                _pad.Reset();
            }
            catch (ControllerOperationException)
            {
            }
            _ui.Post(() =>
            {
                IsStartEnabled = true;
                _ = _dialogService.ShowMessageAsync(MessageTitle, message);
            });
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _source?.Cancel();
        // 等后台绘制真正退出再释放按键、恢复开始按钮（旧版固定等 1 秒，期间绘制可能仍在发键）
        if (DrawTask != null)
            await DrawTask;
        try
        {
            _pad.Reset();
        }
        catch (ControllerOperationException ex)
        {
            _logService.AddLog($"画图停止时释放按键失败: {ex.Message}");
        }
        IsStartEnabled = true;
    }

    [RelayCommand]
    private async Task EvaluateAsync()
    {
        if (_image == null)
        {
            await _dialogService.ShowMessageAsync(MessageTitle, "未加载图片");
            return;
        }

        try
        {
            DrawingBoardSettings settings = ParseSettings(
                [DelayText, DurationText, StartPosYText, StartPosXText, WidthText, HeightText], Reverse);
            List<List<DrawingLine>> rows = SplatoonDrawing.SearchLines(_image, settings);
            int point_num = SplatoonDrawing.EvaluatePoints(rows);
            EvaluateText = SplatoonDrawing.FormatEvaluation(point_num, settings.Wait, settings.Duration);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException)
        {
            await _dialogService.ShowMessageAsync(MessageTitle, ex.Message);
        }
    }

    /// <summary>
    /// 预览图左键按下：按预览框实际尺寸换算成 320×120 画布坐标（列,行）。
    /// 旧版固定除以 2，但预览框是 822×282 拉伸显示，显示的坐标与画布对不上。
    /// </summary>
    public void OnPicturePressed(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return;
        int col = Math.Clamp((int)(x * 320 / width), 0, 319);
        int row = Math.Clamp((int)(y * 120 / height), 0, 119);
        CurPosText = "当前位置:" + col.ToString() + "," + row.ToString();
    }

    /// <summary>窗口关闭时调用：中止进行中的绘制（绘制线程收到取消后自行松开全部按键）。</summary>
    public void Dispose()
    {
        _source?.Cancel();
    }

    /// <summary>texts 顺序：延迟、按键持续、起始行、起始列、列数、行数（旧版 int.Parse 的读取顺序）。</summary>
    private static DrawingBoardSettings ParseSettings(string[] texts, bool reverse)
    {
        int wait = int.Parse(texts[0]);
        int duration = int.Parse(texts[1]);
        int start_row = int.Parse(texts[2]);
        int start_col = int.Parse(texts[3]);
        int col = int.Parse(texts[4]);
        int row = int.Parse(texts[5]);
        return new DrawingBoardSettings(wait, duration, start_row, start_col, col, row, reverse);
    }
}

/// <summary>经设备会话的手动输入通道发键，与旧版直接调用 NintendoSwitch 的路径对应。</summary>
public sealed class DeviceDrawingPad(IDeviceService deviceService) : IDrawingPad
{
    public bool IsConnected() => deviceService.IsConnected;

    public void Down(ECKey key) => deviceService.GetDevice().Down(key);

    public void Up(ECKey key) => deviceService.GetDevice().Up(key);

    public void Reset() => deviceService.GetDevice().Reset();
}