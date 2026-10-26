using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon2.Avalonia.Models;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>图片：载入、拖放、保存，以及 原图 → 旋转/翻转 → 适配（含等比裁切取景）→ 二值化/抖动 的处理流程。</summary>
public partial class DrawingBoardViewModel
{
    /// <summary>适配方式下拉框的顺序。</summary>
    private static readonly ImageFit[] FitModes = [ImageFit.Contain, ImageFit.Cover, ImageFit.Stretch];

    /// <summary>载入的原图（只解码一次）；切换适配方式时从它重新缩放。</summary>
    private PixelImage? _source;

    /// <summary>适配到 320×120 后的画布图片；按阈值转成 <see cref="Mask"/>。</summary>
    private PixelImage? _canvas;

    /// <summary>预览框显示的图片：默认是二值化结果（黑 = 要画的点），勾选"显示原图"时为原图。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePictureCommand))]
    private PixelImage? _preview;

    /// <summary>载入文件的名字（不含扩展名），用于保存时的默认文件名。</summary>
    private string? _sourceName;

    [ObservableProperty]
    private bool _reverse;

    /// <summary>明暗分界，占最大亮度的百分比；50 与旧版写死的分界一致。</summary>
    [ObservableProperty]
    private int _thresholdPercent = 50;

    [ObservableProperty]
    private bool _showOriginal;

    /// <summary>抖动算法在下拉框中的下标，与 <see cref="DitherMode"/> 的顺序一致；默认 0 = 不抖动。</summary>
    [ObservableProperty]
    private int _ditherModeIndex;

    /// <summary>适配方式在 <see cref="FitModes"/> 中的下标；默认等比留白，整张图不变形。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCoverMode))]
    private int _fitModeIndex;

    /// <summary>等比裁切的任意角度旋转（顺时针，-180～180°），叠加在 90° 旋转与翻转之后。</summary>
    [ObservableProperty]
    private double _coverAngle;

    /// <summary>等比裁切在铺满画布基础上的放大比例（100～400%）。</summary>
    [ObservableProperty]
    private double _coverZoomPercent = 100;

    /// <summary>等比裁切的水平取景位置（-100 贴左 ～ 100 贴右）。</summary>
    [ObservableProperty]
    private double _coverPanX;

    /// <summary>等比裁切的垂直取景位置（-100 贴上 ～ 100 贴下）。</summary>
    [ObservableProperty]
    private double _coverPanY;

    /// <summary>重置取景时暂停逐项重算，最后统一算一次。</summary>
    private bool _suppressCanvasUpdate;

    /// <summary>原图顺时针旋转的 90° 次数（0～3），对应下拉框 0°/90°/180°/270°。</summary>
    [ObservableProperty]
    private int _rotationIndex;

    /// <summary>按旋转后的方向左右翻转。</summary>
    [ObservableProperty]
    private bool _flipHorizontal;

    /// <summary>按旋转后的方向上下翻转。</summary>
    [ObservableProperty]
    private bool _flipVertical;

    /// <summary>原图尺寸，例如"原图 640×480"；未载入时为空。</summary>
    [ObservableProperty]
    private string _sourceSizeText = "";

    /// <summary>当前图片按阈值/反转得到的墨点掩码；未载入图片时为 null。</summary>
    public InkMask? Mask { get; private set; }

    partial void OnReverseChanged(bool value) => UpdateMask();

    partial void OnThresholdPercentChanged(int value) => UpdateMask();

    partial void OnShowOriginalChanged(bool value) => UpdateMask();

    partial void OnDitherModeIndexChanged(int value) => UpdateMask();

    private DitherMode SelectedDither => Enum.IsDefined((DitherMode)DitherModeIndex) ? (DitherMode)DitherModeIndex : DitherMode.None;

    partial void OnFitModeIndexChanged(int value) => UpdateCanvas();

    /// <summary>取景调整（任意角度、缩放、位置）只在等比裁切时显示和生效。</summary>
    public bool IsCoverMode => SelectedFit == ImageFit.Cover;

    private ImageFit SelectedFit => FitModes[Math.Clamp(FitModeIndex, 0, FitModes.Length - 1)];

    partial void OnCoverAngleChanged(double value) => UpdateCoverCanvas();

    partial void OnCoverZoomPercentChanged(double value) => UpdateCoverCanvas();

    partial void OnCoverPanXChanged(double value) => UpdateCoverCanvas();

    partial void OnCoverPanYChanged(double value) => UpdateCoverCanvas();

    private void UpdateCoverCanvas()
    {
        if (IsCoverMode)
            UpdateCanvas();
    }

    /// <summary>取景调整恢复为居中、不旋转、不放大。</summary>
    [RelayCommand]
    private void ResetCover()
    {
        _suppressCanvasUpdate = true;
        try
        {
            CoverAngle = 0;
            CoverZoomPercent = 100;
            CoverPanX = 0;
            CoverPanY = 0;
        }
        finally
        {
            _suppressCanvasUpdate = false;
        }
        UpdateCoverCanvas();
    }

    partial void OnRotationIndexChanged(int value) => UpdateCanvas();

    partial void OnFlipHorizontalChanged(bool value) => UpdateCanvas();

    partial void OnFlipVerticalChanged(bool value) => UpdateCanvas();

    /// <summary>原图 → 旋转/翻转 → 适配到 320×120 → 二值化。均从已解码的原图重新计算，不重新读文件。</summary>
    private void UpdateCanvas()
    {
        if (_suppressCanvasUpdate)
            return;
        CoverTransform cover = new(CoverAngle, CoverZoomPercent / 100, CoverPanX / 100, CoverPanY / 100);
        _canvas = _source == null ? null : _imageProcessor.FitTo(_source.Orient(RotationIndex, FlipHorizontal, FlipVertical),
            SplatoonDrawing.CanvasWidth, SplatoonDrawing.CanvasHeight, SelectedFit, cover);
        UpdateMask();
    }

    private void UpdateMask()
    {
        Mask = _canvas == null ? null : InkMask.Binarize(_canvas, new BinarizeOptions(ThresholdPercent, Reverse, SelectedDither));
        Preview = ShowOriginal ? _canvas : Mask?.ToPreview();
        UpdateEstimate();
    }

    /// <summary>SkiaSharp 能解码的格式（实测 HEIC、TIFF、PSD、TGA、JPEG 2000 不支持）。</summary>
    internal static readonly string[] PictureExtensions = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.ico", "*.wbmp"];

    /// <summary>把图片文件拖进窗口即载入（由 FileDropBehavior 传入第一个文件的路径）。</summary>
    [RelayCommand]
    private Task DropPictureAsync(string path) => LoadPictureFileAsync(path);

    [RelayCommand]
    private async Task LoadPictureAsync()
    {
        IReadOnlyList<string> files = await _dialogService.OpenFilesAsync(T("Text.DrawingBoard.LoadPicture"),
            [new FileDialogFilter(T("Text.DrawingBoard.ImageFilter"), PictureExtensions)],
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        if (files.Count == 0)
            return;
        await LoadPictureFileAsync(files[0]);
    }

    internal async Task LoadPictureFileAsync(string filePath)
    {
        PixelImage? image = _imageProcessor.LoadPixels(filePath);
        if (image == null)
        {
            await ShowMessageAsync(T("Text.DrawingBoard.LoadFailed"));
            return;
        }
        // 任意尺寸都按适配方式缩放到 320×120（旧版只接受 320×120，且尺寸检查用 && 写错了）
        _source = image;
        _sourceName = Path.GetFileNameWithoutExtension(filePath);
        SourceSizeText = Format("Text.DrawingBoard.SourceSize", image.Width, image.Height);
        UpdateCanvas();
    }

    /// <summary>
    /// 保存预览中的 320×120 图片：默认是二值化结果（最终画出的样子，纯黑白），勾选"显示原图"时是旋转、适配后的原图。
    /// 二值化结果尺寸正好是画布大小、只有纯黑白，之后再载入不会重采样，按默认 50% 阈值得到同一张掩码。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSavePicture))]
    private async Task SavePictureAsync()
    {
        if (Preview is not { } picture)
            return;
        string suggested = (_sourceName ?? "drawing-board") + $"-{picture.Width}x{picture.Height}.png";
        string? path = await _dialogService.SaveFileAsync(T("Text.DrawingBoard.SavePicture"), "png",
            [new FileDialogFilter("PNG", ["*.png"])], suggested);
        if (path == null)
            return;
        if (_imageProcessor.SavePng(picture, path))
            _logService.AddLog(Format("Text.DrawingBoard.Saved", path));
        else
            await ShowMessageAsync(T("Text.DrawingBoard.SaveFailed"));
    }

    private bool CanSavePicture() => Preview != null;
}