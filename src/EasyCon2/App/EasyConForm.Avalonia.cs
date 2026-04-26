using Avalonia.Win32.Interoperability;
using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Avalonia.Core.ViewModels;
using EasyCon2.Avalonia.Core.Views;

namespace EasyCon2.App;

/// <summary>
/// Standalone WinForms window that hosts the Avalonia EasyMainWindow view.
/// Default entry point for the application.
/// </summary>
public partial class EasyConFormAvalonia : Form
{
    private WinFormsAvaloniaControlHost? _host;
    private EasyMainWindowViewModel? _viewModel;

    public EasyConFormAvalonia()
    {
        InitializeComponent();
        InitializeAvaloniaView();
    }

    private void InitializeAvaloniaView()
    {
        var logService = new LogService();
        var deviceService = new DeviceService(logService);
        var captureService = new CaptureService(logService);
        var scriptService = new ScriptService(logService, deviceService);
        var fileService = new FileService(logService);
        var firmwareService = new FirmwareService(logService, deviceService, scriptService);
        var configService = new ConfigService(logService);
        var dialogService = new Services.DialogService(this);

        _viewModel = new EasyMainWindowViewModel(
            logService, deviceService, scriptService, fileService,
            firmwareService, captureService, configService, dialogService);

        var easyMainView = new EasyMainWindow
        {
            DataContext = _viewModel
        };

        _host = new WinFormsAvaloniaControlHost
        {
            Dock = DockStyle.Fill
        };
        _host.Content = easyMainView;

        Controls.Add(_host);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
    }
}