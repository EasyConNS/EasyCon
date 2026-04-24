using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

public sealed class SyntaxTree
{
    private delegate void ParseHandler(SyntaxTree syntaxTree,
                                           out CompicationUnit root,
                                           out ImmutableArray<Diagnostic> diagnostics);
    #region 兼容性解析标记 // 默认不开启
    public static bool LegacyCompat = false;
    #endregion

    public SourceText Text { get; init; }
    public bool IsLib { get; init; }

    public ImmutableArray<Diagnostic> Diagnostics { get; init; }

    internal CompicationUnit Root { get; init; }

    private SyntaxTree(SourceText text, bool isLib, ParseHandler handler)
    {
        Text = text;
        IsLib = isLib;

        handler(this, out var root, out var diagnostics);

        Diagnostics = diagnostics;
        Root = root;
    }

    public static SyntaxTree Load(string fileName, bool isLib = false)
    {
        var text = File.ReadAllText(fileName);
        var sourceText = SourceText.From(text, fileName);
        return Parse(sourceText, isLib);
    }

    private static void Parse(SyntaxTree syntaxTree, out CompicationUnit root, out ImmutableArray<Diagnostic> diagnostics)
    {
        var parser = new Parser(syntaxTree);
        root = parser.ParseProgram();
        diagnostics = [.. parser.Diagnostics];
    }

    public static SyntaxTree Parse(string text, bool isLib = false)
    {
        var sourceText = SourceText.From(text);
        return Parse(sourceText, isLib);
    }

    public static SyntaxTree Parse(SourceText text, bool isLib = false)
    {
        return new SyntaxTree(text, isLib, Parse);
    }

    public static ImmutableArray<Token> ParseTokens(string text)
    {
        var sourceText = SourceText.From(text);
        return ParseTokens(sourceText, out _);
    }

    private static ImmutableArray<Token> ParseTokens(SourceText text, out ImmutableArray<Diagnostic> diagnostics)
    {
        var tokens = new ImmutableArray<Token>();

        void ParseTokensHandler(SyntaxTree st, out CompicationUnit _, out ImmutableArray<Diagnostic> d)
        {
            _ = new([]);
            d = default;
            var l = new Lexer(st);
            tokens = l.Tokenize();
            d = [.. l.Diagnostics];
        }

        var syntaxTree = new SyntaxTree(text, false, ParseTokensHandler);
        diagnostics = syntaxTree.Diagnostics;
        return tokens;
    }
}