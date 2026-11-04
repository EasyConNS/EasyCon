using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Windows.Input;

namespace EasyCon2.Avalonia.AudioLabels;

/// <summary>Owns Space before focused buttons handle it, while leaving text editing alone.</summary>
public sealed class AudioLabelKeyboard : AvaloniaObject
{
    public static readonly AttachedProperty<ICommand?> TogglePreviewProperty =
        AvaloniaProperty.RegisterAttached<AudioLabelKeyboard, Window, ICommand?>("TogglePreview");
    private static readonly AttachedProperty<Handler?> _handlerProperty =
        AvaloniaProperty.RegisterAttached<AudioLabelKeyboard, Window, Handler?>("Handler");

    static AudioLabelKeyboard()
    {
        TogglePreviewProperty.Changed.AddClassHandler<Window>((window, _) =>
        {
            window.GetValue(_handlerProperty)?.Detach();
            window.SetValue(_handlerProperty, GetTogglePreview(window) == null ? null : new Handler(window));
        });
    }

    public static ICommand? GetTogglePreview(Window window) => window.GetValue(TogglePreviewProperty);
    public static void SetTogglePreview(Window window, ICommand? value) => window.SetValue(TogglePreviewProperty, value);

    private sealed class Handler
    {
        private readonly Window _window;
        private bool _spaceOwned;

        public Handler(Window window)
        {
            _window = window;
            window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
            window.Deactivated += OnDeactivated;
            window.Closed += OnClosed;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space)
                return;
            if (_spaceOwned)
            {
                e.Handled = true;
                return;
            }
            if (e.KeyModifiers != KeyModifiers.None || IsEditing(e.Source))
                return;
            _spaceOwned = true;
            e.Handled = true;
            ICommand? command = GetTogglePreview(_window);
            if (command?.CanExecute(null) == true)
                command.Execute(null);
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && _spaceOwned)
            {
                _spaceOwned = false;
                e.Handled = true;
            }
        }

        private static bool IsEditing(object? source) => source is Visual visual
            && visual.GetSelfAndVisualAncestors().Any(ancestor => ancestor is TextBox or NumericUpDown);

        private void OnDeactivated(object? sender, EventArgs e) => _spaceOwned = false;
        private void OnClosed(object? sender, EventArgs e) => Detach();

        public void Detach()
        {
            _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _window.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
            _window.Deactivated -= OnDeactivated;
            _window.Closed -= OnClosed;
        }
    }
}