using Avalonia.Controls;
using EasyCon2.Avalonia.Core.ModelsConfig;

namespace EasyCon2.Avalonia.Views;

public partial class ModelsConfigWindow : Window
{
    public ModelsConfigWindow()
    {
        InitializeComponent();

        var vm = new ModelsConfigViewModel();
        ModelsConfig.DataContext = vm;
        ModelsConfig.LoadData();
        ModelsConfig.SaveRequested += OnSaved;
        ModelsConfig.CancelRequested += Close;
    }

    private void OnSaved()
    {
        // ConfigManager.SaveModelsConfig 会触发 ModelsConfigChanged 事件，
        // AiAgentViewModel 订阅该事件后自动 RefreshModels（含 ChatClientFactory.ClearCache）。
        Close();
    }
}