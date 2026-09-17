using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Search;
using EasyCon.Core;
using EasyCon.Core.Config;
using EasyCon2.Avalonia.Core.Editor.Lsp;
using System.Diagnostics;

namespace EasyCon2.Avalonia.Core.Editor;

public partial class ScriptEditorControl : UserControl
{
    private const double MinFontSize = 8;
    private const double MaxFontSize = 36;

    private readonly TextEditor _editor;
    private readonly SearchPanel _searchPanel;
    private CodeCompletionController? _completionController;

    private LspClientService? _lspService;
    private LspCompletionAdapter? _lspCompletionAdapter;
    private LspHoverHandler? _lspHoverHandler;
    private LspDefinitionHandler? _lspDefinitionHandler;
    private DispatcherTimer? _lspChangeThrottle;
    private bool _lspDocumentOpened;
    private bool _disposed;

    public event EventHandler? EditorTextChanged;
    public event Action<string>? FileDropped;
    public event Action<double>? FontSizeChanged;

    public TextEditor TextEditor => _editor;

    private bool _isDarkTheme;

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (_isDarkTheme == value) return;
            _isDarkTheme = value;
            ApplyThemeColors();
        }
    }

    private bool _enableAutoCompletion;

    public static readonly DirectProperty<ScriptEditorControl, bool> EnableAutoCompletionProperty =
        AvaloniaProperty.RegisterDirect<ScriptEditorControl, bool>(
            nameof(EnableAutoCompletion),
            o => o.EnableAutoCompletion,
            (o, v) => o.EnableAutoCompletion = v);

    public bool EnableAutoCompletion
    {
        get => _enableAutoCompletion;
        set
        {
            if (_enableAutoCompletion == value) return;
            var oldValue = _enableAutoCompletion;
            _enableAutoCompletion = value;
            if (_completionController != null)
                _completionController.EnableAutoCompletion = value;
            RaisePropertyChanged(EnableAutoCompletionProperty, oldValue, value);
        }
    }

    public static readonly DirectProperty<ScriptEditorControl, string> EditorTextProperty =
        AvaloniaProperty.RegisterDirect<ScriptEditorControl, string>(
            nameof(EditorText),
            o => o.EditorText,
            (o, v) => o.EditorText = v,
            defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    private string _editorText = string.Empty;

    public string EditorText
    {
        get => _editorText;
        set
        {
            if (_editorText == value) return;
            _editorText = value;
            _editor.Text = value;
            TryOpenLspDocument();
            RaisePropertyChanged(EditorTextProperty, default, value);
        }
    }

    public string Text
    {
        get => _editor.Text;
        set
        {
            _editor.Text = value;
            TryOpenLspDocument();
        }
    }

    public bool IsModified
    {
        get => _editor.IsModified;
        set => _editor.IsModified = value;
    }

    public bool IsReadOnly
    {
        get => _editor.IsReadOnly;
        set => _editor.IsReadOnly = value;
    }

    public string? FileName
    {
        get => _editor.Document?.FileName;
        set
        {
            if (_editor.Document != null)
                _editor.Document.FileName = value;
        }
    }

    public IHighlightingDefinition? SyntaxHighlighting
    {
        get => _editor.SyntaxHighlighting;
        set
        {
            if (_isDarkTheme && value != null && !value.Name.EndsWith("-Dark"))
            {
                var dark = EcsHighlightingLoader.GetByName(value.Name + "-Dark");
                _editor.SyntaxHighlighting = dark ?? value;
            }
            else
            {
                _editor.SyntaxHighlighting = value;
            }
        }
    }

    public int SelectionStart => _editor.SelectionStart;
    public int SelectionLength => _editor.SelectionLength;
    public string SelectedText => _editor.SelectedText;

    public ScriptEditorControl()
    {
        InitializeComponent();
        _editor = this.FindControl<TextEditor>("Editor")!;
        _editor.TextChanged += OnTextChanged;

        SetupDragDrop();
        _searchPanel = SearchPanel.Install(_editor);

        _editor.AddHandler(PointerWheelChangedEvent, OnEditorPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _editorText = _editor.Text;
        RaisePropertyChanged(EditorTextProperty, default, _editorText);
        EditorTextChanged?.Invoke(this, EventArgs.Empty);

        if (!_lspDocumentOpened)
        {
            TryOpenLspDocument();
            return;
        }

        if (_lspChangeThrottle != null)
        {
            _lspChangeThrottle.Stop();
            _lspChangeThrottle.Start();
        }
    }

    public void SetFontSize(double size)
    {
        _editor.FontSize = Math.Clamp(size, MinFontSize, MaxFontSize);
    }

    public void AttachLsp(LspClientService lspService)
    {
        if (_disposed) return;

        _lspService = lspService;
        _lspService.Connected += OnLspConnected;
        _lspService.ConnectionFailed += OnLspConnectionFailed;

        _lspChangeThrottle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _lspChangeThrottle.Tick += OnLspChangeThrottleTick;
    }

    private void OnLspConnected()
    {
        if (_disposed || _lspService == null) return;

        try
        {
            _lspCompletionAdapter = new LspCompletionAdapter(_lspService);
            _completionController = new CodeCompletionController(_editor, _lspCompletionAdapter);
            _completionController.EnableAutoCompletion = EnableAutoCompletion;

            _lspHoverHandler = new LspHoverHandler(_editor, _lspService);
            _lspDefinitionHandler = new LspDefinitionHandler(_editor, _lspService);

            TryOpenLspDocument();
            UpdateImgLabels();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LSP] OnLspConnected error: {ex.Message}");
        }
    }

    private void OnLspConnectionFailed(string message)
    {
        if (_disposed) return;
        Debug.WriteLine($"[LSP] Connection failed: {message}");
    }

    private static readonly HashSet<string> LspSupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ecs", ".txt"
    };

    private void TryOpenLspDocument()
    {
        if (_lspService == null || !_lspService.IsConnected || _lspDocumentOpened)
            return;

        var path = FileName;
        if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(_editor.Text))
            return;

        if (string.IsNullOrEmpty(path))
            path = "untitled.ecs";

        // LSP 仅响应 ecs/txt 文件
        var ext = System.IO.Path.GetExtension(path);
        if (!LspSupportedExtensions.Contains(ext))
            return;

        _lspService.DocumentManager.OpenDocument(path, _editor.Text);
        _lspDocumentOpened = true;
    }

    private void OnLspChangeThrottleTick(object? sender, EventArgs e)
    {
        _lspChangeThrottle?.Stop();
        if (_lspService?.IsConnected != true || !_lspDocumentOpened)
            return;

        _lspService.DocumentManager.UpdateDocument(_editor.Text);
    }

    private void OnEditorPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        var newSize = _editor.FontSize + (e.Delta.Y > 0 ? 1 : -1);
        newSize = Math.Clamp(newSize, MinFontSize, MaxFontSize);
        if (Math.Abs(newSize - _editor.FontSize) < 0.01) return;

        _editor.FontSize = newSize;
        FontSizeChanged?.Invoke(newSize);
        e.Handled = true;
    }

    public void OpenSearchPanel() => _searchPanel.Open();
    public void FindNext() => _searchPanel.Open();

    public void ToggleComment()
    {
        int startOffset = _editor.SelectionStart;
        int endOffset = startOffset + _editor.SelectionLength;

        var doc = _editor.Document;
        var startLine = doc.GetLineByOffset(startOffset);
        var endLine = doc.GetLineByOffset(endOffset);

        var docomment = false;
        for (int lineNum = endLine.LineNumber; lineNum >= startLine.LineNumber; lineNum--)
        {
            var line = doc.GetLineByNumber(lineNum);
            if (doc.GetText(line).CanComment())
            {
                docomment = true;
                break;
            }
        }

        using (doc.RunUpdate())
        {
            for (int lineNum = endLine.LineNumber; lineNum >= startLine.LineNumber; lineNum--)
            {
                var line = doc.GetLineByNumber(lineNum);
                var text = doc.GetText(line);
                text = text.ToggleComment(docomment);
                doc.Replace(line, text);
            }
        }
    }

    private void SetupDragDrop()
    {
        DragDrop.SetAllowDrop(_editor, true);
        DragDrop.AddDragOverHandler(_editor, OnDragOver);
        DragDrop.AddDropHandler(_editor, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        var files = e.DataTransfer.TryGetFiles();
        var path = files?.FirstOrDefault()?.Path.LocalPath;
        if (path != null)
            FileDropped?.Invoke(path);
    }

    private void UpdateImgLabels()
    {
        if (_lspCompletionAdapter == null) return;

        var filePath = FileName;
        if (string.IsNullOrEmpty(filePath))
        {
            _lspCompletionAdapter.UpdateImgLabels([]);
            return;
        }

        var scriptDir = Path.GetDirectoryName(filePath);
        if (scriptDir == null || !Directory.Exists(Path.Combine(scriptDir, "ImgLabel")))
        {
            _lspCompletionAdapter.UpdateImgLabels([]);
            return;
        }

        try
        {
            var (labels, _, _) = ECCore.LoadImgLabels(scriptDir, AppPaths.DataDir);
            _lspCompletionAdapter.UpdateImgLabels(labels.Select(il => il.name));
        }
        catch
        {
            _lspCompletionAdapter.UpdateImgLabels([]);
        }
    }

    public void Load(string path)
    {
        using var stream = File.OpenRead(path);
        _editor.Load(stream);
        FileName = path;
        TryOpenLspDocument();
        UpdateImgLabels();
    }

    public void Save(string path)
    {
        using var stream = File.Create(path);
        _editor.Save(stream);
    }

    public void Clear()
    {
        _editor.Clear();
        if (_lspDocumentOpened)
        {
            _lspService?.DocumentManager.CloseDocument();
            _lspDocumentOpened = false;
        }
    }

    public void ScrollToLine(int line) => _editor.ScrollToLine(line);
    public void ScrollToHome() => _editor.ScrollToHome();
    public void Select(int offset, int length) => _editor.Select(offset, length);

    public IDocument Document => _editor.Document;
    public TextDocument TextDocument => _editor.Document;
    public AvaloniaEdit.Editing.TextArea TextArea => _editor.TextArea;

    public LspClientService? LspService => _lspService;

    private void ApplyThemeColors()
    {
        if (_isDarkTheme)
        {
            _editor.Background = new SolidColorBrush(
                Color.FromRgb(32, 33, 35));
            _editor.Foreground = new SolidColorBrush(
                Color.FromRgb(240, 240, 243));

            var current = _editor.SyntaxHighlighting;
            if (current != null && !current.Name.EndsWith("-Dark"))
            {
                var darkHighlighting = EcsHighlightingLoader.GetByName(current.Name + "-Dark");
                if (darkHighlighting != null)
                    _editor.SyntaxHighlighting = darkHighlighting;
            }
        }
        else
        {
            _editor.Background = new SolidColorBrush(
                Color.FromRgb(255, 255, 255));
            _editor.Foreground = new SolidColorBrush(
                Color.FromRgb(38, 37, 30));

            var current = _editor.SyntaxHighlighting;
            if (current != null && current.Name.EndsWith("-Dark"))
            {
                var lightName = current.Name[..^5];
                var lightHighlighting = EcsHighlightingLoader.GetByName(lightName);
                if (lightHighlighting != null)
                    _editor.SyntaxHighlighting = lightHighlighting;
            }
        }

    }

    public void Cleanup()
    {
        if (_disposed) return;
        _disposed = true;

        _lspChangeThrottle?.Stop();

        if (_lspService != null)
        {
            _lspService.Connected -= OnLspConnected;
            _lspService.ConnectionFailed -= OnLspConnectionFailed;
        }

        _lspHoverHandler?.Dispose();
        _lspDefinitionHandler?.Dispose();
        _completionController?.Dispose();
    }
}