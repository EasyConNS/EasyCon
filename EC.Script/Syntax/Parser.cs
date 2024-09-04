namespace EC.Script.Syntax;

internal sealed class Parser
{
    private readonly DiagnosticBag _diagnostics = [];
    public Parser(SyntaxTree syntaxTree)
    {
        var lexer = new Lexer(syntaxTree);
        lexer.Lex();
    }

    public DiagnosticBag Diagnostics => _diagnostics;
    private SyntaxToken NextToken()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken MatchToken(TokenType kind)
    {
        throw new NotImplementedException();
    }

    public MainUnitSyntax ParseMainUnit()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseFunctionDeclaration()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseStatement()
    {
        ParseAssignmentStatement();
        ParseIfStatement();
        ParseForStatement();
        throw new NotImplementedException();
    }

    public SyntaxToken ParseIfStatement()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseElIfStatement()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseOptionalElseClause()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseForStatement()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseBreakStatement()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseContinueStatement()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseReturnStatement()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseAssignmentStatement()
    {
        var targetVariable = ParseVariable();
        // AugAssign
        MatchToken(TokenType.AssignToken);
        return ParseExpression();
    }

    public SyntaxToken ParseExpression()
    {
        return ParseBinaryExpression();
    }

    public SyntaxToken ParseParenthesizedExpression()
    {
        MatchToken(TokenType.LeftPHToken);
        var expression = ParseExpression();
        MatchToken(TokenType.RightPHToken);
        throw new NotImplementedException();
    }

    public SyntaxToken ParseBinaryExpression(int parentPrecedence = 0)
    {
        var unaryOperatorPrecedence = TokenType.NotKeyword.GetUnaryOperatorPrecedence();
        if (unaryOperatorPrecedence != 0 && unaryOperatorPrecedence >= parentPrecedence)
        {
            // TODO UnaryExpression
        }
        else
        {
            ParsePrimaryExpression();
        }

        while (true)
        {
            // TODO BinaryExpression
        }
        throw new NotImplementedException();
    }

    public SyntaxToken ParsePrimaryExpression()
    {
        ParseNumberLiteral();
        ParseStringLiteral();
        ParseVariable();
        ParseCallExpression();
        throw new NotImplementedException();
    }

    public SyntaxToken ParseNumberLiteral()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseStringLiteral()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseVariable()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseCallExpression()
    {
        throw new NotImplementedException();
    }
    public IList<SyntaxToken> ParseArguments()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseKeyAction()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseButtonKeyAction()
    {
        throw new NotImplementedException();
    }

    public SyntaxToken ParseStickKeyAction()
    {
        throw new NotImplementedException();
    }
}
