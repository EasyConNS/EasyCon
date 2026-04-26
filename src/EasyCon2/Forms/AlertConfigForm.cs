using Avalonia.Win32.Interoperability;
using EasyCon2.Avalonia.Core.AlertConfig;

namespace EasyCon2.Forms;

public partial class AlertConfigForm : Form
{
    public AlertConfigForm(Action? onSave = null)
    {
        InitializeComponent();

        var host = new WinFormsAvaloniaControlHost
        {
            Dock = DockStyle.Fill
        };

        var control = AlertConfigHost.CreateControl(onSave);
        control.SaveRequested += () =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        control.CancelRequested += () =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        host.Content = control;
        Controls.Add(host);
    }
}