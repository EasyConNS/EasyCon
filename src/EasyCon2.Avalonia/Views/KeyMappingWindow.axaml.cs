using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EasyCon2.Avalonia.ViewModels;

namespace EasyCon2.Avalonia.Views;

public partial class KeyMappingWindow : Window
{
    private KeyMappingViewModel? _vm;

    public KeyMappingWindow()
    {
        InitializeComponent();
        // 使用 Tunnel 策略：在子控件（Button）处理按键之前先截获
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm != null)
            _vm.RequestClose -= OnRequestClose;

        _vm = DataContext as KeyMappingViewModel;
        if (_vm != null)
            _vm.RequestClose += OnRequestClose;
    }

    private void OnRequestClose() => Close();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        _vm?.OnKeyDown(e.Key);
        e.Handled = true;
    }
}