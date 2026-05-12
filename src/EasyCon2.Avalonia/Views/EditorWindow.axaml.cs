using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit.Folding;
using EasyCon2.Avalonia.Core.Editor;
using EasyCon2.Avalonia.Core.Editor.Lsp;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace EasyCon2.Avalonia.Views;

public partial class EditorWindow : Window
{
    private LspClientService? _lspService;
    private FoldingManager? _foldingManager;
    private CustomFoldingStrategy? _foldingStrategy;
    private readonly string _filePath;

    public EditorWindow() : this("untitled.ecs") { }

    public EditorWindow(string filePath)
    {
        InitializeComponent();

        _filePath = filePath;
        Title = $"编辑器 - {filePath}";

        KeyDown += OnKeyDown;
        Loaded += (_, _) => InitializeEditor(filePath);
        Closing += OnClosing;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S)
        {
            SaveFile();
            e.Handled = true;
        }
    }

    private void SaveMenuItem_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        SaveFile();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (Editor.IsModified)
        {
            e.Cancel = true;
            _ = ConfirmCloseAsync();
            return;
        }

        Editor.Cleanup();
        _ = _lspService?.DisposeAsync();
    }

    private async Task ConfirmCloseAsync()
    {
        var box = MessageBoxManager.GetMessageBoxStandard("保存提示", "文件已修改，是否保存？",
            ButtonEnum.YesNoCancel);
        var result = await box.ShowAsync();

        if (result == ButtonResult.Cancel)
            return;

        if (result == ButtonResult.Yes)
            SaveFile();

        Editor.Cleanup();
        if (_lspService != null)
            await _lspService.DisposeAsync();

        Close();
    }

    private void SaveFile()
    {
        try
        {
            Editor.Save(_filePath);
            Editor.IsModified = false;
        }
        catch (Exception ex)
        {
            // TODO: show error dialog
        }
    }

    private void InitializeEditor(string filePath)
    {
        EcsHighlightingLoader.RegisterAll();
        Editor.SyntaxHighlighting = EcsHighlightingLoader.GetByName("ECScript");

        _lspService = new LspClientService();
        Editor.AttachLsp(_lspService);
        _ = _lspService.InitializeAsync(filePath);

        _foldingManager = FoldingManager.Install(Editor.TextArea);
        _foldingStrategy = new CustomFoldingStrategy();

        Editor.EditorTextChanged += (_, _) =>
        {
            if (_foldingManager != null)
                _foldingStrategy?.UpdateFoldings(_foldingManager, Editor.TextDocument);
        };

        if (File.Exists(filePath))
            Editor.Load(filePath);
    }
}