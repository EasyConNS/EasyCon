using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using System;
using System.ComponentModel;
using System.Linq;

namespace EasyCon2.Avalonia.Core.TagEditor;

public partial class TagEditorView : UserControl
{
    private const double PresetWidth = 1511.0;
    private const double PresetHeight = 944.0;
    private const double MinScale = 0.62;
    private const double MaxScale = 1.0;

    public TagEditorView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyPresetScale();
        AttachedToVisualTree += (_, _) => ApplyPresetScale();
    }

    private void ApplyPresetScale()
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        var scale = Math.Clamp(Math.Min(width / PresetWidth, height / PresetHeight), MinScale, MaxScale);

        var numberHeight = Scale(30, scale, 28);
        var spinButtonWidth = Scale(18, scale, 16);
        var spinButtonHeight = Math.Round(numberHeight / 2, 1);
        var fieldHeight = Scale(38, scale, 30);
        var previewWidth = Scale(150, scale, 108);
        var previewHeight = Scale(100, scale, 72);
        var previewLabelWidth = Scale(30, scale, 24);
        var previewColumnWidth = previewWidth + previewLabelWidth + Scale(18, scale, 12);
        var previewActionWidth = Scale(96, scale, 82);
        var fontSize = Scale(16, scale, 13);
        var numberFontSize = Scale(14, scale, 12);
        var numberWidth = GetHalfWidthCharacterWidth(numberFontSize, 6, Scale(12, scale, 9));
        var coordinateEditorWidth = numberWidth + Scale(20, scale, 18);
        var searchWidth = GetSearchMethodWidth(fontSize);
        var parameterOptionWidth = GetParameterOptionWidth(fontSize);
        var labelMinimumWidth = Math.Ceiling(fontSize * 6 + Scale(32, scale, 24));
        var formLabelColumnWidth = Scale(76, scale, 64);
        var formColumnSpacing = Scale(8, scale, 5);
        var parameterColumnSpacing = Scale(14, scale, 8);
        var parameterLeftMargin = Scale(18, scale, 8);
        var parameterRightMargin = Scale(12, scale, 6);

        ParameterLayout.ColumnSpacing = parameterColumnSpacing;
        ParameterLayout.Margin = new global::Avalonia.Thickness(parameterLeftMargin, 0, parameterRightMargin, 0);
        ParameterLayout.ColumnDefinitions[1].Width = new GridLength(previewColumnWidth);

        FieldsLayout.RowSpacing = Scale(18, scale, 10);
        HeaderFieldsGrid.ColumnSpacing = formColumnSpacing;
        HeaderFieldsGrid.ColumnDefinitions[0].Width = new GridLength(formLabelColumnWidth);
        HeaderFieldsGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);

        LabelNameBox.Height = fieldHeight;
        LabelNameBox.Width = double.NaN;
        LabelNameBox.MinWidth = labelMinimumWidth;
        LabelNameBox.MaxWidth = double.PositiveInfinity;
        HeaderActionPanel.Spacing = Scale(8, scale, 5);
        foreach (var button in HeaderActionPanel.Children.OfType<Button>())
        {
            button.Height = fieldHeight;
            button.FontSize = fontSize;
            button.Padding = new global::Avalonia.Thickness(Scale(14, scale, 10), 0);
        }

        PreviewActionPanel.Width = previewActionWidth;
        PreviewActionPanel.Spacing = Scale(8, scale, 5);
        PreviewActionPanel.Margin = new global::Avalonia.Thickness(Scale(8, scale, 5));
        foreach (var button in PreviewActionPanel.Children.OfType<Button>())
        {
            button.Width = previewActionWidth;
            button.Height = fieldHeight;
            button.FontSize = fontSize;
            button.Padding = new global::Avalonia.Thickness(Scale(10, scale, 8), 0);
        }

        SearchMethodBox.Height = fieldHeight;
        SearchMethodBox.MinWidth = Scale(120, scale, 86);
        ParameterOptionBox.Height = fieldHeight;
        ParameterOptionBox.MinWidth = Scale(90, scale, 74);
        PreprocessOptionsPanel.ColumnDefinitions[0].Width = new GridLength(formLabelColumnWidth);
        PreprocessOptionsPanel.ColumnDefinitions[1].Width = new GridLength(searchWidth);
        PreprocessOptionsPanel.ColumnDefinitions[3].Width = new GridLength(parameterOptionWidth);
        PreprocessOptionsPanel.ColumnSpacing = formColumnSpacing;

        CoordinateSectionsGrid.ColumnSpacing = Scale(28, scale, 12);
        CoordinateSectionsGrid.ColumnDefinitions[1].Width = new GridLength(Scale(32, scale, 8));
        TargetSection.Spacing = Scale(10, scale, 6);
        RangeSection.Spacing = Scale(10, scale, 6);
        ApplyCoordinateGridScale(TargetCoordinateGrid, coordinateEditorWidth, scale);
        ApplyCoordinateGridScale(RangeCoordinateGrid, coordinateEditorWidth, scale);
        ApplyParameterLayoutWidth(
            scale,
            fontSize,
            labelMinimumWidth,
            searchWidth,
            parameterOptionWidth,
            coordinateEditorWidth,
            previewColumnWidth,
            parameterColumnSpacing,
            parameterLeftMargin,
            parameterRightMargin);

        foreach (var number in this.GetVisualDescendants().OfType<NumericUpDown>())
        {
            if (!number.Classes.Contains("coordinate-number"))
                continue;

            number.Width = numberWidth;
            number.MinWidth = numberWidth;
            number.Height = numberHeight;
            number.FontSize = numberFontSize;
            number.HorizontalContentAlignment = HorizontalAlignment.Center;
            number.VerticalContentAlignment = VerticalAlignment.Center;
            number.TextAlignment = global::Avalonia.Media.TextAlignment.Center;
        }

        foreach (var button in this.GetVisualDescendants().OfType<Button>())
        {
            if (!button.Classes.Contains("coordinate-spin-button"))
                continue;

            button.Width = spinButtonWidth;
            button.MinWidth = spinButtonWidth;
            button.Height = spinButtonHeight;
            button.MinHeight = spinButtonHeight;
            button.FontSize = numberFontSize;
            button.Padding = new global::Avalonia.Thickness(0);
        }

        foreach (var textBlock in this.GetVisualDescendants().OfType<TextBlock>())
        {
            if (textBlock.Classes.Contains("coordinate-label") ||
                textBlock.Classes.Contains("section-title"))
                textBlock.FontSize = fontSize;
        }

        PreviewPanel.Spacing = Scale(12, scale, 7);
        TargetPreviewRow.ColumnSpacing = Scale(8, scale, 5);
        ResultPreviewRow.ColumnSpacing = Scale(8, scale, 5);
        TargetPreviewLabel.Width = previewLabelWidth;
        ResultPreviewLabel.Width = previewLabelWidth;
        TargetPreviewLabel.FontSize = fontSize;
        ResultPreviewLabel.FontSize = fontSize;
        MatchScoreText.FontSize = fontSize;
        TargetPreviewBox.Width = previewWidth;
        TargetPreviewBox.Height = previewHeight;
        ResultPreviewBox.Width = previewWidth;
        ResultPreviewBox.Height = previewHeight;
    }

    private static double Scale(double value, double scale, double minimum)
    {
        return Math.Max(minimum, Math.Round(value * scale, 1));
    }

    private static double GetSearchMethodWidth(double fontSize)
    {
        var longest = TagEditorViewModel.SearchMethods
            .Select(GetSearchMethodDescription)
            .DefaultIfEmpty("")
            .Max(text => text.Length);

        return Math.Ceiling(longest * fontSize + 58);
    }

    private void ApplyParameterLayoutWidth(
        double scale,
        double fontSize,
        double labelMinimumWidth,
        double searchWidth,
        double parameterOptionWidth,
        double numberWidth,
        double previewColumnWidth,
        double columnSpacing,
        double leftMargin,
        double rightMargin)
    {
        var minimumWidth = GetParameterMinimumWidth(
            scale,
            fontSize,
            labelMinimumWidth,
            searchWidth,
            parameterOptionWidth,
            numberWidth,
            previewColumnWidth,
            columnSpacing);

        ParameterLayout.MinWidth = minimumWidth;

        var viewportWidth = ParameterScroll.Bounds.Width;
        if (viewportWidth <= 0)
        {
            ParameterLayout.Width = double.NaN;
            return;
        }

        var availableWidth = Math.Max(0, viewportWidth - leftMargin - rightMargin);
        ParameterLayout.Width = Math.Max(minimumWidth, Math.Round(availableWidth, 1));
    }

    private double GetParameterMinimumWidth(
        double scale,
        double fontSize,
        double labelMinimumWidth,
        double searchWidth,
        double parameterOptionWidth,
        double numberWidth,
        double previewColumnWidth,
        double columnSpacing)
    {
        var headerMinimumWidth =
            GetTextWidth("标签名称:", fontSize) +
            labelMinimumWidth +
            HeaderFieldsGrid.ColumnSpacing * 2 +
            GetHeaderActionMinimumWidth(scale, fontSize);

        var preprocessMinimumWidth =
            GetTextWidth("搜索方法:", fontSize) +
            searchWidth +
            GetTextWidth("参数:", fontSize) +
            parameterOptionWidth +
            PreprocessOptionsPanel.ColumnSpacing * 3;

        var coordinateSectionWidth =
            GetTextWidth("X:", fontSize) +
            numberWidth +
            GetTextWidth("Y:", fontSize) +
            numberWidth +
            Scale(6, scale, 3) * 3;

        var coordinateMinimumWidth =
            coordinateSectionWidth * 2 +
            CoordinateSectionsGrid.ColumnSpacing +
            CoordinateSectionsGrid.ColumnDefinitions[1].Width.Value;

        var leftMinimumWidth = Math.Ceiling(Math.Max(
            Math.Max(headerMinimumWidth, preprocessMinimumWidth),
            coordinateMinimumWidth));

        return leftMinimumWidth + columnSpacing + previewColumnWidth;
    }

    private double GetHeaderActionMinimumWidth(double scale, double fontSize)
    {
        var horizontalPadding = Scale(14, scale, 10) * 2;
        return GetTextWidth("保存", fontSize) + horizontalPadding;
    }

    private static double GetTextWidth(string text, double fontSize)
    {
        return Math.Ceiling(text.Length * fontSize + 4);
    }

    private static double GetHalfWidthCharacterWidth(double fontSize, int characterCount, double horizontalPadding)
    {
        return Math.Ceiling(fontSize * 0.62 * characterCount + horizontalPadding);
    }

    private static double GetParameterOptionWidth(double fontSize)
    {
        var longest = TagEditorViewModel.ParameterOptions
            .DefaultIfEmpty("")
            .Max(text => text.Length);

        return Math.Ceiling(longest * fontSize + 52);
    }

    private static string GetSearchMethodDescription(SearchMethod method)
    {
        return method.GetType().GetField(method.ToString())?
            .GetCustomAttributes(typeof(DescriptionAttribute), false)
            .Cast<DescriptionAttribute>()
            .FirstOrDefault()?.Description ?? method.ToString();
    }

    private static void ApplyCoordinateGridScale(Grid grid, double editorWidth, double scale)
    {
        grid.ColumnSpacing = Scale(6, scale, 3);
        grid.RowSpacing = Scale(10, scale, 5);
        if (grid.ColumnDefinitions.Count < 4)
            return;

        grid.ColumnDefinitions[1].Width = new GridLength(editorWidth);
        grid.ColumnDefinitions[3].Width = new GridLength(editorWidth);
    }
}
