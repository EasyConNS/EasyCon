using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace EasyCon2.Avalonia.Behaviors;

/// <summary>
/// ComboBox 展开时自动执行刷新命令。
/// 用法：behaviors:DropDownRefreshBehavior.RefreshCommand="{Binding RefreshCommand}"
/// </summary>
public class DropDownRefreshBehavior
{
    public static readonly AttachedProperty<ICommand> RefreshCommandProperty =
        AvaloniaProperty.RegisterAttached<DropDownRefreshBehavior, ComboBox, ICommand>("RefreshCommand");

    static DropDownRefreshBehavior()
    {
        RefreshCommandProperty.Changed.AddClassHandler<ComboBox>((comboBox, e) =>
        {
            if (e.NewValue is ICommand cmd && cmd != null)
                comboBox.DropDownOpened += OnDropDownOpened;
            else
                comboBox.DropDownOpened -= OnDropDownOpened;
        });
    }

    private static void OnDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox cb)
        {
            var cmd = cb.GetValue(RefreshCommandProperty);
            if (cmd?.CanExecute(null) == true)
                cmd.Execute(null);
        }
    }

    public static ICommand? GetRefreshCommand(ComboBox element) => element.GetValue(RefreshCommandProperty);
    public static void SetRefreshCommand(ComboBox element, ICommand value) => element.SetValue(RefreshCommandProperty, value);
}