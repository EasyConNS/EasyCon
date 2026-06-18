using Avalonia.Controls;

namespace EasyCon2.Avalonia.Views.Shared;

/// <summary>
/// 功能中心页（远程运行 / 编译烧录 / 固件 / 蓝牙手柄 / 画图）。
/// 默认与卡片两套布局共享同一份内容，由 MainWindow 按风格分别引用。
/// </summary>
public partial class FeatureCenterPage : UserControl
{
    public FeatureCenterPage()
    {
        InitializeComponent();
    }
}