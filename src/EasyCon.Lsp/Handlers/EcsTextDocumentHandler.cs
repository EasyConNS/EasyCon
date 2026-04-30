using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Client.ClientCapabilities;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Server;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Server.Options;
using EmmyLua.LanguageServer.Framework.Protocol.Message.Client.PublishDiagnostics;
using EmmyLua.LanguageServer.Framework.Protocol.Message.TextDocument;
using EmmyLua.LanguageServer.Framework.Protocol.Model;
using EmmyLua.LanguageServer.Framework.Protocol.Model.Kind;
using EmmyLua.LanguageServer.Framework.Protocol.Model.TextEdit;
using EmmyLua.LanguageServer.Framework.Server;
using EmmyLua.LanguageServer.Framework.Server.Handler;

namespace EasyCon.Lsp.Handlers;

internal sealed class EcsTextDocumentHandler(DocumentManager docManager, LanguageServer server) : TextDocumentHandlerBase
{
    protected override async Task Handle(DidOpenTextDocumentParams request, CancellationToken token)
    {
        var uri = request.TextDocument.Uri;
        var text = request.TextDocument.Text;
        docManager.OpenDocument(uri, text);
        await PublishDiagnostics(uri).ConfigureAwait(false);
    }

    protected override async Task Handle(DidChangeTextDocumentParams request, CancellationToken token)
    {
        var uri = request.TextDocument.Uri;
        if (request.ContentChanges.Count > 0)
        {
            var text = request.ContentChanges[0].Text;
            docManager.UpdateDocument(uri, text);
            await PublishDiagnostics(uri).ConfigureAwait(false);
        }
    }

    protected override Task Handle(DidCloseTextDocumentParams request, CancellationToken token)
    {
        docManager.CloseDocument(request.TextDocument.Uri);
        return Task.CompletedTask;
    }

    protected override Task Handle(WillSaveTextDocumentParams request, CancellationToken token) => Task.CompletedTask;

    protected override Task<List<TextEdit>?> HandleRequest(WillSaveTextDocumentParams request, CancellationToken token)
        => Task.FromResult<List<TextEdit>?>(null);

    public override void RegisterCapability(ServerCapabilities serverCapabilities, ClientCapabilities clientCapabilities)
    {
        serverCapabilities.TextDocumentSync = new TextDocumentSyncOptions
        {
            OpenClose = true,
            Change = TextDocumentSyncKind.Full,
        };
    }

    private async Task PublishDiagnostics(DocumentUri uri)
    {
        var tree = docManager.GetSyntaxTree(uri);
        if (tree == null) return;

        var diagnostics = new List<EmmyLua.LanguageServer.Framework.Protocol.Model.Diagnostic.Diagnostic>();
        foreach (var diag in tree.Diagnostics)
        {
            if (!diag.IsError) continue;
            var line = diag.Location.StartLine;
            var lineLength = 0;
            if (line >= 0 && line < tree.Text.Lines.Length)
                lineLength = tree.Text.Lines[line].Length;

            diagnostics.Add(new()
            {
                Range = new(new(line, 0), new(line, lineLength)),
                Severity = EmmyLua.LanguageServer.Framework.Protocol.Model.Diagnostic.DiagnosticSeverity.Error,
                Message = diag.Message,
                Source = "ecs-lsp",
            });
        }

        try
        {
            await server.Client.PublishDiagnostics(new()
            {
                Uri = uri,
                Diagnostics = diagnostics,
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ecs-lsp] Failed to publish diagnostics: {ex.Message}");
        }
    }
}