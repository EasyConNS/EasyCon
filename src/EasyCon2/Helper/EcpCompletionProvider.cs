using EasyCon2.Models;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;

namespace EasyCon2.Helper;

public delegate IEnumerable<string> GetImgLabel();

internal class EcpCompletionProvider : ICompletionProvider
{
    public GetImgLabel GetImgLabel;

    private readonly List<string> _keywords = [
        "IF", "ELIF", "ELSE", "ENDIF",
        "FOR", "TO", "NEXT", "BREAK", "CONTINUE",
        "FUNC", "ENDFUNC", "CALL", "RETURN",
        "PRINT", "ALERT", "TIME", "WAIT",
        "PLUS", "MINUS", "HOME", "CAPTURE",
        "LCLICK", "RCLICK",
        "LEFT", "RIGHT", "UP", "DOWN"
        ];
    public async Task<IEnumerable<ICompletionData>> GetCompletions(ITextSource textSource, int offset, string cur)
    {
        var completions = new List<ICompletionData>();

        if(cur.StartsWith('@'))
        {
            var ilnames = GetImgLabel?.Invoke()?? [];
            foreach(var name in ilnames)
            {
                completions.Add(new EcpCompletionData($"@{name}"));
            }
        }else if (char.IsLetter(cur[0]))
        {
            foreach (var item in _keywords.Where(ch => ch.StartsWith(cur, StringComparison.OrdinalIgnoreCase)))
            {
                completions.Add(new EcpCompletionData(item));
            }
        }
        return completions;
    }

    private bool IsWordPart(char c)
    {
        return char.IsLetterOrDigit(c) || c == '@' || c == '.';
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
