using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Search;

namespace EasyCon2.Avalonia.Core.Editor;

public partial class ScriptEditorControl : UserControl
{
    private readonly TextEditor _editor;
    private readonly SearchPanel _searchPanel;
    private CodeCompletionController _completionController;
    private EcpCompletionProvider _completionProvider;

    public event EventHandler? EditorTextChanged;
    public event Action<string>? FileDropped;

    public TextEditor TextEditor => _editor;

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
        set => _editor.SyntaxHighlighting = value;
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
    }

    private void InitCompletion()
    {
        _completionProvider = new EcpCompletionProvider(_editor);
        _completionController = new CodeCompletionController(_editor, _completionProvider);
    }

    public void SetImgLabelProvider(Func<IEnumerable<string>> provider)
    {
        _completionProvider.GetImgLabel = provider;
    }

    public void OpenSearchPanel() => _searchPanel.Open();

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
}