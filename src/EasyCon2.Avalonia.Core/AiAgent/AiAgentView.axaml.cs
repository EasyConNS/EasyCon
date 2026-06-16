using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace EasyCon2.Avalonia.Core.AiAgent;

public partial class AiAgentView : UserControl
{
    public AiAgentView()
    {
        InitializeComponent();
        InputTextBox.AddHandler(InputElement.KeyDownEvent, InputTextBox_OnKeyDown, RoutingStrategies.Tunnel);
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

        // 回车发送：找到当前可见的按钮触发命令
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