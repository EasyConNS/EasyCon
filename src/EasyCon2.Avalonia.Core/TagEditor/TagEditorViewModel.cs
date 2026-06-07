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
    private IImage? _sourceImage;

    [ObservableProperty]
    private IImage? _targetImage;

    [ObservableProperty]
    private IImage? _rangePreviewImage;

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
        SelectedParameterOption = GetParameterOption();

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
