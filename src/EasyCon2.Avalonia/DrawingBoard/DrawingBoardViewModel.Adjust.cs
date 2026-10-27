using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon2.Avalonia.Models;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>
/// 图片调整：像素画最近邻缩放、亮度/对比度/伽马、去除孤立杂点、轮廓模式。
/// 处理顺序：适配缩放 → 明暗调整 → 阈值/抖动 → 去杂点 → 轮廓 → 反转黑白（最后一步）。默认值不改变任何结果。
/// </summary>
public partial class DrawingBoardViewModel
{
    /// <summary>像素画：缩放用最近邻采样，放大时不出现灰色过渡、线条不会变粗或断开。</summary>
    [ObservableProperty]
    private bool _pixelArt;

    /// <summary>亮度 -100～100。</summary>
    [ObservableProperty]
    private int _toneBrightness;

    /// <summary>对比度 -100～100。</summary>
    [ObservableProperty]
    private int _toneContrast;

    /// <summary>伽马 0.2～3，1 为不调整。</summary>
    [ObservableProperty]
    private double _toneGamma = 1;

    /// <summary>去掉不超过这么多点的孤立墨点块（八方向相连算一块），0～20，0 为关闭。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeckText))]
    private int _speckSize;

    /// <summary>轮廓模式：只画墨点区域的边缘。</summary>
    [ObservableProperty]
    private bool _outlineMode;

    /// <summary>重置明暗调整时暂停逐项重算，最后统一算一次。</summary>
    private bool _suppressMaskUpdate;

    public string SpeckText => SpeckSize == 0 ? T("Text.DrawingBoard.SpeckOff") : Format("Text.DrawingBoard.SpeckValue", SpeckSize);

    partial void OnPixelArtChanged(bool value) => UpdateCanvas();

    partial void OnToneBrightnessChanged(int value) => UpdateMask();

    partial void OnToneContrastChanged(int value) => UpdateMask();

    partial void OnToneGammaChanged(double value) => UpdateMask();

    partial void OnSpeckSizeChanged(int value) => UpdateMask();

    partial void OnOutlineModeChanged(bool value) => UpdateMask();

    /// <summary>亮度、对比度、伽马恢复默认。</summary>
    [RelayCommand]
    private void ResetTone()
    {
        _suppressMaskUpdate = true;
        try
        {
            ToneBrightness = 0;
            ToneContrast = 0;
            ToneGamma = 1;
        }
        finally
        {
            _suppressMaskUpdate = false;
        }
        UpdateMask();
    }

    /// <summary>
    /// 画布图片 → 掩码：明暗调整 + 阈值/抖动，再去杂点、取轮廓，最后才反转黑白——
    /// 反转作用于处理完的结果（例如黑色轮廓线反转成黑底白线）；若先反转，轮廓取的就是背景的边，与不反转几乎一样。
    /// </summary>
    private InkMask BuildMask(PixelImage canvas)
    {
        ToneAdjust tone = new(ToneBrightness, ToneContrast, Math.Round(ToneGamma, 2));
        InkMask mask = InkMask.Binarize(canvas, new BinarizeOptions(ThresholdPercent, false, SelectedDither, tone));
        mask = mask.RemoveSpecks(SpeckSize);
        if (OutlineMode)
            mask = mask.Outline();
        return Reverse ? mask.Inverted() : mask;
    }
}