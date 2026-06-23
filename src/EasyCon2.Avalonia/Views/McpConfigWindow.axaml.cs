using Avalonia.Controls;
using EasyCon2.Avalonia.Core.Mcp;

namespace EasyCon2.Avalonia.Views;

public partial class McpConfigWindow : Window
{
    public McpConfigWindow()
    {
        InitializeComponent();
        var vm = new McpConfigViewModel();
        McpConfig.DataContext = vm;
        McpConfig.LoadData();
        McpConfig.SaveRequested += OnSaved;
        McpConfig.CancelRequested += Close;
    }

    private void OnSaved()
    {
        // ConfigManager.SaveMcpConfig 会触发 McpConfigChanged 事件，
        // AiAgentViewModel 订阅该事件后自动 RefreshAsync（差量重连）。
        Close();
    }
}
