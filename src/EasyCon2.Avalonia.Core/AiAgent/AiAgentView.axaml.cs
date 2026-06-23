using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Collections.Specialized;

namespace EasyCon2.Avalonia.Core.AiAgent;

public partial class AiAgentView : UserControl
{
    private AiAgentViewModel? _previousVm;

    public AiAgentView()
    {
        InitializeComponent();
        InputTextBox.AddHandler(InputElement.KeyDownEvent, InputTextBox_OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_previousVm is not null)
            _previousVm.Messages.CollectionChanged -= OnMessagesChanged;

        if (DataContext is AiAgentViewModel vm)
        {
            _previousVm = vm;
            vm.Messages.CollectionChanged += OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OutputScrollViewer.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void InputTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            InsertNewLine();
            return;
        }

        var btn = StopButton.IsVisible ? StopButton : SendButton;
        var command = btn.Command;
        if (command?.CanExecute(btn.CommandParameter) == true)
            command.Execute(btn.CommandParameter);
    }

    private void InsertNewLine()
    {
        var text = InputTextBox.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(InputTextBox.SelectionStart, InputTextBox.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(InputTextBox.SelectionStart, InputTextBox.SelectionEnd), 0, text.Length);

        InputTextBox.Text = string.Concat(text.AsSpan(0, start), Environment.NewLine, text.AsSpan(end));
        InputTextBox.CaretIndex = start + Environment.NewLine.Length;
    }
}