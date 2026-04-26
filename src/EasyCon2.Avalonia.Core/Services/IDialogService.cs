namespace EasyCon2.Avalonia.Core.Services;

public interface IDialogService
{
    void ShowMessage(string message, string title = "");
    MessageBoxResult ShowQuestion(string message, string title = "");
    string? ShowOpenFileDialog(string title, string filter);
    string? ShowSaveFileDialog(string title, string filter);
    void ShowAlertConfigDialog();
    void ShowCaptureConsole();
    void RequestClose();
}

public enum MessageBoxResult
{
    None,
    OK,
    Cancel,
    Yes,
    No,
}