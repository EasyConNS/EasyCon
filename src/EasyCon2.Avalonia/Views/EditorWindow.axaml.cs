using Avalonia.Controls;
using AvaloniaEdit.Folding;
using EasyCon2.Avalonia.Core.Editor;
using EasyCon2.Avalonia.Core.Editor.Lsp;

namespace EasyCon2.Avalonia.Views;

public partial class EditorWindow : Window
{
    private LspClientService? _lspService;
    private FoldingManager? _foldingManager;
    private CustomFoldingStrategy? _foldingStrategy;

    public EditorWindow() : this("untitled.ecs") { }

    public EditorWindow(string filePath)
    {
        InitializeComponent();

        Title = $"编辑器 - {filePath}";

        Loaded += (_, _) => InitializeEditor(filePath);
        Closing += async (_, _) =>
        {
            Editor.Cleanup();
            if (_lspService != null)
                await _lspService.DisposeAsync();
        };
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