using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

public sealed class SyntaxTree
{
    private delegate void ParseHandler(SyntaxTree syntaxTree, IEnumerable<ExternalVariable> extVars,
                                           out CompicationUnit root, out ImmutableArray<CompicationUnit> flattenroot,
                                           out ImmutableArray<Diagnostic> diagnostics);
    public SourceText Text { get; init; }

    public ImmutableArray<Diagnostic> Diagnostics { get; init; }

    internal CompicationUnit Root { get; init; }
    internal ImmutableArray<CompicationUnit> FlattenRoot { get; init; }

    private SyntaxTree(SourceText text, IEnumerable<ExternalVariable> extVars, ParseHandler handler)
    {
        Text = text;

        handler(this, extVars, out var root, out var froot, out var diagnostics);

        Diagnostics = diagnostics;
        Root = root;
        FlattenRoot = froot;
    }

    public static SyntaxTree Load(string fileName, IEnumerable<ExternalVariable> extVars)
    {
        var text = File.ReadAllText(fileName);
        var sourceText = SourceText.From(text, fileName);
        return Parse(sourceText, extVars);
    }

    private static void Parse(SyntaxTree syntaxTree, IEnumerable<ExternalVariable> extVars, out CompicationUnit root, out ImmutableArray<CompicationUnit> fullSyntax, out ImmutableArray<Diagnostic> diagnostics)
    {
        var parser = new Parser(syntaxTree, extVars);
        root = parser.ParseProgram();
        fullSyntax = parser.Flatten(root);
        diagnostics = [.. parser.Diagnostics];
    }

    public static SyntaxTree Parse(string text, IEnumerable<ExternalVariable> extVars)
    {
        var sourceText = SourceText.From(text);
        return Parse(sourceText, extVars);
    }

    public static SyntaxTree Parse(SourceText text, IEnumerable<ExternalVariable> extVars)
    {
        return new SyntaxTree(text, extVars, Parse);
    }

    public static ImmutableArray<Token> ParseTokens(string text)
    {
        var sourceText = SourceText.From(text);
        return ParseTokens(sourceText, out _);
    }

    private static ImmutableArray<Token> ParseTokens(SourceText text, out ImmutableArray<Diagnostic> diagnostics)
    {
        var tokens = new ImmutableArray<Token>();

        void ParseTokens(SyntaxTree st, IEnumerable<ExternalVariable> _, out CompicationUnit __, out ImmutableArray<CompicationUnit> ___, out ImmutableArray<Diagnostic> d)
        {
            __ = null;
            ___ = default;
            d = default;
            var l = new Lexer(st);
            tokens = l.Tokenize();
            d = [.. l.Diagnostics];
        }

        var syntaxTree = new SyntaxTree(text, [], ParseTokens);
        diagnostics = syntaxTree.Diagnostics;
        return tokens;
    }
}
