using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace EasyCon2.Avalonia.Core.Editor.Lsp;

public class LspDocumentManager
{
    private readonly LspClientService _client;
    private int _version;

    public string? DocumentUri { get; private set; }
    public string? LanguageId { get; set; } = "ecp";

    public LspDocumentManager(LspClientService client)
    {
        _client = client;
    }

    public void OpenDocument(string filePath, string text)
    {
        DocumentUri = new Uri(filePath).AbsoluteUri;
        _version = 1;

        _client.SendDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = DocumentUri,
                LanguageId = LanguageId ?? "ecp",
                Version = _version,
                Text = text
            }
        });
    }

    public void UpdateDocument(string text)
    {
        if (DocumentUri == null) return;
        _version++;

        _client.SendDidChange(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
            {
                Uri = DocumentUri,
                Version = _version
            },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = text }
            )
        });
    }

    public void CloseDocument()
    {
        if (DocumentUri == null) return;

        _client.SendDidClose(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri }
        });

        DocumentUri = null;
        _version = 0;
    }
}
