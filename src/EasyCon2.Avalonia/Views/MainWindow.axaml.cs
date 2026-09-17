using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using AvaloniaEdit.Folding;
using EasyCon2.Avalonia.Controls;
using EasyCon2.Avalonia.Core.Editor;
using EasyCon2.Avalonia.Core.Editor.Lsp;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.ViewModels;
using System.ComponentModel;

namespace EasyCon2.Avalonia.Views;

public partial class MainWindow : ChromelessWindow
{
    private readonly HashSet<ScriptEditorControl> _initializedEditors = [];
    private readonly Dictionary<ScriptEditorControl, FoldingManager> _foldingManagers = [];
    private LspClientService? _lspService;
    private CustomFoldingStrategy? _foldingStrategy;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Application.Current != null)
            Application.Current.ActualThemeVariantChanged += OnApplicationActualThemeVariantChanged;

        ThemeManager.Instance.DarkModeChanged += OnDarkModeChanged;
        ApplyEditorTheme(ThemeManager.Instance.IsDarkMode);
        UpdateSystemColorScheme();

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

    private void OnApplicationActualThemeVariantChanged(object? sender, EventArgs e)
    {
        UpdateSystemColorScheme();
    }

    private void UpdateSystemColorScheme()
    {
        if (DataContext is MainWindowViewModel vm)
            vm.UpdateSystemColorScheme(Application.Current?.ActualThemeVariant == ThemeVariant.Dark);
    }

    private void ApplyEditorTheme(bool isDarkMode)
    {
        Classes.Set("dark", isDarkMode);

        foreach (var editor in GetScriptEditors())
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
        EcsHighlightingLoader.RegisterAll();

        _lspService ??= new LspClientService();
        _foldingStrategy ??= new CustomFoldingStrategy();

        foreach (var editor in GetScriptEditors())
            EnsureEditorInitialized(editor);
    }

    private void EnsureEditorInitialized(ScriptEditorControl editor)
    {
        if (!_initializedEditors.Add(editor))
            return;

        editor.SyntaxHighlighting = EcsHighlightingLoader.GetByName("ECScript");
        editor.AttachLsp(_lspService!);

        var foldingManager = FoldingManager.Install(editor.TextArea);
        _foldingManagers[editor] = foldingManager;
        editor.EditorTextChanged += (_, _) =>
        {
            // 检查ViewModel的ShowFolding属性
            if (DataContext is MainWindowViewModel vm && vm.ShowFolding)
                _foldingStrategy?.UpdateFoldings(foldingManager, editor.TextDocument);
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

        var editors = GetScriptEditors().ToArray();
        if (editors.Length == 0) return;

        var ext = Path.GetExtension(filePath);

        // 仅对 ecs/txt 文件初始化 LSP 连接
        if (_lspService != null && !_lspService.IsConnected && LspSupportedExtensions.Contains(ext))
        {
            _ = _lspService.InitializeAsync(filePath);
        }

        foreach (var editor in editors)
        {
            // 关闭旧 LSP 文档，防止文档叠加
            editor.Clear();

            if (File.Exists(filePath))
                editor.Load(filePath);
        }
    }

    /// <summary>
    /// 保存当前编辑器内容。
    /// </summary>
    public void SaveCurrentEditor(string filePath)
    {
        var editor = GetActiveScriptEditor();
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
        if (Application.Current != null)
            Application.Current.ActualThemeVariantChanged -= OnApplicationActualThemeVariantChanged;

        // 清理编辑器资源
        foreach (var editor in GetScriptEditors())
            editor.Cleanup();
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
            foreach (var (editor, foldingManager) in _foldingManagers)
            {
                _foldingStrategy?.UpdateFoldings(foldingManager, editor.TextDocument);
            }
        }
        else
        {
            foreach (var foldingManager in _foldingManagers.Values)
                foldingManager.Clear();
        }
    }

    private IEnumerable<ScriptEditorControl> GetScriptEditors()
    {
        var classicEditor = this.FindControl<ScriptEditorControl>("ClassicScriptEditor");
        if (classicEditor != null)
            yield return classicEditor;

        var cardEditor = this.FindControl<ScriptEditorControl>("CardScriptEditor");
        if (cardEditor != null)
            yield return cardEditor;
    }

    private ScriptEditorControl? GetActiveScriptEditor()
    {
        return GetScriptEditors().FirstOrDefault(editor => editor.IsEffectivelyVisible)
            ?? GetScriptEditors().FirstOrDefault();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            WindowFrameService.UpdateWindowStatePadding(this);
            CaptionButtonsControl?.UpdateState(WindowState);
        }
    }

}