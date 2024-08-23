namespace ECScript.Syntax;

internal sealed class Parser
{
    public Parser(string text)
    {
        var lexer = new Lexer(text);
        foreach(var lexline in lexer.LexLine())
        {
            lexline.Lex();
        }
    }

    public SyntaxToken ParseMain()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseLiteral()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseVariable()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseExpression()
    {
        return ParseTerm();
    }

    public SyntaxToken ParseTerm()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParsePrimaryExpression()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseBinaryExpression()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseAssignmentExpression()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseCallExpression()
    {
        throw new NotImplementedException();
    }
}
