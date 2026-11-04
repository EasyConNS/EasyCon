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

        // 提取光标前的当前词前缀，服务端按前缀过滤
        string? prefix = null;
        var lineText = docManager.GetLineText(uri, request.Position.Line);
        if (lineText != null && request.Position.Character >= 0 && request.Position.Character <= lineText.Length)
        {
            var before = lineText[..request.Position.Character];
            var start = before.Length;
            while (start > 0 && (char.IsLetterOrDigit(before[start - 1]) || before[start - 1] == '_'))
                start--;
            prefix = before[start..];
        }

        var list = CompletionProvider.GetCompletions(root, string.IsNullOrEmpty(prefix) ? null : prefix);
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
            // 不声明 "("（无 signatureHelp 支撑）；$/_/@ 仅为编辑器侧触发便利
            TriggerCharacters = ["$", "_", "@"],
        };
    }
}