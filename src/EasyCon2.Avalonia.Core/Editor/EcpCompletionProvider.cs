using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using EasyCon.Core;

namespace EasyCon2.Avalonia.Core.Editor;

public interface ICompletionProvider
{
    Task<IEnumerable<ICompletionData>> GetCompletions(ITextSource textSource, int offset, string cur);
    bool ShouldTriggerCompletion(char triggerChar, string currentLineText, int caretIndex);
    string GetCurrentWord(TextDocument document, int offset);
}

internal class EcpCompletionProvider(TextEditor textEditor) : ICompletionProvider
{
    private readonly TextEditor Editor = textEditor;
    public Func<IEnumerable<string>> GetImgLabel;

    private readonly List<string> _keywords =
    [
        "IMPORT",
        "IF", "ELIF", "ELSE", "ENDIF",
        "FOR", "TO", "NEXT", "BREAK", "CONTINUE",
        "WHILE", "END",
        "FUNC", "ENDFUNC", "CALL", "RETURN",
        "PRINT", "ALERT", "TIME", "WAIT",
        "A", "B", "X", "Y", "L", "R", "ZL", "ZR",
        "PLUS", "MINUS", "HOME", "CAPTURE",
        "LS", "RS", "LCLICK", "RCLICK",
        "LEFT", "RIGHT", "UP", "DOWN",
        "UPLEFT", "UPRIGHT", "DOWNLEFT", "DOWNRIGHT",
        "RAND", "AMIIBO", "BEEP",
    ];

    public Task<IEnumerable<ICompletionData>> GetCompletions(ITextSource textSource, int offset, string cur)
    {
        var completions = new List<ICompletionData>();

        if (cur.StartsWith('@'))
        {
            var ilnames = GetImgLabel?.Invoke() ?? [];
            foreach (var name in ilnames)
                completions.Add(new EcpCompletionData($"@{name}"));
        }
        else if (cur.StartsWith('_') || cur.StartsWith('$'))
        {
            var tok = Scripter.GetTokens(Editor.Text, cur);
            foreach (var name in tok)
                completions.Add(new EcpCompletionData(name));
        }
        else if (cur.Length > 0 && char.IsLetter(cur[0]))
        {
            var kw = _keywords.Where(ch => ch.StartsWith(cur, StringComparison.OrdinalIgnoreCase));
            foreach (var item in kw)
                completions.Add(new EcpCompletionData(item));
        }

        return Task.FromResult<IEnumerable<ICompletionData>>(completions);
    }

    public string GetCurrentWord(TextDocument document, int offset)
    {
        int start = offset;
        while (start > 0 && IsWordPart(document.GetCharAt(start - 1)))
            start--;
        return document.GetText(start, offset - start);
    }

    public bool ShouldTriggerCompletion(char triggerChar, string currentLineText, int caretIndex) => true;

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || "@$_".IndexOf(c) != -1;
}