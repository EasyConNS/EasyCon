namespace EC.Script.Syntax;

internal sealed class Parser
{
    private readonly DiagnosticBag _diagnostics = [];
    private readonly SyntaxTree _syntaxTree;

    public Parser(SyntaxTree syntaxTree)
    {
        var lexer = new Lexer(syntaxTree);
        lexer.Lex().ToArray();
        _syntaxTree = syntaxTree;
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    private SyntaxToken NextToken()
    {
        throw new NotImplementedException();
    }

    private SyntaxToken MatchToken(TokenType kind)
    {
        throw new NotImplementedException();
    }

    public MainUnitSyntax ParseMainUnit()
    {
        throw new NotImplementedException();
    }

    private StatementSyntax ParseImportDeclaration()
    {
        MatchToken(TokenType.ImportKeyword);
        MatchToken(TokenType.StringToken);
        throw new NotImplementedException();
    }

    private SyntaxToken ParseIndexDeclaration()
    {
        MatchToken(TokenType.LeftBKToken);
        MatchToken(TokenType.RightBKToken);
        throw new NotImplementedException();
    }

    private FunctionDeclarationSyntax ParseFunctionDeclaration()
    {
        MatchToken(TokenType.FunctionKeyword);
        MatchToken(TokenType.IdentifierToken);

        MatchToken(TokenType.LeftPHToken);
        MatchToken(TokenType.RightPHToken);

        MatchToken(TokenType.EndFunctionKeyword);
        throw new NotImplementedException();
    }

    private StatementSyntax ParseStatement()
    {
        ParseAssignmentStatement();
        ParseIfStatement();
        ParseForStatement();
        throw new NotImplementedException();
    }

    private IfStatementSyntax ParseIfStatement()
    {
        MatchToken(TokenType.IfKeyword);
        MatchToken(TokenType.EndifKeyword);
        throw new NotImplementedException();
    }

    private StatementSyntax ParseElIfStatement()
    {
        MatchToken(TokenType.ElifKeyword);
        throw new NotImplementedException();
    }

    private StatementSyntax ParseOptionalElseClause()
    {
        MatchToken(TokenType.ElseKeyword);
        throw new NotImplementedException();
    }

    private ForStatementSyntax ParseForStatement()
    {
        MatchToken(TokenType.ForKeyword);
        MatchToken(TokenType.NextKeyword);
        throw new NotImplementedException();
    }

    private StatementSyntax ParseBreakStatement()
    {
        MatchToken(TokenType.BreakKeyword);
        throw new NotImplementedException();
    }

    private StatementSyntax ParseContinueStatement()
    {
        MatchToken(TokenType.ContinueKeyword);
        throw new NotImplementedException();
    }

    private StatementSyntax ParseReturnStatement()
    {
        MatchToken(TokenType.ReturnKeyword);
        throw new NotImplementedException();
    }

    private AssignmentStatement ParseAssignmentStatement()
    {
        var targetVariable = ParseVariable();
        // AugAssign
        var operatorToken = MatchToken(TokenType.AssignToken);
        var expr = ParseExpression();
        return new AssignmentStatement(_syntaxTree, null, operatorToken, expr);
    }

    private ExpressionSyntax ParseExpression()
    {
        return ParseBinaryExpression();
    }

    private ExpressionSyntax ParseBinaryExpression(int parentPrecedence = 0)
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

    private ExpressionSyntax ParsePrimaryExpression()
    {
        ParseNumberLiteral();
        ParseStringLiteral();
        ParseVariable();
        ParseCallExpression();
        throw new NotImplementedException();
    }

    private ExpressionSyntax ParseParenthesizedExpression()
    {
        MatchToken(TokenType.LeftPHToken);
        var expression = ParseExpression();
        MatchToken(TokenType.RightPHToken);
        throw new NotImplementedException();
    }

    private SyntaxToken ParseNumberLiteral()
    {
        throw new NotImplementedException();
    }

    private ExpressionSyntax ParseStringLiteral()
    {
        throw new NotImplementedException();
    }

    private ExpressionSyntax ParseVariable()
    {
        throw new NotImplementedException();
    }

    private ExpressionSyntax ParseIndexExpression()
    {
        throw new NotImplementedException();
    }

    private ExpressionSyntax ParseCallExpression()
    {
        throw new NotImplementedException();
    }
    private IList<SyntaxToken> ParseArguments()
    {
        throw new NotImplementedException();
    }

    private StatementSyntax ParseKeyAction()
    {
        throw new NotImplementedException();
    }

    private StatementSyntax ParseButtonKeyAction()
    {
        throw new NotImplementedException();
    }

    private StatementSyntax ParseStickKeyAction()
    {
        throw new NotImplementedException();
    }
}
