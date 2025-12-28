using EasyCon2.Models;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using System.Diagnostics;
using System.Windows.Input;

namespace EasyCon2.Helper;

public interface ICompletionProvider
{
    /// <summary>
    /// 获取代码提示列表
    /// </summary>
    Task<IEnumerable<ICompletionData>> GetCompletions(ITextSource textSource, int offset, string cur);

    /// <summary>
    /// 处理字符输入，决定是否触发代码提示
    /// </summary>
    bool ShouldTriggerCompletion(char triggerChar, string currentLineText, int caretIndex);

    /// <summary>
    /// 获取当前单词（用于过滤提示列表）
    /// </summary>
    string GetCurrentWord(TextDocument document, int offset);
}

public class CodeCompletionController : IDisposable
{
    private readonly TextEditor _editor;
    private readonly ICompletionProvider _completionProvider;
    private CompletionWindow _completionWindow;
    private bool _isDisposed;

    public CodeCompletionController(TextEditor editor, ICompletionProvider completionProvider)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _completionProvider = completionProvider;

        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        // 文本输入事件
        _editor.TextArea.TextEntering += OnTextEntering;
        _editor.TextArea.TextEntered += OnTextEntered;

        // 键盘事件
        //_editor.TextArea.KeyDown += OnKeyDown;

        // 失去焦点时关闭提示窗口
        _editor.LostFocus += (s, e) => CloseCompletionWindow();
    }

    private async void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        var line = _editor.TextArea.Document.GetLineByNumber(_editor.TextArea.Caret.Line);
        if (_completionProvider.ShouldTriggerCompletion(e.Text[0],
            _editor.TextArea.Document.GetText(line.Offset, line.Length),
            _editor.TextArea.Caret.Column))
        {
            await ShowCompletionWindow();
        }
    }

    private void OnTextEntering(object sender, TextCompositionEventArgs e)
    {
        if (_completionWindow != null && Keyboard.Modifiers == ModifierKeys.None)
        {
            // 如果用户输入空格、换行等，关闭提示窗口
            if (e.Text == " " || e.Text == "\t" || e.Text == "\n" || e.Text == "\r")
            {
                CloseCompletionWindow();
            }
        }
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_completionWindow != null)
        {
            // Ctrl+Space 强制显示提示
            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                ShowCompletionWindow();
            }
            // ESC 关闭提示窗口
            else if (e.Key == Key.Escape)
            {
                CloseCompletionWindow();
                e.Handled = true;
            }
        }
        else
        {
            // Ctrl+Space 强制显示提示
            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                ShowCompletionWindow();
            }
        }
    }

    private async Task ShowCompletionWindow()
    {
        // 关闭已存在的窗口
        CloseCompletionWindow();

        try
        {
            // 获取当前单词用于过滤
            var currentWord = _completionProvider.GetCurrentWord(
                _editor.Document,
                _editor.TextArea.Caret.Offset
            );

            // 获取提示列表
            var completions = await _completionProvider.GetCompletions(
                _editor.Document,
                _editor.TextArea.Caret.Offset,
                currentWord);

            if (!completions.Any()) return;

            // 创建提示窗口
            _completionWindow = new CompletionWindow(_editor.TextArea)
            {
                CloseWhenCaretAtBeginning = true,
                CloseAutomatically = true,
            };

            // 设置数据源
            var data = _completionWindow.CompletionList.CompletionData;
            foreach (var c in completions)
            {
                data.Add(new EcpCompletionData(c.Text));
            }

            // 如果当前有单词，设置起始偏移量
            if (!string.IsNullOrEmpty(currentWord))
            {
                _completionWindow.StartOffset = _editor.TextArea.Caret.Offset - currentWord.Length;
            }

            // 设置事件处理
            _completionWindow.Closed += (s, e) => _completionWindow = null;

            // 显示窗口
            _completionWindow.Show();

            // 设置初始选择
            if (!string.IsNullOrEmpty(currentWord))
            {
                _completionWindow.CompletionList.SelectItem(currentWord);
            }
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