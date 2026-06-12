using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Capture;
using EasyCon.Core;
using System.Collections.ObjectModel;
using System.IO;

namespace EasyCon2.Avalonia.Core.TagEditor;

public partial class TagEditorViewModel : ObservableObject
{
    public ImgLabel Label { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleRangeSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTargetSelectionCommand))]
    private IImage? _sourceImage;

    [ObservableProperty]
    private IImage? _targetImage;

    [ObservableProperty]
    private IImage? _rangePreviewImage;

    public bool HasSourceImage => _sourceImage != null;

    public static readonly IReadOnlyList<SearchMethod> SearchMethods = ECCore.GetSearchMethods().ToList();

    public static readonly IReadOnlyList<string> ParameterOptions =
    [
        "默认",
        "灰度",
        "二值",
        "高斯模糊",
        "其他"
    ];

    public string SelectedParameterDisplay
    {
        get
        {
            var selected = new List<string>();
            if (UseGrayscale)
                selected.Add("灰度");
            if (UseBinary)
                selected.Add("二值");
            if (UseGaussianBlur)
                selected.Add("高斯模糊");
            if (UseOther)
                selected.Add("其他");

            return string.Join("+", selected);
        }
    }

    public double SelectedParameterFontSize
    {
        get
        {
            var length = SelectedParameterDisplay.Length;
            if (length <= 4)
                return 15;
            if (length <= 7)
                return 13;

            return 11;
        }
    }

    public int ParameterComboSelectedIndex
    {
        get => -1;
        set => OnPropertyChanged();
    }

    #region Proxy properties from ImgLabel

    [ObservableProperty]
    private string _labelName = "5号路孵蛋屋主人";

    /// <summary>
    /// 标签名称是否有值（用于控制保存按钮可用性）
    /// </summary>
    [ObservableProperty]
    private bool _hasLabelName = true;

    partial void OnLabelNameChanged(string value)
    {
        Label.name = value;
        HasLabelName = !string.IsNullOrWhiteSpace(value);
    }

    [ObservableProperty]
    private int _targetX;

    partial void OnTargetXChanged(int value)
    {
        Label.TargetX = value;
        UpdateTargetRect();
    }

    [ObservableProperty]
    private int _targetY;

    partial void OnTargetYChanged(int value)
    {
        Label.TargetY = value;
        UpdateTargetRect();
    }

    [ObservableProperty]
    private int _targetWidth;

    partial void OnTargetWidthChanged(int value)
    {
        Label.TargetWidth = value;
        UpdateTargetRect();
    }

    [ObservableProperty]
    private int _targetHeight;

    partial void OnTargetHeightChanged(int value)
    {
        Label.TargetHeight = value;
        UpdateTargetRect();
    }

    [ObservableProperty]
    private int _rangeX;

    partial void OnRangeXChanged(int value)
    {
        Label.RangeX = value;
        UpdateRangeRect();
    }

    [ObservableProperty]
    private int _rangeY;

    partial void OnRangeYChanged(int value)
    {
        Label.RangeY = value;
        UpdateRangeRect();
    }

    [ObservableProperty]
    private int _rangeWidth;

    partial void OnRangeWidthChanged(int value)
    {
        Label.RangeWidth = value;
        UpdateRangeRect();
    }

    [ObservableProperty]
    private int _rangeHeight;

    partial void OnRangeHeightChanged(int value)
    {
        Label.RangeHeight = value;
        UpdateRangeRect();
    }

    [ObservableProperty]
    private SearchMethod _searchMethod = SearchMethod.CCoeffNormed;

    partial void OnSearchMethodChanged(SearchMethod value) => Label.searchMethod = value;

    [ObservableProperty]
    private bool _useGrayscale;

    partial void OnUseGrayscaleChanged(bool value)
    {
        Label.UseGrayscale = value;
        OnSelectedParametersChanged();
    }

    [ObservableProperty]
    private bool _useBinary;

    partial void OnUseBinaryChanged(bool value)
    {
        Label.UseBinary = value;
        OnSelectedParametersChanged();
    }

    [ObservableProperty]
    private bool _useGaussianBlur;

    partial void OnUseGaussianBlurChanged(bool value)
    {
        Label.UseGaussianBlur = value;
        OnSelectedParametersChanged();
    }

    [ObservableProperty]
    private bool _useOther;

    partial void OnUseOtherChanged(bool value)
    {
        Label.UseOther = value;
        OnSelectedParametersChanged();
    }

    private void OnSelectedParametersChanged()
    {
        OnPropertyChanged(nameof(SelectedParameterDisplay));
        OnPropertyChanged(nameof(SelectedParameterFontSize));
    }

    [ObservableProperty]
    private string _selectedParameterOption = "默认";

    partial void OnSelectedParameterOptionChanged(string value)
    {
        UseGrayscale = value == "灰度";
        UseBinary = value == "二值";
        UseGaussianBlur = value == "高斯模糊";
        UseOther = value == "其他";
    }

    #endregion

    /// <summary>
    /// 请求主窗口打开文件选择对话框。
    /// </summary>
    public event Action? OpenFileRequested;

    /// <summary>
    /// 请求主窗口执行截图操作。
    /// </summary>
    public event Action? CaptureScreenshotRequested;

    /// <summary>
    /// 日志输出事件。
    /// </summary>
    public event Action<string>? LogMessage;

    /// <summary>
    /// 视频源是否已连接（用于控制截图按钮可用性）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CaptureScreenshotCommand))]
    private bool _isCaptureConnected;

    /// <summary>
    /// 当前圈选模式
    /// </summary>
    [ObservableProperty]
    private SelectionMode _currentSelectionMode = SelectionMode.None;

    /// <summary>
    /// 范围矩形（图片坐标）
    /// </summary>
    [ObservableProperty]
    private Rect _rangeRect;

    /// <summary>
    /// 目标矩形（图片坐标）
    /// </summary>
    [ObservableProperty]
    private Rect _targetRect;

    /// <summary>
    /// 圈选范围按钮文本
    /// </summary>
    public string RangeButtonText => CurrentSelectionMode == SelectionMode.Range ? "确定范围" : "圈选范围";

    /// <summary>
    /// 圈选目标按钮文本
    /// </summary>
    public string TargetButtonText => CurrentSelectionMode == SelectionMode.Target ? "确定目标" : "圈选目标";

    partial void OnCurrentSelectionModeChanged(SelectionMode value)
    {
        OnPropertyChanged(nameof(RangeButtonText));
        OnPropertyChanged(nameof(TargetButtonText));
    }

    partial void OnRangeRectChanged(Rect value)
    {
        // 圈选范围矩形变化 → 更新搜索范围坐标
        if (value.Width > 0 && value.Height > 0)
        {
            RangeX = (int)value.X;
            RangeY = (int)value.Y;
            RangeWidth = (int)value.Width;
            RangeHeight = (int)value.Height;
        }
    }

    partial void OnTargetRectChanged(Rect value)
    {
        // 圈选目标矩形变化 → 更新目标位置坐标
        if (value.Width > 0 && value.Height > 0)
        {
            TargetX = (int)value.X;
            TargetY = (int)value.Y;
            TargetWidth = (int)value.Width;
            TargetHeight = (int)value.Height;
        }
    }

    [RelayCommand]
    private void OpenFile()
    {
        OpenFileRequested?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(IsCaptureConnected))]
    private void CaptureScreenshot()
    {
        CaptureScreenshotRequested?.Invoke();
    }

    /// <summary>
    /// 设置截图结果（由主窗口调用）。
    /// </summary>
    public void SetScreenshot(Bitmap bitmap)
    {
        SourceImage = bitmap;
    }

    [RelayCommand(CanExecute = nameof(HasSourceImage))]
    private void ToggleRangeSelection()
    {
        CurrentSelectionMode = CurrentSelectionMode == SelectionMode.Range
            ? SelectionMode.None
            : SelectionMode.Range;
    }

    [RelayCommand(CanExecute = nameof(HasSourceImage))]
    private void ToggleTargetSelection()
    {
        // 从"圈选中"切换到"确定"时，裁剪目标区域并更新目标图和 ImgBase64
        if (CurrentSelectionMode == SelectionMode.Target)
        {
            CurrentSelectionMode = SelectionMode.None;
            UpdateTargetImageFromRoi();
        }
        else
        {
            CurrentSelectionMode = SelectionMode.Target;
        }
    }

    /// <summary>
    /// 根据 TargetRect 从 SourceImage 裁剪 ROI，更新 TargetImage 和 Label.ImgBase64。
    /// </summary>
    private void UpdateTargetImageFromRoi()
    {
        if (SourceImage is not Bitmap src)
            return;

        var rect = TargetRect;
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        // 裁剪区域限制在图片范围内
        var cropRect = rect.Intersect(new Rect(0, 0, src.PixelSize.Width, src.PixelSize.Height));
        if (cropRect.Width <= 0 || cropRect.Height <= 0)
            return;

        // 用 RenderTargetBitmap 裁剪 ROI 区域
        var pixelSize = new PixelSize((int)cropRect.Width, (int)cropRect.Height);
        var rtb = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            var srcRect = new Rect(cropRect.X, cropRect.Y, cropRect.Width, cropRect.Height);
            var dstRect = new Rect(0, 0, cropRect.Width, cropRect.Height);
            ctx.DrawImage(src, srcRect, dstRect);
        }

        // 写入内存流，同时更新 TargetImage 和 ImgBase64
        var ms = new MemoryStream();
        rtb.Save(ms);
        ms.Position = 0;
        TargetImage = new Bitmap(ms);
        Label.ImgBase64 = Convert.ToBase64String(ms.ToArray());
    }

    [RelayCommand(CanExecute = nameof(HasLabelName))]
    private void SaveLabel()
    {
        try
        {
            // 验证标签数据
            if (!Label.Valid())
            {
                LogMessage?.Invoke("标签数据不完善，无法保存");
                return;
            }

            // 确定保存路径
            var savePath = !string.IsNullOrEmpty(Label.path)
                ? Label.path
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImgLabel");

            // 保存（同名文件覆盖）
            Label.Save(savePath);

            LogMessage?.Invoke($"标签已保存: {Label.name}.IL");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"保存失败: {ex.Message}");
        }
    }

    private void UpdateRangeRect()
    {
        RangeRect = new Rect(RangeX, RangeY, RangeWidth, RangeHeight);
    }

    private void UpdateTargetRect()
    {
        TargetRect = new Rect(TargetX, TargetY, TargetWidth, TargetHeight);
    }

    /// <summary>
    /// 从文件路径加载图片到SourceImage。
    /// </summary>
    public void LoadImageFromFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                SourceImage = new Bitmap(filePath);
            }
        }
        catch
        {
            // 忽略加载错误
        }
    }

    public TagEditorViewModel() { }

    public TagEditorViewModel(ImgLabel label)
    {
        LoadFromLabel(label);
    }

    public void LoadFromLabel(ImgLabel label)
    {
        // 复制所有属性到 Label 对象
        Label.name = label.name;
        Label.path = label.path;
        Label.ImgBase64 = label.ImgBase64;
        Label.searchMethod = label.searchMethod;
        Label.TargetX = label.TargetX;
        Label.TargetY = label.TargetY;
        Label.TargetWidth = label.TargetWidth;
        Label.TargetHeight = label.TargetHeight;
        Label.RangeX = label.RangeX;
        Label.RangeY = label.RangeY;
        Label.RangeWidth = label.RangeWidth;
        Label.RangeHeight = label.RangeHeight;
        Label.UseGrayscale = label.UseGrayscale;
        Label.UseBinary = label.UseBinary;
        Label.UseGaussianBlur = label.UseGaussianBlur;
        Label.UseOther = label.UseOther;

        // 更新 UI 属性
        LabelName = label.name;
        TargetX = label.TargetX;
        TargetY = label.TargetY;
        TargetWidth = label.TargetWidth;
        TargetHeight = label.TargetHeight;
        RangeX = label.RangeX;
        RangeY = label.RangeY;
        RangeWidth = label.RangeWidth;
        RangeHeight = label.RangeHeight;
        SearchMethod = label.searchMethod;
        UseGrayscale = label.UseGrayscale;
        UseBinary = label.UseBinary;
        UseGaussianBlur = label.UseGaussianBlur;
        UseOther = label.UseOther;
        SelectedParameterOption = GetParameterOption();

        // 同步矩形到 SelectableImage 控件
        RangeRect = new Rect(RangeX, RangeY, RangeWidth, RangeHeight);
        TargetRect = new Rect(TargetX, TargetY, TargetWidth, TargetHeight);

        TargetImage = CreateTargetImage(label);
    }

    private static Bitmap? CreateTargetImage(ImgLabel label)
    {
        if (string.IsNullOrWhiteSpace(label.ImgBase64) || !label.searchMethod.IsImageMethod())
            return null;

        var bytes = Convert.FromBase64String(label.ImgBase64);
        return new Bitmap(new MemoryStream(bytes));
    }

    public ImgLabel ToImgLabel() => Label with { };

    [RelayCommand]
    private void IncrementCoordinate(string name)
    {
        AdjustCoordinate(name, 1);
    }

    [RelayCommand]
    private void DecrementCoordinate(string name)
    {
        AdjustCoordinate(name, -1);
    }

    private void AdjustCoordinate(string name, int delta)
    {
        static int Clamp(int value) => Math.Clamp(value, 0, 9999);

        switch (name)
        {
            case nameof(TargetX):
                TargetX = Clamp(TargetX + delta);
                break;
            case nameof(TargetY):
                TargetY = Clamp(TargetY + delta);
                break;
            case nameof(TargetWidth):
                TargetWidth = Clamp(TargetWidth + delta);
                break;
            case nameof(TargetHeight):
                TargetHeight = Clamp(TargetHeight + delta);
                break;
            case nameof(RangeX):
                RangeX = Clamp(RangeX + delta);
                break;
            case nameof(RangeY):
                RangeY = Clamp(RangeY + delta);
                break;
            case nameof(RangeWidth):
                RangeWidth = Clamp(RangeWidth + delta);
                break;
            case nameof(RangeHeight):
                RangeHeight = Clamp(RangeHeight + delta);
                break;
        }
    }

    private string GetParameterOption()
    {
        if (UseGrayscale)
            return "灰度";
        if (UseBinary)
            return "二值";
        if (UseGaussianBlur)
            return "高斯模糊";
        if (UseOther)
            return "其他";

        return "默认";
    }
}