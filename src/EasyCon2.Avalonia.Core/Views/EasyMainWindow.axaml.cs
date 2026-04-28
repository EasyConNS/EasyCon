using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit.Folding;
using EasyCon2.Avalonia.Core.Editor;
using EasyCon2.Avalonia.Core.Editor.Lsp;
using EasyCon2.Avalonia.Core.ViewModels;
using System.Collections.Specialized;

namespace EasyCon2.Avalonia.Core.Views;

public partial class EasyMainWindow : UserControl
{
    private DispatcherTimer? _uiTimer;
    private EasyMainWindowViewModel? _vm;
    private ScriptEditorControl? _editor;
    private FoldingManager? _foldingManager;
    private CustomFoldingStrategy? _foldingStrategy;
    private LspClientService? _lspService;
    private LspFoldingStrategy? _lspFoldingStrategy;
    private bool _lspFoldingWired;

    public EasyMainWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _vm = DataContext as EasyMainWindowViewModel;
        if (_vm == null) return;

        _editor = this.FindControl<ScriptEditorControl>("ScriptEditor");
        if (_editor == null) return;

        // Initialize LSP if not already attached (XAML-declared editor path)
        InitializeLsp();

        // Wire editor access to FileService
        _vm.FileService.SetEditorAccess(
            () => _editor.Text,
            text => _editor.Text = text,
            () => _editor.IsModified,
            val => _editor.IsModified = val);

        // File drop support
        _editor.FileDropped += path =>
        {
            if (_vm == null) return;
            var fs = _vm.FileService;
            if (fs.IsModified)
            {
                var r = _vm.Dialog.ShowQuestion("文件已编辑，是否保存？");
                if (r == Services.MessageBoxResult.Cancel) return;
                if (r == Services.MessageBoxResult.Yes)
                    fs.FileSave();
            }
            fs.FileOpen(path);
        };

        _editor.EnableAutoCompletion = _vm.EnableAutoCompletion;
        _editor.SetFontSize(_vm.EditorFontSize);
        _editor.FontSizeChanged += size =>
        {
            _vm.EditorFontSize = size;
        };

        // Propagate EnableAutoCompletion changes to editor
        _vm.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(EasyMainWindowViewModel.EnableAutoCompletion) && _editor != null)
                _editor.EnableAutoCompletion = _vm.EnableAutoCompletion;
        };

        // Wire editor operation events from ViewModel
        _vm.OpenSearchRequested += () => _editor.OpenSearchPanel();
        _vm.FindNextRequested += () => _editor.FindNext();
        _vm.ToggleCommentRequested += () => _editor.ToggleComment();

        // Folding setup
        _foldingManager = FoldingManager.Install(_editor.TextArea);
        _foldingStrategy = new CustomFoldingStrategy();

        // Wire LSP folding (delayed until LSP connects)
        TryWireLspFolding();

        if (_vm.ShowFolding)
            UpdateAllFoldings();

        _vm.ShowFoldingChanged += show =>
        {
            if (_foldingManager == null) return;
            if (show)
                UpdateAllFoldings();
            else
                _foldingManager.Clear();
        };

        _editor.EditorTextChanged += (_, _) =>
        {
            if (_vm.ShowFolding && _foldingManager != null)
                UpdateAllFoldings();
        };

        // Auto-scroll log on new entries
        _vm.LogEntries.CollectionChanged += OnLogEntriesChanged;

        // Populate serial ports
        _vm.RefreshSerialPorts();

        // UI tick timer (25ms)
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _uiTimer.Tick += (_, _) =>
        {
            if (_vm == null) return;
            _vm.OnUITick();
        };
        _uiTimer.Start();
    }

    private void InitializeLsp()
    {
        if (_editor == null) return;

        EcsHighlightingLoader.RegisterAll();

        if (_editor.SyntaxHighlighting == null)
            _editor.SyntaxHighlighting = EcsHighlightingLoader.GetByName("ECScript");

        if (_editor.LspService != null) return;

        _lspService = new LspClientService();
        _editor.AttachLsp(_lspService);
        _ = _lspService.InitializeAsync("untitled.ecs");
    }

    private void TryWireLspFolding()
    {
        if (_lspFoldingWired || _editor == null) return;

        var lspService = _editor.LspService;
        if (lspService == null) return;

        if (lspService.IsConnected)
        {
            _lspFoldingStrategy = new LspFoldingStrategy(lspService);
            _lspFoldingWired = true;
        }
        else
        {
            lspService.Connected += OnLspConnectedForFolding;
        }
    }

    private void OnLspConnectedForFolding()
    {
        if (_editor?.LspService == null) return;
        _lspFoldingStrategy = new LspFoldingStrategy(_editor.LspService);
        _lspFoldingWired = true;
        _editor.LspService.Connected -= OnLspConnectedForFolding;
    }

    private void UpdateAllFoldings()
    {
        if (_foldingManager == null || _editor == null) return;

        _foldingStrategy?.UpdateFoldings(_foldingManager, _editor.TextDocument);

        if (_lspFoldingStrategy != null)
            _ = _lspFoldingStrategy.UpdateFoldingsAsync(_foldingManager, _editor.TextDocument);
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            var listBox = this.FindControl<ListBox>("LogListBox");
            if (listBox != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    listBox.ScrollIntoView(_vm!.LogEntries.Count - 1);
                }, DispatcherPriority.Background);
            }
        }
    }

    public ScriptEditorControl? GetEditor() => _editor;
    public EasyMainWindowViewModel? GetViewModel() => _vm;
}
