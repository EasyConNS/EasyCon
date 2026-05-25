using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Capture;
using EasyCon.Core;
using System.Collections.ObjectModel;

namespace EasyCon2.Avalonia.Core.TagEditor;

public partial class TagEditorViewModel : ObservableObject
{
    public ImgLabel Label { get; } = new();

    [ObservableProperty]
    private IImage? _sourceImage;

    [ObservableProperty]
    private IImage? _targetImage;

    [ObservableProperty]
    private IImage? _rangePreviewImage;

    public static readonly IReadOnlyList<SearchMethod> SearchMethods = ECCore.GetSearchMethods().ToList();

    #region Proxy properties from ImgLabel

    [ObservableProperty]
    private string _labelName = "";

    partial void OnLabelNameChanged(string value) => Label.name = value;

    [ObservableProperty]
    private int _targetX;

    partial void OnTargetXChanged(int value) => Label.TargetX = value;

    [ObservableProperty]
    private int _targetY;

    partial void OnTargetYChanged(int value) => Label.TargetY = value;

    [ObservableProperty]
    private int _targetWidth;

    partial void OnTargetWidthChanged(int value) => Label.TargetWidth = value;

    [ObservableProperty]
    private int _targetHeight;

    partial void OnTargetHeightChanged(int value) => Label.TargetHeight = value;

    [ObservableProperty]
    private int _rangeX;

    partial void OnRangeXChanged(int value) => Label.RangeX = value;

    [ObservableProperty]
    private int _rangeY;

    partial void OnRangeYChanged(int value) => Label.RangeY = value;

    [ObservableProperty]
    private int _rangeWidth;

    partial void OnRangeWidthChanged(int value) => Label.RangeWidth = value;

    [ObservableProperty]
    private int _rangeHeight;

    partial void OnRangeHeightChanged(int value) => Label.RangeHeight = value;

    [ObservableProperty]
    private SearchMethod _searchMethod = SearchMethod.CCoeffNormed;

    partial void OnSearchMethodChanged(SearchMethod value) => Label.searchMethod = value;

    [ObservableProperty]
    private bool _useGrayscale;

    partial void OnUseGrayscaleChanged(bool value) => Label.UseGrayscale = value;

    [ObservableProperty]
    private bool _useBinary;

    partial void OnUseBinaryChanged(bool value) => Label.UseBinary = value;

    [ObservableProperty]
    private bool _useGaussianBlur;

    partial void OnUseGaussianBlurChanged(bool value) => Label.UseGaussianBlur = value;

    [ObservableProperty]
    private bool _useOther;

    partial void OnUseOtherChanged(bool value) => Label.UseOther = value;

    #endregion

    public TagEditorViewModel() { }

    public TagEditorViewModel(ImgLabel label)
    {
        LoadFromLabel(label);
    }

    public void LoadFromLabel(ImgLabel label)
    {
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
    }

    public ImgLabel ToImgLabel() => Label with { };
}