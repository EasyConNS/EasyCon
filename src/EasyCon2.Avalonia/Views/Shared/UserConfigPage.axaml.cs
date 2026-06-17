using Avalonia;
using Avalonia.Controls;

namespace EasyCon2.Avalonia.Views.Shared;

/// <summary>
/// 用户配置页（关于 / 配置 / 外观样式）。
/// 默认与卡片两套布局共享同一份内容，由 MainWindow 按风格分别引用。
/// </summary>
/// <remarks>
/// 布局预览的 RadioButton 需要按宿主风格隔离分组：默认风格与卡片风格同时挂在视觉树中，
/// 若共用 GroupName 会相互串改选中态。引用处通过以下两个属性传入各自前缀：
/// 默认风格走默认值（IdleLayoutOptions / RunningLayoutOptions），卡片风格传 Card* 前缀。
/// </remarks>
public partial class UserConfigPage : UserControl
{
    public static readonly StyledProperty<string> IdleLayoutGroupNameProperty =
        AvaloniaProperty.Register<UserConfigPage, string>(nameof(IdleLayoutGroupName), "IdleLayoutOptions");

    public static readonly StyledProperty<string> RunningLayoutGroupNameProperty =
        AvaloniaProperty.Register<UserConfigPage, string>(nameof(RunningLayoutGroupName), "RunningLayoutOptions");

    /// <summary>未运行时布局 RadioButton 的分组名，默认风格与卡片风格需各不相同。</summary>
    public string IdleLayoutGroupName
    {
        get => GetValue(IdleLayoutGroupNameProperty);
        set => SetValue(IdleLayoutGroupNameProperty, value);
    }

    /// <summary>运行时布局 RadioButton 的分组名，默认风格与卡片风格需各不相同。</summary>
    public string RunningLayoutGroupName
    {
        get => GetValue(RunningLayoutGroupNameProperty);
        set => SetValue(RunningLayoutGroupNameProperty, value);
    }

    public UserConfigPage()
    {
        InitializeComponent();
    }
}
