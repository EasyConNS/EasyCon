using AvaloniaEdit.CodeCompletion;
using EasyCon2.Avalonia.Core.Editor;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using AvTextDocument = AvaloniaEdit.Document.TextDocument;

namespace EasyCon2.Avalonia.Core.Editor.Lsp;

internal class LspCompletionAdapter : ICompletionProvider
{
    private readonly LspClientService _lspService;
    private List<string> _imgLabels = [];

    public LspCompletionAdapter(LspClientService lspService)
    {
        _lspService = lspService;
    }

    public void UpdateImgLabels(IEnumerable<string> labels)
    {
        _imgLabels = [.. labels];
    }

    public async Task<IEnumerable<ICompletionData>> GetCompletionsAsync(
        AvTextDocument document, int offset, string cur)
    {
        if (cur.StartsWith('@'))
        {
            var prefix = cur[1..];
            return _imgLabels
                .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(name => new EcpCompletionData($"@{name}"));
        }

        if (!_lspService.IsConnected || _lspService.DocumentManager.DocumentUri == null)
            return [];

        var parameters = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = _lspService.DocumentManager.DocumentUri
            },
            Position = GetPosition(document, offset),
            Context = new CompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        };

        var result = await _lspService.RequestCompletionAsync(parameters);
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
}