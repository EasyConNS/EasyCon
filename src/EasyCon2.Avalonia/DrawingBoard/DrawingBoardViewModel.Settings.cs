using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics.CodeAnalysis;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>
/// 参数：按键时序的输入与校验、绘制区域（默认整张画布，高级选项下在预览上拖动框选），
/// 以及随图片和参数自动更新的耗时估算。
/// </summary>
public partial class DrawingBoardViewModel
{
    private const string DefaultDuration = "50";
    private const string DefaultDelay = "45";

    [ObservableProperty]
    private string _durationText = DefaultDuration;

    [ObservableProperty]
    private string _delayText = DefaultDelay;

    /// <summary>
    /// 高级：在预览上拖动框选绘制区域。默认关闭，即整张画布、光标从左上角出发——
    /// 游戏里很难确认某个点的坐标，手填起点行列容易错位，所以不再提供输入框。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegionText))]
    private bool _selectRegion;

    private int _regionStartCol;
    private int _regionStartRow;
    private int _regionColCount = SplatoonDrawing.CanvasWidth;
    private int _regionRowCount = SplatoonDrawing.CanvasHeight;

    /// <summary>拖动框选的起点；按下时记录，松开时清除。</summary>
    private (int Col, int Row)? _dragAnchor;

    private TimeSpan? _estimate;
    private bool _estimateInvalid;

    partial void OnDurationTextChanged(string value) => UpdateEstimate();

    partial void OnDelayTextChanged(string value) => UpdateEstimate();

    partial void OnSelectRegionChanged(bool value)
    {
        ClearFinishedProgress();
        UpdateEstimate();
        ComposeDisplay();
    }

    /// <summary>实际生效的绘制区域：未开启框选时恒为整张画布。</summary>
    private (int StartCol, int StartRow, int ColCount, int RowCount) EffectiveRegion => SelectRegion
        ? (_regionStartCol, _regionStartRow, _regionColCount, _regionRowCount)
        : (0, 0, SplatoonDrawing.CanvasWidth, SplatoonDrawing.CanvasHeight);

    public string RegionText => SelectRegion
        ? Format("Text.DrawingBoard.RegionRange", _regionStartCol, _regionStartCol + _regionColCount - 1,
            _regionStartRow, _regionStartRow + _regionRowCount - 1)
        : T("Text.DrawingBoard.RegionAll");

    /// <summary>设置框选区域（列、行从 0 起），自动截到画布内；绘制进行中不允许修改。</summary>
    internal void SetRegion(int startCol, int startRow, int colCount, int rowCount)
    {
        if (IsDrawing)
            return;
        // 重新框选时换回完整预览：上一次绘制留下的进度图会把区域外淡化，不便对照整张图选区
        ClearFinishedProgress();
        _regionStartCol = Math.Clamp(startCol, 0, SplatoonDrawing.CanvasWidth - 1);
        _regionStartRow = Math.Clamp(startRow, 0, SplatoonDrawing.CanvasHeight - 1);
        _regionColCount = Math.Clamp(colCount, 1, SplatoonDrawing.CanvasWidth - _regionStartCol);
        _regionRowCount = Math.Clamp(rowCount, 1, SplatoonDrawing.CanvasHeight - _regionStartRow);
        OnPropertyChanged(nameof(RegionText));
        UpdateEstimate();
        ComposeDisplay();
    }

    /// <summary>框选区域恢复为整张画布。</summary>
    [RelayCommand]
    private void ResetRegion() => SetRegion(0, 0, SplatoonDrawing.CanvasWidth, SplatoonDrawing.CanvasHeight);

    /// <summary>
    /// 预览图左键按下：开启框选时，按预览框实际尺寸换算成 320×120 画布坐标（列,行）作为框选起点。
    /// 旧版固定除以 2，但预览框是 822×282 拉伸显示，坐标与画布对不上。
    /// </summary>
    public void OnPicturePressed(double x, double y, double width, double height)
    {
        if (ToCanvas(x, y, width, height) is not { } at)
            return;
        if (SelectRegion && !IsDrawing)
        {
            _dragAnchor = at;
            SetRegion(at.Col, at.Row, 1, 1);
        }
    }

    /// <summary>按住左键拖动：框选时以按下点和当前点为对角更新区域。</summary>
    public void OnPictureDragged(double x, double y, double width, double height)
    {
        if (ToCanvas(x, y, width, height) is not { } at)
            return;
        if (_dragAnchor is { } anchor)
        {
            SetRegion(Math.Min(anchor.Col, at.Col), Math.Min(anchor.Row, at.Row),
                Math.Abs(at.Col - anchor.Col) + 1, Math.Abs(at.Row - anchor.Row) + 1);
        }
    }

    public void OnPictureReleased() => _dragAnchor = null;

    private static (int Col, int Row)? ToCanvas(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return null;
        int col = Math.Clamp((int)(x * SplatoonDrawing.CanvasWidth / width), 0, SplatoonDrawing.CanvasWidth - 1);
        int row = Math.Clamp((int)(y * SplatoonDrawing.CanvasHeight / height), 0, SplatoonDrawing.CanvasHeight - 1);
        return (col, row);
    }

    /// <summary>
    /// 图片或参数一变就重新估算并显示在右下角（取代旧版的"估算耗时"按钮）。参数有误时只在耗时处提示，不弹框；
    /// 整张抖动图的估算也只需几毫秒，拖动滑条时可实时跟随。
    /// </summary>
    private void UpdateEstimate()
    {
        _estimate = null;
        _estimateInvalid = false;
        if (Mask != null)
        {
            if (TryParseSettings(out DrawingBoardSettings? settings, out _))
                _estimate = SplatoonDrawing.Estimate(SplatoonDrawing.SearchLines(StrokeMask(Mask), settings), settings.Wait, settings.Duration);
            else
                _estimateInvalid = true;
        }
        OnPropertyChanged(nameof(EstimateText));
    }

    public string EstimateText => _estimate is { } estimate
        ? Format("Text.DrawingBoard.EstimateResult", estimate.TotalMinutes)
        : T(_estimateInvalid ? "Text.DrawingBoard.EstimateInvalid" : "Text.DrawingBoard.EstimateNone");

    /// <summary>校验时序输入框（旧版直接 int.Parse，非数字会让绘制任务静默中断），区域取 <see cref="EffectiveRegion"/>。</summary>
    private bool TryParseSettings([NotNullWhen(true)] out DrawingBoardSettings? settings, [NotNullWhen(false)] out string? error)
    {
        settings = null;
        if (!TryParseField("Text.DrawingBoard.Delay", DelayText, 0, 10000, out int wait, out error)
            || !TryParseField("Text.DrawingBoard.Duration", DurationText, 1, 10000, out int duration, out error))
            return false;
        (int startCol, int startRow, int colCount, int rowCount) = EffectiveRegion;
        settings = new DrawingBoardSettings(wait, duration, startRow, startCol, colCount, rowCount);
        return true;
    }

    private static bool TryParseField(string nameKey, string text, int min, int max, out int value, [NotNullWhen(false)] out string? error)
    {
        if (int.TryParse(text.Trim(), out value) && value >= min && value <= max)
        {
            error = null;
            return true;
        }
        error = Format("Text.DrawingBoard.InvalidField", T(nameKey), min, max);
        return false;
    }
}