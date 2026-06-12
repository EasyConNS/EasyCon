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

        var command = SendButton.Command;
        var parameter = SendButton.CommandParameter;
        if (command?.CanExecute(parameter) == true)
            command.Execute(parameter);
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
