using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EasyCon2.Avalonia.Core.AiAgent;

public partial class AiAgentPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _conversationText = "AI Agent 功能待接入。";

    [ObservableProperty]
    private string _inputText = "";

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    [RelayCommand]
    private void Send()
    {
    }
}
