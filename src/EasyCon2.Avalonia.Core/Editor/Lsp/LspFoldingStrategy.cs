using AvaloniaEdit.Folding;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace EasyCon2.Avalonia.Core.Editor.Lsp;

public class LspFoldingStrategy
{
    private readonly LspClientService _lspService;

    public LspFoldingStrategy(LspClientService lspService)
    {
        _lspService = lspService;
    }

    public async Task UpdateFoldingsAsync(FoldingManager manager, AvaloniaEdit.Document.TextDocument document)
    {
        var foldings = await GetFoldingsAsync(document);
        manager.UpdateFoldings(foldings, -1);
    }

    private async Task<IEnumerable<NewFolding>> GetFoldingsAsync(AvaloniaEdit.Document.TextDocument document)
    {
        if (!_lspService.IsConnected || _lspService.DocumentManager.DocumentUri == null)
            return [];

        var result = await _lspService.RequestDocumentSymbolAsync(new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = _lspService.DocumentManager.DocumentUri
            }
        });

        if (result == null) return [];

        var foldings = new List<NewFolding>();
        foreach (var item in result)
        {
            if (item.IsDocumentSymbol)
                CollectFoldings(item.DocumentSymbol, document, foldings);
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }

    private void CollectFoldings(DocumentSymbol symbol, AvaloniaEdit.Document.TextDocument document, List<NewFolding> foldings)
    {
        var range = symbol.Range;
        if (range != null)
        {
            var startLine = document.GetLineByNumber((int)range.Start.Line + 1);
            var endLine = document.GetLineByNumber((int)range.End.Line + 1);

            if (startLine.LineNumber < endLine.LineNumber)
            {
                foldings.Add(new NewFolding
                {
                    StartOffset = startLine.Offset,
                    EndOffset = endLine.EndOffset,
                    Name = symbol.Name,
                    DefaultClosed = false
                });
            }
        }

        if (symbol.Children != null)
        {
            foreach (var child in symbol.Children)
                CollectFoldings(child, document, foldings);
        }
    }
}