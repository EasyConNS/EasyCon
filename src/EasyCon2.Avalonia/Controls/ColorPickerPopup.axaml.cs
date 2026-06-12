using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace EasyCon2.Avalonia.Controls;

public partial class ColorPickerPopup : Window
{
    public Color SelectedColor { get; private set; }

    public ColorPickerPopup() : this(Colors.Black) { }

    public ColorPickerPopup(Color initialColor)
    {
        SelectedColor = initialColor;
        InitializeComponent();

        // Find controls
        var sliderR = this.FindControl<Slider>("SliderR")!;
        var sliderG = this.FindControl<Slider>("SliderG")!;
        var sliderB = this.FindControl<Slider>("SliderB")!;
        var preview = this.FindControl<Border>("Preview")!;
        var hexBox = this.FindControl<TextBox>("HexBox")!;

        sliderR.Value = initialColor.R;
        sliderG.Value = initialColor.G;
        sliderB.Value = initialColor.B;
        hexBox.Text = $"#{initialColor.R:X2}{initialColor.G:X2}{initialColor.B:X2}";

        void UpdateColor()
        {
            var c = Color.FromRgb((byte)sliderR.Value, (byte)sliderG.Value, (byte)sliderB.Value);
            preview.Background = new SolidColorBrush(c);
            hexBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            SelectedColor = c;
        }

        sliderR.ValueChanged += (_, _) => UpdateColor();
        sliderG.ValueChanged += (_, _) => UpdateColor();
        sliderB.ValueChanged += (_, _) => UpdateColor();
        UpdateColor();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Close(SelectedColor);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}