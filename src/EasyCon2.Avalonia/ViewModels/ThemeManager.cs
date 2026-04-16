using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using EasyCon2.Avalonia.ViewModels;
using System.Collections.Generic;

namespace EasyCon2.Avalonia.ViewModels;

public sealed partial class ThemeManager : ViewModelBase
{
    private static readonly Lazy<ThemeManager> _instance = new(
        () => new ThemeManager(),
        LazyThreadSafetyMode.PublicationOnly
    );
    public static ThemeManager Instance => _instance.Value;

    [ObservableProperty]
    private string _themeColor = "Blue";
    public string[] ThemeColors { get; } = { "Blue", "Orange", "Green", "Red" };

    [ObservableProperty]
    private string _themeVariant = "Dark";
    public string[] ThemeVariants { get; } = { "Light", "Dark" };

    private ThemeManager()
    {
        // 初始化默认主题
        ThemeVariant = "Dark";
        ThemeColor = "Blue";
    }

    partial void OnThemeColorChanged(string value)
    {
        // 在这里可以添加主题颜色变更的逻辑
        // 但由于我们移除了SukiUI，这里暂时留空
    }

    partial void OnThemeVariantChanged(string value)
    {
        // 在这里可以添加主题变体变更的逻辑
        // 但由于我们移除了SukiUI，这里暂时留空
    }
}