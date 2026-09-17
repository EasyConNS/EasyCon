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
        var symbols = SymbolCollector.CollectSymbols(root);
        foreach (var sym in symbols)
        {
            if (string.Equals(word, sym.Name, sym.Kind is "constant" or "parameter" or "variable" or "field" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            {
                // 对于结构体，显示更详细的字段信息
                if (sym.Kind == "struct")
                {
                    var fields = symbols.Where(s => s.Kind == "field").ToList();
                    var fieldList = fields.Count > 0
                        ? "\n\n**字段:**\n" + string.Join("\n", fields.Select(f => $"- `{f.Name}`"))
                        : "";
                    return new()
                    {
                        Contents = new()
                        {
                            Kind = MarkupKind.Markdown,
                            Value = $"**struct** `{sym.Name}` — 第 {sym.Line} 行{fieldList}",
                        },
                    };
                }

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