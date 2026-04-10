using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

public sealed class SyntaxTree
{
    private delegate void ParseHandler(SyntaxTree syntaxTree,
                                           out CompicationUnit root,
                                           out ImmutableArray<CompicationUnit> flattenroot,
                                           out ImmutableArray<Diagnostic> diagnostics);
    #region 兼容性解析标记 // 默认不开启
    public static bool LegacyCompat = true;
    #endregion

    public SourceText Text { get; init; }

    public ImmutableArray<Diagnostic> Diagnostics { get; init; }

    internal CompicationUnit Root { get; init; }
    internal ImmutableArray<CompicationUnit> FlattenRoot { get; init; }

    private SyntaxTree(SourceText text, ParseHandler handler)
    {
        Text = text;

        handler(this, out var root, out var froot, out var diagnostics);

        Diagnostics = diagnostics;
        Root = root;
        FlattenRoot = froot;
    }

    public static SyntaxTree Load(string fileName)
    {
        var text = File.ReadAllText(fileName);
        var sourceText = SourceText.From(text, fileName);
        return Parse(sourceText);
    }

    private static void Parse(SyntaxTree syntaxTree, out CompicationUnit root, out ImmutableArray<CompicationUnit> fullSyntax, out ImmutableArray<Diagnostic> diagnostics)
    {
        var parser = new Parser(syntaxTree);
        root = parser.ParseProgram();
        fullSyntax = parser.Flatten(root);
        diagnostics = [.. parser.Diagnostics];
    }

    public static SyntaxTree Parse(string text)
    {
        var sourceText = SourceText.From(text);
        return Parse(sourceText);
    }

    public static SyntaxTree Parse(SourceText text)
    {
        return new SyntaxTree(text, Parse);
    }

    public static ImmutableArray<Token> ParseTokens(string text)
    {
        var sourceText = SourceText.From(text);
        return ParseTokens(sourceText, out _);
    }

    private static ImmutableArray<Token> ParseTokens(SourceText text, out ImmutableArray<Diagnostic> diagnostics)
    {
        var tokens = new ImmutableArray<Token>();

        void ParseTokens(SyntaxTree st, out CompicationUnit _, out ImmutableArray<CompicationUnit> __, out ImmutableArray<Diagnostic> d)
        {
            _ = null;
            __ = default;
            d = default;
            var l = new Lexer(st);
            tokens = l.Tokenize();
            d = [.. l.Diagnostics];
        }

        var syntaxTree = new SyntaxTree(text, ParseTokens);
        diagnostics = syntaxTree.Diagnostics;
        return tokens;
    }
}
