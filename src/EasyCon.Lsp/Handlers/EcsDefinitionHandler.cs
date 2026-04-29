using EasyCon.Lsp.Analysis;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Client.ClientCapabilities;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Server;
using EmmyLua.LanguageServer.Framework.Protocol.Message.Definition;
using EmmyLua.LanguageServer.Framework.Protocol.Model;
using EmmyLua.LanguageServer.Framework.Server.Handler;

namespace EasyCon.Lsp.Handlers;

internal sealed class EcsDefinitionHandler(DocumentManager docManager) : DefinitionHandlerBase
{
    protected override Task<DefinitionResponse?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri;
        var root = docManager.GetRoot(uri);
        var lineText = docManager.GetLineText(uri, request.Position.Line);
        var range = DefinitionFinder.FindDefinition(root, lineText, request.Position);

        if (range != null)
        {
            var loc = new Location(uri, range.Value);
            return Task.FromResult<DefinitionResponse?>(new DefinitionResponse([loc]));
        }

        return Task.FromResult<DefinitionResponse?>(null);
    }

    public override void RegisterCapability(ServerCapabilities serverCapabilities, ClientCapabilities clientCapabilities)
    {
        serverCapabilities.DefinitionProvider = true;
    }
}