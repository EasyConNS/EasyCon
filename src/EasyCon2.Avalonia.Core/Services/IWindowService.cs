namespace EasyCon2.Avalonia.Core.Services;

/// <summary>
/// 子窗口管理服务抽象，将 View 创建逻辑从 ViewModel 中解耦。
/// </summary>
public interface IWindowService
{
    void ShowESPConfigWindow();
    void ShowAlertConfigWindow();
    void ShowKeyMappingWindow();
    void ShowScriptSyntaxWindow();
}