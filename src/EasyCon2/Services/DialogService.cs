using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Forms;

namespace EasyCon2.Services;

/// <summary>
/// WinForms implementation of IDialogService.
/// </summary>
public class DialogService : IDialogService
{
    private System.Windows.Forms.Form? _parentForm;

    public DialogService(System.Windows.Forms.Form? parentForm = null)
    {
        _parentForm = parentForm;
    }

    public void ShowMessage(string message, string title = "")
    {
        System.Windows.Forms.MessageBox.Show(_parentForm, message, title);
    }

    public MessageBoxResult ShowQuestion(string message, string title = "")
    {
        var result = System.Windows.Forms.MessageBox.Show(_parentForm, message, title, MessageBoxButtons.YesNoCancel);
        return result switch
        {
            DialogResult.Yes => MessageBoxResult.Yes,
            DialogResult.No => MessageBoxResult.No,
            DialogResult.Cancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.None
        };
    }

    public string? ShowOpenFileDialog(string title, string filter)
    {
        using var dlg = new OpenFileDialog { Title = title, Filter = filter, RestoreDirectory = true };
        return dlg.ShowDialog(_parentForm) == DialogResult.OK ? dlg.FileName : null;
    }

    public string? ShowSaveFileDialog(string title, string filter)
    {
        using var dlg = new SaveFileDialog { Title = title, Filter = filter, RestoreDirectory = true };
        return dlg.ShowDialog(_parentForm) == DialogResult.OK ? dlg.FileName : null;
    }

    public void ShowAlertConfigDialog()
    {
        using var form = new AlertConfigForm();
        form.ShowDialog(_parentForm);
    }

    public void ShowCaptureConsole()
    {
        System.Windows.Forms.MessageBox.Show(_parentForm, "搜图控制台功能开发中", "搜图");
    }

    public void RequestClose()
    {
        _parentForm?.Close();
    }
}