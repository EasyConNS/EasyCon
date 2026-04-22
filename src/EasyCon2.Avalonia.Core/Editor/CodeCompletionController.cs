using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using System.Diagnostics;

namespace EasyCon2.Avalonia.Core.Editor;

internal class CodeCompletionController : IDisposable
{
    private readonly TextEditor _editor;
    private readonly ICompletionProvider _completionProvider;
    private bool _enableAutoCompletion;
    private CompletionWindow _completionWindow;
    private bool _isDisposed;

    public bool EnableAutoCompletion
    {
        get => _enableAutoCompletion;
        set => _enableAutoCompletion = value;
    }

    public CodeCompletionController(TextEditor editor, ICompletionProvider completionProvider, bool enableAutoCompletion = true)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _completionProvider = completionProvider;
        _enableAutoCompletion = enableAutoCompletion;

        _editor.TextArea.TextEntering += OnTextEntering;
        _editor.TextArea.TextEntered += OnTextEntered;
        _editor.TextArea.KeyDown += OnKeyDown;
        _editor.LostFocus += (_, _) => CloseCompletionWindow();
    }

    private async void OnTextEntered(object sender, TextInputEventArgs e)
    {
        if (!_enableAutoCompletion || string.IsNullOrEmpty(e.Text)) return;

        var line = _editor.TextArea.Document.GetLineByNumber(_editor.TextArea.Caret.Line);
        if (_completionProvider.ShouldTriggerCompletion(e.Text[0],
            _editor.TextArea.Document.GetText(line.Offset, line.Length),
            _editor.TextArea.Caret.Column))
        {
            await ShowCompletionWindow();
        }
    }

    private void OnTextEntering(object sender, TextInputEventArgs e)
    {
        if (_completionWindow != null && e.Text is " " or "\t" or "\n" or "\r")
            CloseCompletionWindow();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = ShowCompletionWindow();
        }
        else if (e.Key == Key.Escape && _completionWindow != null)
        {
            CloseCompletionWindow();
            e.Handled = true;
        }
    }

    private async Task ShowCompletionWindow()
    {
        CloseCompletionWindow();

        try
        {
            var currentWord = _completionProvider.GetCurrentWord(
                _editor.Document,
                _editor.TextArea.Caret.Offset
            );

            var completions = await _completionProvider.GetCompletions(
                _editor.Document,
                _editor.TextArea.Caret.Offset,
                currentWord);

            if (!completions.Any()) return;

            _completionWindow = new CompletionWindow(_editor.TextArea)
            {
                CloseWhenCaretAtBeginning = true,
                CloseAutomatically = true,
            };

            var data = _completionWindow.CompletionList.CompletionData;
            foreach (var c in completions)
                data.Add(c);

            if (!string.IsNullOrEmpty(currentWord))
                _completionWindow.StartOffset = _editor.TextArea.Caret.Offset - currentWord.Length;

            _completionWindow.Closed += (_, _) => _completionWindow = null;
            _completionWindow.Show();

            if (!string.IsNullOrEmpty(currentWord))
                _completionWindow.CompletionList.SelectItem(currentWord);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"显示代码提示时出错: {ex.Message}");
            CloseCompletionWindow();
        }
    }

    public void CloseCompletionWindow()
    {
        _completionWindow?.Close();
        _completionWindow = null;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            CloseCompletionWindow();
            _isDisposed = true;
        }
    }
}