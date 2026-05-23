using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyCon2.Avalonia.Core.TagEditor;

namespace EasyCon2.Avalonia.Views;

public partial class TagEditorWindow : Window
{
    public TagEditorWindow()
    {
        InitializeComponent();
        DataContext = new TagEditorViewModel();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // TODO: save label data
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
