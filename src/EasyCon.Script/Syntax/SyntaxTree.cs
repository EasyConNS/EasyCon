using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

public sealed class SyntaxTree
{
    private delegate void ParseHandler(SyntaxTree syntaxTree, IEnumerable<string> extVarNames,
                                           out CompicationUnit root, out ImmutableArray<CompicationUnit> flattenroot,
                                           out ImmutableArray<Diagnostic> diagnostics);
    public SourceText Text { get; init; }

    public ImmutableArray<Diagnostic> Diagnostics { get; init; }

    internal CompicationUnit Root { get; init; }
    internal ImmutableArray<CompicationUnit> FlattenRoot { get; init; }

    private SyntaxTree(SourceText text, IEnumerable<string> extVarNames, ParseHandler handler)
    {
        Text = text;

        handler(this, extVarNames, out var root, out var froot, out var diagnostics);

        Diagnostics = diagnostics;
        Root = root;
        FlattenRoot = froot;
    }

    public static SyntaxTree Load(string fileName, IEnumerable<string> extVarNames)
    {
        var text = File.ReadAllText(fileName);
        var sourceText = SourceText.From(text, fileName);
        return Parse(sourceText, extVarNames);
    }

    private static void Parse(SyntaxTree syntaxTree, IEnumerable<string> extVarNames, out CompicationUnit root, out ImmutableArray<CompicationUnit> fullSyntax, out ImmutableArray<Diagnostic> diagnostics)
    {
        var parser = new Parser(syntaxTree, extVarNames);
        root = parser.ParseProgram();
        fullSyntax = parser.Flatten(root);
        diagnostics = [.. parser.Diagnostics];
    }

    public static SyntaxTree Parse(string text, IEnumerable<string> extVarNames)
    {
        var sourceText = SourceText.From(text);
        return Parse(sourceText, extVarNames);
    }

    public static SyntaxTree Parse(SourceText text, IEnumerable<string> extVarNames)
    {
        return new SyntaxTree(text, extVarNames, Parse);
    }

    public static ImmutableArray<Token> ParseTokens(string text)
    {
        var sourceText = SourceText.From(text);
        return ParseTokens(sourceText, out _);
    }

    private static ImmutableArray<Token> ParseTokens(SourceText text, out ImmutableArray<Diagnostic> diagnostics)
    {
        var tokens = new ImmutableArray<Token>();

        void ParseTokens(SyntaxTree st, IEnumerable<string> _, out CompicationUnit __, out ImmutableArray<CompicationUnit> ___, out ImmutableArray<Diagnostic> d)
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
