using CommunityToolkit.Mvvm.Input;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Services;
using System.Globalization;
using ILogService = EasyCon.Core.Services.ILogService;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>
/// 喷射战士画板（移植自 1.6.3 WinForms 版 DrawingBoard 窗体）。算法见 <see cref="SplatoonDrawing"/>。
/// 文案经 <see cref="L10nBridge"/> 取自 Text.DrawingBoard.*；状态类文本（区域/耗时/进度）只保存状态，
/// 读取时按当前语言生成，窗口激活时经 <see cref="RefreshLocalizedTexts"/> 刷新。
/// 按职责分为分部文件：本文件（依赖、本地化文本、全部重置、释放）、.Picture（载入/保存、图片处理流程与预览叠加）、
/// .Settings（时序参数、框选区域与自动估算）、.Drawing（开始/暂停/停止、后台绘制与进度）。
/// </summary>
public partial class DrawingBoardViewModel : ViewModelBase, IDisposable
{
    private readonly IDrawingRunner _runner;
    private readonly IDialogService _dialogService;
    private readonly IImageProcessor _imageProcessor;
    private readonly IUiDispatcher _ui;
    private readonly ILogService _logService;

    public DrawingBoardViewModel(IDrawingRunner runner, IDialogService dialogService, IImageProcessor imageProcessor, IUiDispatcher ui, ILogService logService)
    {
        _runner = runner;
        _dialogService = dialogService;
        _imageProcessor = imageProcessor;
        _ui = ui;
        _logService = logService;
    }

    /// <summary>语言切换后由窗口激活时调用，重新生成状态类文本。</summary>
    public void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(EstimateText));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(RegionText));
        OnPropertyChanged(nameof(PauseButtonText));
        OnPropertyChanged(nameof(SpeckText));
    }

    /// <summary>
    /// 所有选项恢复默认：图片处理（适配、取景、方向、明暗、黑白化）、按键时序、橡皮擦、自动归位与框选区域。
    /// 已载入的图片保留，各项改完后只重算一次；绘制进行中不可用。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanResetAll))]
    private void ResetAll()
    {
        _suppressCanvasUpdate = true;
        _suppressMaskUpdate = true;
        try
        {
            FitModeIndex = 0;
            CoverAngle = 0;
            CoverZoomPercent = 100;
            CoverPanX = 0;
            CoverPanY = 0;
            RotationIndex = 0;
            FlipHorizontal = false;
            FlipVertical = false;
            PixelArt = false;
            ToneBrightness = 0;
            ToneContrast = 0;
            ToneGamma = 1;
            ThresholdPercent = 50;
            DitherModeIndex = 0;
            SpeckSize = 0;
            OutlineMode = false;
            Reverse = false;
            ShowOriginal = false;
            DurationText = DefaultDuration;
            DelayText = DefaultDelay;
            EraserMode = false;
            AutoHome = true;
            SelectRegion = false;
            ResetRegion();
        }
        finally
        {
            _suppressCanvasUpdate = false;
            _suppressMaskUpdate = false;
        }
        UpdateCanvas();
    }

    private bool CanResetAll() => !IsDrawing;

    /// <summary>窗口关闭时调用：中止进行中的绘制（运行租约结束时松开全部按键）。</summary>
    public void Dispose()
    {
        // 取消同样会打断暂停中的等待
        _drawCancellation?.Cancel();
    }

    private Task ShowMessageAsync(string message) => _dialogService.ShowMessageAsync(T("Text.DrawingBoard.Title"), message);

    private static string T(string key) => L10nBridge.T(key);

    private static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
}