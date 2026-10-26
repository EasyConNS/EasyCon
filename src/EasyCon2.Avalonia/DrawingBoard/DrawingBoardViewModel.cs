using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Services;
using System.Globalization;
using ILogService = EasyCon.Core.Services.ILogService;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>
/// 喷射战士画板（移植自 1.6.3 WinForms 版 DrawingBoard 窗体）。算法见 <see cref="SplatoonDrawing"/>。
/// 文案经 <see cref="L10nBridge"/> 取自 Text.DrawingBoard.*；状态类文本（坐标/耗时/进度）只保存状态，
/// 读取时按当前语言生成，窗口激活时经 <see cref="RefreshLocalizedTexts"/> 刷新。
/// 按职责分为分部文件：本文件（依赖、坐标、本地化文本、释放）、.Picture（载入/保存与图片处理流程）、
/// .Settings（参数输入、校验与自动估算）、.Drawing（开始/停止、后台绘制与进度）。
/// </summary>
public partial class DrawingBoardViewModel : ViewModelBase, IDisposable
{
    private readonly IDrawingRunner _runner;
    private readonly IDialogService _dialogService;
    private readonly IImageProcessor _imageProcessor;
    private readonly IUiDispatcher _ui;
    private readonly ILogService _logService;

    private (int Col, int Row)? _curPos;

    public DrawingBoardViewModel(IDrawingRunner runner, IDialogService dialogService, IImageProcessor imageProcessor, IUiDispatcher ui, ILogService logService)
    {
        _runner = runner;
        _dialogService = dialogService;
        _imageProcessor = imageProcessor;
        _ui = ui;
        _logService = logService;
    }

    public string CurPosText => _curPos is { } pos
        ? Format("Text.DrawingBoard.CurPos", pos.Col, pos.Row)
        : T("Text.DrawingBoard.CurPosNone");

    /// <summary>语言切换后由窗口激活时调用，重新生成状态类文本。</summary>
    public void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(CurPosText));
        OnPropertyChanged(nameof(EstimateText));
        OnPropertyChanged(nameof(ProgressText));
    }

    /// <summary>
    /// 预览图左键按下：按预览框实际尺寸换算成 320×120 画布坐标（列,行）。
    /// 旧版固定除以 2，但预览框是 822×282 拉伸显示，显示的坐标与画布对不上。
    /// </summary>
    public void OnPicturePressed(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return;
        int col = Math.Clamp((int)(x * SplatoonDrawing.CanvasWidth / width), 0, SplatoonDrawing.CanvasWidth - 1);
        int row = Math.Clamp((int)(y * SplatoonDrawing.CanvasHeight / height), 0, SplatoonDrawing.CanvasHeight - 1);
        _curPos = (col, row);
        OnPropertyChanged(nameof(CurPosText));
    }

    /// <summary>窗口关闭时调用：中止进行中的绘制（运行租约结束时松开全部按键）。</summary>
    public void Dispose()
    {
        _drawCancellation?.Cancel();
    }

    private Task ShowMessageAsync(string message) => _dialogService.ShowMessageAsync(T("Text.DrawingBoard.Title"), message);

    private static string T(string key) => L10nBridge.T(key);

    private static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
}