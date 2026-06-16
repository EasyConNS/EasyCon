using Avalonia.Controls;
using System.Reflection;

namespace EasyCon2.Avalonia.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        // 版本号显示完整的 InformationalVersion（如 1.7.0-alpha+sha），不截断。
        var fullVer = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        VersionText.Text = $"Version v{fullVer}";
    }
}