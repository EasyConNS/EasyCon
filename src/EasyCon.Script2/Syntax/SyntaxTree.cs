using EC.Script.Text;
using EC.Script.Ast;
using System.Collections.Immutable;

namespace EC.Script.Syntax;

public sealed class SyntaxTree
{
    private delegate void ParseHandler(SyntaxTree syntaxTree,
                                           out MainProgram root,
                                           out ImmutableArray<Diagnostic> diagnostics);
    public SourceText Text { get; init; }

    public ImmutableArray<Diagnostic> Diagnostics { get; init; }

    public MainProgram Root { get; init; }

    private SyntaxTree(SourceText text, ParseHandler handler)
    {
        Text = text;

        handler(this, out var root, out var diagnostics);

        Diagnostics = diagnostics;
        Root = root;
    }

    public static SyntaxTree Load(string fileName)
    {
        var text = File.ReadAllText(fileName);
        var sourceText = SourceText.From(text, fileName);
        return Parse(sourceText);
    }

    private static void Parse(SyntaxTree syntaxTree, out MainProgram root, out ImmutableArray<Diagnostic> diagnostics)
    {
        var parser = new Parser(syntaxTree);
        root = parser.ParseProgram();
        diagnostics = parser.Diagnostics.ToImmutableArray();
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
        return ParseTokens(sourceText);
    }

    public static ImmutableArray<Token> ParseTokens(string text, out ImmutableArray<Diagnostic> diagnostics)
    {
        var sourceText = SourceText.From(text);
        return ParseTokens(sourceText, out diagnostics);
    }

    public static ImmutableArray<Token> ParseTokens(SourceText text)
    {
        return ParseTokens(text, out _);
    }

    public static ImmutableArray<Token> ParseTokens(SourceText text, out ImmutableArray<Diagnostic> diagnostics)
    {
        var tokens = new List<Token>();

        void ParseTokens(SyntaxTree st, out MainProgram root, out ImmutableArray<Diagnostic> d)
        {
            var l = new Lexer(st);
            tokens = l.Tokenize();
            var parser = new Parser(st);
            root = parser.ParseProgram();
            d = l.Diagnostics.ToImmutableArray();
        }

        var syntaxTree = new SyntaxTree(text, ParseTokens);
        diagnostics = syntaxTree.Diagnostics.ToImmutableArray();
        return tokens.ToImmutableArray();
    }
}
