using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>参数：起点、区域、按键时序的输入与校验，以及随图片和参数自动更新的耗时估算。</summary>
public partial class DrawingBoardViewModel
{
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

    private TimeSpan? _estimate;
    private bool _estimateInvalid;

    // 区域与时序参数同样影响耗时
    partial void OnStartPosXTextChanged(string value) => UpdateEstimate();

    partial void OnStartPosYTextChanged(string value) => UpdateEstimate();

    partial void OnWidthTextChanged(string value) => UpdateEstimate();

    partial void OnHeightTextChanged(string value) => UpdateEstimate();

    partial void OnDurationTextChanged(string value) => UpdateEstimate();

    partial void OnDelayTextChanged(string value) => UpdateEstimate();

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

    /// <summary>
    /// 校验并读取各输入框。旧版直接 int.Parse，非数字会让绘制任务静默中断，
    /// 负数或越界的起点会在扫描时越界。
    /// </summary>
    private bool TryParseSettings([NotNullWhen(true)] out DrawingBoardSettings? settings, [NotNullWhen(false)] out string? error)
    {
        settings = null;
        if (!TryParseField("Text.DrawingBoard.Delay", DelayText, 0, 10000, out int wait, out error)
            || !TryParseField("Text.DrawingBoard.Duration", DurationText, 1, 10000, out int duration, out error)
            || !TryParseField("Text.DrawingBoard.StartRow", StartPosYText, 0, SplatoonDrawing.CanvasHeight - 1, out int startRow, out error)
            || !TryParseField("Text.DrawingBoard.StartCol", StartPosXText, 0, SplatoonDrawing.CanvasWidth - 1, out int startCol, out error)
            || !TryParseField("Text.DrawingBoard.ColCount", WidthText, 1, SplatoonDrawing.CanvasWidth, out int colCount, out error)
            || !TryParseField("Text.DrawingBoard.RowCount", HeightText, 1, SplatoonDrawing.CanvasHeight, out int rowCount, out error))
            return false;
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