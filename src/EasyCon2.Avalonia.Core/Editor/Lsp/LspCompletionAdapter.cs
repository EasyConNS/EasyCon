using AvaloniaEdit.CodeCompletion;
using AvTextDocument = AvaloniaEdit.Document.TextDocument;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace EasyCon2.Avalonia.Core.Editor.Lsp;

internal class LspCompletionAdapter : ICompletionProvider
{
    private readonly LspClientService _lspService;

    public LspCompletionAdapter(LspClientService lspService)
    {
        _lspService = lspService;
    }

    public async Task<IEnumerable<ICompletionData>> GetCompletions(
        AvaloniaEdit.Document.ITextSource textSource, int offset, string cur)
    {
        if (!_lspService.IsConnected || _lspService.DocumentManager.DocumentUri == null)
            return [];

        var parameters = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = _lspService.DocumentManager.DocumentUri
            },
            Position = OffsetToPosition(textSource, offset),
            Context = new CompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        };

        var result = await _lspService.RequestCompletion(parameters);
        if (result == null) return [];

        return result.Select(item => new LspCompletionData(item));
    }

    public bool ShouldTriggerCompletion(char triggerChar, string currentLineText, int caretIndex)
    {
        return char.IsLetterOrDigit(triggerChar) || "@$_".IndexOf(triggerChar) != -1;
    }

    public string GetCurrentWord(AvTextDocument document, int offset)
    {
        int start = offset;
        while (start > 0 && IsWordPart(document.GetCharAt(start - 1)))
            start--;
        return document.GetText(start, offset - start);
    }

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || "@$_".IndexOf(c) != -1;

    public static Position GetPosition(AvTextDocument document, int offset)
    {
        var loc = document.GetLocation(offset);
        return new Position(loc.Line - 1, loc.Column - 1);
    }

    private static Position OffsetToPosition(AvaloniaEdit.Document.ITextSource text, int offset)
    {
        int line = 0, col = 0;
        int count = text.TextLength;
        for (int i = 0; i < offset && i < count; i++)
        {
            if (text.GetCharAt(i) == '\n') { line++; col = 0; }
            else col++;
        }
        return new Position(line, col);
    }
}
