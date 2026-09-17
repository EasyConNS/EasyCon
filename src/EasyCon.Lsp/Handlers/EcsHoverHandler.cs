using EasyCon.Lsp.Analysis;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Client.ClientCapabilities;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Server;
using EmmyLua.LanguageServer.Framework.Protocol.Message.Hover;
using EmmyLua.LanguageServer.Framework.Server.Handler;

namespace EasyCon.Lsp.Handlers;

internal sealed class EcsHoverHandler(DocumentManager docManager) : HoverHandlerBase
{
    protected override Task<HoverResponse?> Handle(HoverParams request, CancellationToken token)
    {
        var uri = request.TextDocument.Uri;
        var root = docManager.GetRoot(uri);
        var lineText = docManager.GetLineText(uri, request.Position.Line);
        var result = HoverProvider.GetHover(root, lineText, request.Position);
        return Task.FromResult(result);
    }

    public override void RegisterCapability(ServerCapabilities serverCapabilities, ClientCapabilities clientCapabilities)
    {
        serverCapabilities.HoverProvider = true;
    }
}