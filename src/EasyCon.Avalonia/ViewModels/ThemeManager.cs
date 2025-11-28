using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using SukiUI;
using SukiUI.Enums;

namespace EC.Avalonia.ViewModels;

public sealed partial class ThemeManager : ViewModelBase
{
    private static readonly Lazy<ThemeManager> _instance = new(
        () => new ThemeManager(),
        LazyThreadSafetyMode.PublicationOnly
    );
    public static ThemeManager Instance => _instance.Value;

    [ObservableProperty]
    public partial SukiColor ThemeColor { get; set; }
    public SukiColor[] ThemeColors { get; }

    [ObservableProperty]
    public partial ThemeVariant ThemeVariant { get; set; }
    public ThemeVariant[] ThemeVariants { get; }

    private ThemeManager()
    {
        SukiTheme thems = SukiTheme.GetInstance();
        ThemeVariant = thems.ActiveBaseTheme;
        ThemeVariants = [ThemeVariant.Default, ThemeVariant.Light, ThemeVariant.Dark];
        ThemeColors = [SukiColor.Blue, SukiColor.Orange, SukiColor.Green, SukiColor.Red];
        ThemeColor = thems.ThemeColor;
    }

    partial void OnThemeColorChanged(SukiColor value)
    {
        SukiTheme.GetInstance().ChangeColorTheme(value);
    }

    partial void OnThemeVariantChanged(ThemeVariant value)
    {
        SukiTheme.GetInstance().ChangeBaseTheme(value);
    }
}
