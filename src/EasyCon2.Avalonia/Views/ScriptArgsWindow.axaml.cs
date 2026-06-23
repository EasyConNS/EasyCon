using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using System.Collections.Generic;
using System.Linq;

namespace EasyCon2.Avalonia.Views;

public partial class ScriptArgsWindow : Window
{
    private readonly List<TextBox> _argBoxes = [];
    private bool _confirmed;

    /// <summary>用户确认的参数列表，取消时为 null。</summary>
    public string[]? Args => _confirmed ? _argBoxes.Select(b => b.Text ?? "").ToArray() : null;

    public ScriptArgsWindow()
    {
        InitializeComponent();
        AddArgRow();
        AddArgRow();
        AddArgButton.Click += (_, _) => AddArgRow();
        RunButton.Click += OnRunClicked;
    }

    private void AddArgRow()
    {
        var idx = _argBoxes.Count;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        var label = new TextBlock
        {
            Text = $"#{idx}",
            Width = 28,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.6
        };

        var textBox = new TextBox
        {
            PlaceholderText = $"参数 {idx}",
            MinWidth = 200
        };

        panel.Children.Add(label);
        panel.Children.Add(textBox);
        ArgsPanel.Children.Add(panel);
        _argBoxes.Add(textBox);

        textBox.AttachedToVisualTree += (_, _) => textBox.Focus();
    }

    private void OnRunClicked(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }
}