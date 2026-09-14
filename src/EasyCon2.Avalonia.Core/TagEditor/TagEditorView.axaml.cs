using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EasyCon2.Avalonia.Core.TagEditor;

public partial class TagEditorView : UserControl
{
    public TagEditorView()
    {
        InitializeComponent();
    }

    private async void CopyOcrCall(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TagEditorViewModel viewModel || string.IsNullOrWhiteSpace(viewModel.OcrCall))
            return;

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
                return;

            var transfer = new global::Avalonia.Input.DataTransfer();
            transfer.Add(global::Avalonia.Input.DataTransferItem.CreateText(viewModel.OcrCall));
            await clipboard.SetDataAsync(transfer);
        }
        catch
        {
            // Clipboard can be unavailable on headless or restricted platforms.
        }
    }
}