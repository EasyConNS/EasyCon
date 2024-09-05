using EC.Script.Text;
using System.Collections.Immutable;

namespace EC.Script.Syntax;

internal sealed partial class Lexer(SyntaxTree syntaxTree)
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly SyntaxTree _syntaxTree = syntaxTree;
    private readonly SourceText _text = syntaxTree.Text;

    private ImmutableArray<SyntaxToken>.Builder _tokenBuilder = ImmutableArray.CreateBuilder<SyntaxToken>();

    public IEnumerable<SyntaxLine> Lex()
    {
        foreach(var l in _text.Lines)
        {
            var trivia = new SyntaxTrivia(TokenType.CommentTrivia, l.Comment);

            ReadToken(l.Text);

            yield return new SyntaxLine(_tokenBuilder.ToImmutable(), trivia, l.Line);
        }
    }

    private void ReadToken(string text)
    {
        _tokenBuilder.Clear();
        // TODO
        //_tokenBuilder.Add();
    }
}
 