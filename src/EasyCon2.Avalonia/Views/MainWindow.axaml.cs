using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Folding;
using EasyCon2.Avalonia.Core.Editor;
using EasyCon2.Avalonia.Core.Editor.Lsp;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.ViewModels;
using System.ComponentModel;

namespace EasyCon2.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _editorInitialized;
    private LspClientService? _lspService;
    private FoldingManager? _foldingManager;
    private CustomFoldingStrategy? _foldingStrategy;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        ThemeManager.Instance.DarkModeChanged += OnDarkModeChanged;
        ApplyEditorTheme(ThemeManager.Instance.IsDarkMode);
        UpdateWindowCaptionState();

        if (DataContext is MainWindowViewModel vm)
        {
            vm.EmbeddedEditorInitializeRequested += OnEmbeddedEditorInitializeRequested;
            vm.OpenFolderDialogRequested += OnOpenFolderDialogRequested;
            vm.FoldingVisibilityChanged += OnFoldingVisibilityChanged;
        }

        // 默认初始化编辑器并连接 LSP 服务
        EnsureEditorInitialized();
        if (_lspService != null && !_lspService.IsConnected)
            _ = _lspService.InitializeAsync(string.Empty);
    }

    private void OnDarkModeChanged(bool isDarkMode)
    {
        ApplyEditorTheme(isDarkMode);
    }

    private void ApplyEditorTheme(bool isDarkMode)
    {
        Classes.Set("dark", isDarkMode);

        var editor = this.FindControl<ScriptEditorControl>("ScriptEditor");
        if (editor != null)
            editor.IsDarkTheme = isDarkMode;
    }

    private void OnEmbeddedEditorInitializeRequested(string filePath)
    {
        LoadFileInEditor(filePath);
    }

    private void OnOpenFolderDialogRequested()
    {
        _ = OpenFolderDialogAsync();
    }

    private async Task OpenFolderDialogAsync()
    {
        // 使用当前项目目录或用户文档目录作为默认位置
        var startPath = (DataContext as MainWindowViewModel)?.GetCurrentProjectDirectory()
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var startFolder = await StorageProvider.TryGetFolderFromPathAsync(startPath);

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "打开项目目录",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder
        });

        if (folders.Count > 0 && DataContext is MainWindowViewModel vm)
        {
            var path = folders[0].Path.LocalPath;
            vm.OpenProjectFromDirectory(path);
        }
    }

    /// <summary>
    /// 一次性初始化编辑器基础设施（LSP、折叠、语法高亮）。
    /// 仅在首次调用时执行，后续调用跳过。
    /// </summary>
    private void EnsureEditorInitialized()
    {
        if (_editorInitialized) return;
        _editorInitialized = true;

        var editor = this.FindControl<ScriptEditorControl>("ScriptEditor");
        if (editor == null) return;

        EcsHighlightingLoader.RegisterAll();
        editor.SyntaxHighlighting = EcsHighlightingLoader.GetByName("ECScript");

        _lspService = new LspClientService();
        editor.AttachLsp(_lspService);

        _foldingManager = FoldingManager.Install(editor.TextArea);
        _foldingStrategy = new CustomFoldingStrategy();

        editor.EditorTextChanged += (_, _) =>
        {
            // 检查ViewModel的ShowFolding属性
            if (DataContext is MainWindowViewModel vm && vm.ShowFolding && _foldingManager != null)
                _foldingStrategy?.UpdateFoldings(_foldingManager, editor.TextDocument);
        };
    }

    private static readonly HashSet<string> LspSupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ecs", ".txt"
    };

    /// <summary>
    /// 在编辑器中加载指定文件。首次调用会初始化 LSP 和折叠管理器。
    /// 切换文件时先关闭旧 LSP 文档再加载新文件。
    /// LSP 仅对 ecs/txt 文件初始化。
    /// </summary>
    private void LoadFileInEditor(string filePath)
    {
        EnsureEditorInitialized();

        var editor = this.FindControl<ScriptEditorControl>("ScriptEditor");
        if (editor == null) return;

        var ext = Path.GetExtension(filePath);

        // 仅对 ecs/txt 文件初始化 LSP 连接
        if (_lspService != null && !_lspService.IsConnected && LspSupportedExtensions.Contains(ext))
        {
            _ = _lspService.InitializeAsync(filePath);
        }

        // 关闭旧 LSP 文档，防止文档叠加
        editor.Clear();

        if (File.Exists(filePath))
            editor.Load(filePath);
    }

    /// <summary>
    /// 保存当前编辑器内容。
    /// </summary>
    public void SaveCurrentEditor(string filePath)
    {
        var editor = this.FindControl<ScriptEditorControl>("ScriptEditor");
        if (editor == null) return;
        editor.Save(filePath);
        editor.IsModified = false;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.EmbeddedEditorInitializeRequested -= OnEmbeddedEditorInitializeRequested;
            vm.OpenFolderDialogRequested -= OnOpenFolderDialogRequested;
            vm.FoldingVisibilityChanged -= OnFoldingVisibilityChanged;
            vm.OnMainWindowClosing();
        }

        ThemeManager.Instance.DarkModeChanged -= OnDarkModeChanged;

        // 清理编辑器资源
        var editor = this.FindControl<ScriptEditorControl>("ScriptEditor");
        editor?.Cleanup();
        if (_lspService != null)
            _ = _lspService.DisposeAsync();
    }

    private void MonitorArea_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ToggleMonitorVisibilityCommand.Execute(null);
    }

    private void OnFoldingVisibilityChanged(bool showFolding)
    {
        if (showFolding)
        {
            var editor = this.FindControl<ScriptEditorControl>("ScriptEditor");
            if (editor != null && _foldingManager != null && _foldingStrategy != null)
            {
                _foldingStrategy.UpdateFoldings(_foldingManager, editor.TextDocument);
            }
        }
        else
        {
            _foldingManager?.Clear();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
            UpdateWindowCaptionState();
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object? sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateWindowCaptionState()
    {
        if (MaximizeRestoreButton == null)
            return;

        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        ToolTip.SetTip(MaximizeRestoreButton, WindowState == WindowState.Maximized ? "还原" : "最大化");
    }

    private void ResizeEdge_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState != WindowState.Normal)
            return;

        if (sender is not Control { Tag: string edgeName })
            return;

        if (!Enum.TryParse<WindowEdge>(edgeName, out var edge))
            return;

        BeginResizeDrag(edge, e);
    }
}
