using Avalonia;
using Avalonia.Markup.Xaml;

namespace EasyCon2.Avalonia.Core.AlertConfig;

public partial class AlertConfigApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Library mode: no main window
        base.OnFrameworkInitializationCompleted();
    }
}
