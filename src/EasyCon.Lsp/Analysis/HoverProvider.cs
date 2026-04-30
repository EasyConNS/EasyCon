using EasyCon.Script.Syntax;
using EmmyLua.LanguageServer.Framework.Protocol.Message.Hover;
using EmmyLua.LanguageServer.Framework.Protocol.Model;
using EmmyLua.LanguageServer.Framework.Protocol.Model.Markup;

namespace EasyCon.Lsp.Analysis;

internal static class HoverProvider
{
    public static HoverResponse? GetHover(CompicationUnit? root, string? lineText, Position position)
    {
        if (lineText == null || root == null) return null;

        var word = SourceHelper.ExtractWord(lineText, position.Character);
        if (string.IsNullOrEmpty(word)) return null;

        // Check builtins
        foreach (var (name, sig, doc) in Constants.BuiltinFunctions)
        {
            if (string.Equals(word, name, StringComparison.OrdinalIgnoreCase))
            {
                return new()
                {
                    Contents = new() { Kind = MarkupKind.Markdown, Value = $"**{sig}**\n\n{doc}" },
                };
            }
        }

        // Check keywords
        if (Constants.Keywords.Contains(word.ToUpper()))
        {
            return new()
            {
                Contents = new() { Kind = MarkupKind.PlainText, Value = $"关键字: {word}" },
            };
        }

        // Check AST symbols
        foreach (var sym in SymbolCollector.CollectSymbols(root))
        {
            if (string.Equals(word, sym.Name, sym.Kind is "constant" or "parameter" or "variable" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            {
                return new()
                {
                    Contents = new()
                    {
                        Kind = MarkupKind.Markdown,
                        Value = $"**{sym.Kind}** `{sym.Name}` — 第 {sym.Line} 行",
                    },
                };
            }
        }

        return null;
    }
}