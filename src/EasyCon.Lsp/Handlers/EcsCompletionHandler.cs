using EasyCon.Lsp.Analysis;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Client.ClientCapabilities;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Server;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Server.Options;
using EmmyLua.LanguageServer.Framework.Protocol.Message.Completion;
using EmmyLua.LanguageServer.Framework.Server.Handler;

namespace EasyCon.Lsp.Handlers;

internal sealed class EcsCompletionHandler(DocumentManager docManager) : CompletionHandlerBase
{
    protected override Task<CompletionResponse?> Handle(CompletionParams request, CancellationToken token)
    {
        var uri = request.TextDocument.Uri;
        var root = docManager.GetRoot(uri);
        var list = CompletionProvider.GetCompletions(root);
        return Task.FromResult<CompletionResponse?>(new CompletionResponse(list));
    }

    protected override Task<CompletionItem> Resolve(CompletionItem item, CancellationToken token)
    {
        return Task.FromResult(item);
    }

    public override void RegisterCapability(ServerCapabilities serverCapabilities, ClientCapabilities clientCapabilities)
    {
        serverCapabilities.CompletionProvider = new CompletionOptions
        {
            TriggerCharacters = ["$", "_", "@", "("],
        };
    }
}