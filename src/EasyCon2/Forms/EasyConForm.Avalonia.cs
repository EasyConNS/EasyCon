using Avalonia.Win32.Interoperability;
using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Avalonia.Core.ViewModels;
using EasyCon2.Avalonia.Core.Views;
using EasyCon2.Services;

namespace EasyCon2.Forms;

/// <summary>
/// Standalone WinForms window that hosts the Avalonia EasyMainWindow view.
/// Default entry point for the application.
/// </summary>
public class EasyConFormAvalonia : Form
{
    private WinFormsAvaloniaControlHost? _host;
    private EasyMainWindowViewModel? _viewModel;

    public EasyConFormAvalonia()
    {
        Text = "伊机控 EasyCon";
        Size = new Size(910, 700);
        MinimumSize = new Size(800, 500);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        InitializeAvaloniaView();
    }

    private void InitializeAvaloniaView()
    {
        // Create services
        var logService = new LogService();
        var deviceService = new DeviceService(logService);
        var captureService = new CaptureService(logService);
        var scriptService = new ScriptService(logService, deviceService);
        var fileService = new FileService(logService);
        var firmwareService = new FirmwareService(logService, deviceService, scriptService);
        var configService = new ConfigService(logService);
        var dialogService = new DialogService(this);

        // Create ViewModel
        _viewModel = new EasyMainWindowViewModel(
            logService, deviceService, scriptService, fileService,
            firmwareService, captureService, configService, dialogService);

        // Create Avalonia view
        var easyMainView = new EasyMainWindow
        {
            DataContext = _viewModel
        };

        // Host in WinForms
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