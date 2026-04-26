using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Search;
using EasyCon.Core;

namespace EasyCon2.Avalonia.Core.Editor;

public partial class ScriptEditorControl : UserControl
{
    private const double MinFontSize = 8;
    private const double MaxFontSize = 36;

    private readonly TextEditor _editor;
    private readonly SearchPanel _searchPanel;
    private CodeCompletionController _completionController;
    private EcpCompletionProvider _completionProvider;

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

    public bool EnableAutoCompletion
    {
        get => _completionController?.EnableAutoCompletion ?? false;
        set { if (_completionController != null) _completionController.EnableAutoCompletion = value; }
    }

    public string Text
    {
        get => _editor.Text;
        set => _editor.Text = value;
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
        _editor.TextChanged += (_, _) => EditorTextChanged?.Invoke(this, EventArgs.Empty);

        SetupDragDrop();
        _searchPanel = SearchPanel.Install(_editor);
        InitCompletion();

        _editor.AddHandler(PointerWheelChangedEvent, OnEditorPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    public void SetFontSize(double size)
    {
        _editor.FontSize = Math.Clamp(size, MinFontSize, MaxFontSize);
    }

    private void InitCompletion()
    {
        _completionProvider = new EcpCompletionProvider(_editor);
        _completionController = new CodeCompletionController(_editor, _completionProvider);
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

    public void SetImgLabelProvider(Func<IEnumerable<string>> provider)
    {
        _completionProvider.GetImgLabel = provider;
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
            if (Scripter.CanComment(doc.GetText(line)))
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
                text = Scripter.ToggleComment(text, docomment);
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

    public void Load(string path)
    {
        using var stream = File.OpenRead(path);
        _editor.Load(stream);
        FileName = path;
    }
    public void Save(string path)
    {
        using var stream = File.Create(path);
        _editor.Save(stream);
    }
    public void Clear() => _editor.Clear();
    public void ScrollToLine(int line) => _editor.ScrollToLine(line);
    public void ScrollToHome() => _editor.ScrollToHome();
    public void Select(int offset, int length) => _editor.Select(offset, length);

    public IDocument Document => _editor.Document;
    public TextDocument TextDocument => _editor.Document;
    public AvaloniaEdit.Editing.TextArea TextArea => _editor.TextArea;

    private void ApplyThemeColors()
    {
        if (_isDarkTheme)
        {
            _editor.Background = new SolidColorBrush(
                Color.FromRgb(20, 20, 19));   // #141413
            _editor.Foreground = new SolidColorBrush(
                Color.FromRgb(250, 249, 245)); // #faf9f5

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
                Color.FromRgb(250, 250, 247)); // #FAFAF7
            _editor.Foreground = new SolidColorBrush(
                Color.FromRgb(38, 37, 30));    // #26251E

            var current = _editor.SyntaxHighlighting;
            if (current != null && current.Name.EndsWith("-Dark"))
            {
                var lightName = current.Name[..^5]; // strip "-Dark"
                var lightHighlighting = EcsHighlightingLoader.GetByName(lightName);
                if (lightHighlighting != null)
                    _editor.SyntaxHighlighting = lightHighlighting;
            }
        }
    }
}