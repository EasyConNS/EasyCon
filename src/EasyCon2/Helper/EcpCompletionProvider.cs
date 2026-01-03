using EasyCon.Script;
using EasyCon2.Models;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;

namespace EasyCon2.Helper;

public delegate IEnumerable<string> GetImgLabel();

internal class EcpCompletionProvider(TextEditor textEditor) : ICompletionProvider
{
    private readonly TextEditor Editor = textEditor;
    public GetImgLabel GetImgLabel;

    private readonly List<string> _keywords = [
        "IMPORT",
        "IF", "ELIF", "ELSE", "ENDIF",
        "FOR", "TO", "NEXT", "BREAK", "CONTINUE",
        "FUNC", "ENDFUNC", "CALL", "RETURN",
        "PRINT", "ALERT", "TIME", "WAIT",
        "A", "B", "X", "Y","L", "R","ZL", "ZR",
        "PLUS", "MINUS", "HOME", "CAPTURE",
        "LS", "RS", "LCLICK", "RCLICK",
        "LEFT", "RIGHT", "UP", "DOWN"
        ];
    public async Task<IEnumerable<ICompletionData>> GetCompletions(ITextSource textSource, int offset, string cur)
    {
        var completions = new List<ICompletionData>();

        if (cur.StartsWith('@'))
        {
            var ilnames = GetImgLabel?.Invoke() ?? [];
            foreach (var name in ilnames)
            {
                completions.Add(new EcpCompletionData($"@{name}"));
            }
        }
        else if (cur.StartsWith('_') || cur.StartsWith('$'))
        {
            var tok = Scripter.GetTokens(Editor.Text, cur);
            foreach (var name in tok)
            {
                completions.Add(new EcpCompletionData(name));
            }
        }
        else if (char.IsLetter(cur[0]))
        {
            var kw = _keywords.Where(ch => ch.StartsWith(cur, StringComparison.OrdinalIgnoreCase));
            foreach (var item in kw)
            {
                completions.Add(new EcpCompletionData(item));
            }
        }
        return completions;
    }

    private bool IsWordPart(char c)
    {
        return char.IsLetterOrDigit(c) || "@$_".IndexOf(c) != -1;
    }

    public string GetCurrentWord(TextDocument document, int offset)
    {
        int start = offset;
        while (start > 0 && IsWordPart(document.GetCharAt(start - 1)))
        {
            start--;
        }
        return document.GetText(start, offset - start);
    }

    public bool ShouldTriggerCompletion(char triggerChar, string currentLineText, int caretIndex) => true;
}
