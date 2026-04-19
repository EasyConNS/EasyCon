using Avalonia.Win32.Interoperability;
using EasyCon2.Avalonia.Core.AlertConfig;

namespace EasyCon2.Forms;

public class AlertConfigForm : Form
{
    public AlertConfigForm(Action? onSave = null)
    {
        Text = "推送配置";
        Size = new Size(560, 640);
        MinimumSize = new Size(420, 400);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

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
