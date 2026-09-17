using EasyCon.Script.Syntax;
using EmmyLua.LanguageServer.Framework.Protocol.Message.Completion;
using EmmyLua.LanguageServer.Framework.Protocol.Model;
using EmmyLua.LanguageServer.Framework.Protocol.Model.Kind;

namespace EasyCon.Lsp.Analysis;

internal static class CompletionProvider
{
    public static CompletionList GetCompletions(CompicationUnit? root)
    {
        var items = new List<CompletionItem>();

        // Keywords
        foreach (var kw in Constants.Keywords)
        {
            items.Add(new()
            {
                Label = kw,
                Kind = CompletionItemKind.Keyword,
                Detail = "关键字",
            });
        }

        // Builtin functions
        foreach (var (name, sig, doc) in Constants.BuiltinFunctions)
        {
            items.Add(new()
            {
                Label = name,
                Kind = CompletionItemKind.Function,
                Detail = sig,
                Documentation = doc,
            });
        }

        // FFI types
        foreach (var t in Constants.FfiTypes)
        {
            items.Add(new()
            {
                Label = t,
                Kind = CompletionItemKind.TypeParameter,
                Detail = "类型",
            });
        }

        // Gamepad keys
        foreach (var key in Constants.GamepadKeys)
        {
            items.Add(new()
            {
                Label = key,
                Kind = CompletionItemKind.EnumMember,
                Detail = "按键",
            });
        }

        // Stick keys
        foreach (var sk in Constants.StickKeys)
        {
            items.Add(new()
            {
                Label = sk,
                Kind = CompletionItemKind.EnumMember,
                Detail = "摇杆",
            });
        }

        // Directions
        foreach (var dir in Constants.Directions)
        {
            items.Add(new()
            {
                Label = dir,
                Kind = CompletionItemKind.EnumMember,
                Detail = "方向",
            });
        }

        // Key mods
        foreach (var mod in Constants.KeyMods)
        {
            items.Add(new()
            {
                Label = mod,
                Kind = CompletionItemKind.EnumMember,
                Detail = "按键状态",
            });
        }

        // AST symbols
        if (root != null)
        {
            foreach (var sym in SymbolCollector.CollectSymbols(root))
            {
                items.Add(new()
                {
                    Label = sym.Name,
                    Kind = sym.Kind switch
                    {
                        "constant" => CompletionItemKind.Constant,
                        "function" => CompletionItemKind.Function,
                        "parameter" => CompletionItemKind.Variable,
                        "struct" => CompletionItemKind.Struct,
                        "field" => CompletionItemKind.Field,
                        _ => CompletionItemKind.Variable,
                    },
                    Detail = sym.Kind,
                });
            }
        }

        return new() { IsIncomplete = false, Items = items };
    }
}