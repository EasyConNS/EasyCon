using EC.Script.Text;

namespace EC.Script.Syntax;

internal sealed partial class Lexer
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly SyntaxTree _syntaxTree;
    private readonly SourceText _text;

    public Lexer(SyntaxTree syntaxTree)
    {
        _syntaxTree = syntaxTree;
        _text = syntaxTree.Text;
    }

    public IEnumerable<TextLine> Lex()
    {
        return _text.Lines;
    }
}
 