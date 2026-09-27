using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;

namespace EasyCon2.Avalonia.Core.Editor;

public interface ICompletionProvider
{
    Task<IEnumerable<ICompletionData>> GetCompletionsAsync(TextDocument document, int offset, string cur);
    bool ShouldTriggerCompletion(char triggerChar, string currentLineText, int caretIndex);
    string GetCurrentWord(TextDocument document, int offset);
}