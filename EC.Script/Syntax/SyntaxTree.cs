using EC.Script.Text;
using System.Collections.Immutable;

namespace EC.Script.Syntax;

public sealed class SyntaxTree
{
    private delegate void ParseHandler(SyntaxTree syntaxTree,
                                           out MainUnitSyntax root,
                                           out ImmutableArray<Diagnostic> diagnostics);
    public SourceText Text { get; init; }

    public ImmutableArray<Diagnostic> Diagnostics { get; init; }

    public MainUnitSyntax Root { get; init; }

    private SyntaxTree(SourceText text, ParseHandler handler)
    {
        Text = text;

        handler(this, out var root, out var diagnostics);

        Diagnostics = diagnostics;
        Root = root;
    }
    private static void Parse(SyntaxTree syntaxTree, out MainUnitSyntax root, out ImmutableArray<Diagnostic> diagnostics)
    {
        var parser = new Parser(syntaxTree);
        root = parser.ParseMainUnit();
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
}
