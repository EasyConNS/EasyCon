using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace EasyCon2.Avalonia.Behaviors;

/// <summary>
/// TextBox 文本变更时自动滚动到底部。用法：AutoScrollBehavior.IsEnabled="True"
/// </summary>
public class AutoScrollBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<AutoScrollBehavior, TextBox, bool>("IsEnabled");

    static AutoScrollBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>((textBox, e) =>
        {
            if (e.NewValue is true)
                textBox.TextChanged += OnTextChanged;
            else
                textBox.TextChanged -= OnTextChanged;
        });
    }

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is TextBox textBox)
        {
            var sv = textBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (sv != null)
                sv.Offset = new Vector(0, Math.Max(0, sv.Extent.Height - sv.Viewport.Height + 20));
        }
    }

    public static bool GetIsEnabled(TextBox element) => element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(TextBox element, bool value) => element.SetValue(IsEnabledProperty, value);
}