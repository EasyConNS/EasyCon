namespace EasyCon.Lsp.Analysis;

internal static class SourceHelper
{
    public static string ExtractWord(string lineText, int character)
    {
        if (character < 0 || character > lineText.Length) return "";
        var start = character;
        if (start == lineText.Length) start--;
        while (start > 0 && IsWordChar(lineText[start - 1])) start--;
        var end = start;
        while (end < lineText.Length && IsWordChar(lineText[end])) end++;
        if (start > 0 && (lineText[start - 1] == '$' || lineText[start - 1] == '_' || lineText[start - 1] == '@'))
            start--;
        return lineText[start..end];
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}