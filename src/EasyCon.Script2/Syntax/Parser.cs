using EasyCon.Script2.Ast;
using EasyCon.Script2.Text;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace EasyCon.Script2.Syntax;

internal sealed class Parser
{
    private readonly DiagnosticBag _diagnostics = [];
    private readonly SyntaxTree _syntaxTree;
    private readonly SourceText _text;
    private readonly ImmutableArray<Token> _tokens;

    private int _position = 0;
    private readonly List<TriviaNode> _leadingtrivias = [];
    private readonly List<TriviaNode> _trailtrivias = [];

    public DiagnosticBag Diagnostics => _diagnostics;

    public Parser(SyntaxTree syntaxTree)
    {
        var lexer = new Lexer(syntaxTree);
        _tokens = lexer.Tokenize().ToImmutableArray();
        _syntaxTree = syntaxTree;
        _text = syntaxTree.Text;
        _diagnostics.AddRange(lexer.Diagnostics);
    }

    private Token Peek(int offset = 0)
    {
        var index = _position + offset;
        if (index >= _tokens.Length)
            return _tokens[_tokens.Length - 1];

        return _tokens[index];
    }
    private Token Current => Peek();
    private Token Advance()
    {
        var now = Current;
        if (now.Type == TokenType.COMMENT) _leadingtrivias.Add(new TriviaNode(now));

        _position++;

        // advance next non-comment token
        while (Current.Type == TokenType.COMMENT)
        {
            var comment = new TriviaNode(Current);
            if (now.Line == Current.Line)
            {
                _trailtrivias.Add(comment);
            }
            else
            {
                _leadingtrivias.Add(comment);
            }
            _position++;
        }
        return now;
    }
    private bool Check(TokenType type) => Current?.Type == type;

    private Token Match(TokenType type)
    {
        if (Current.Type == type)
        {
            return Advance();
        }
        var location = new TextLocation(_text, new SourceSpan(_position, 0, 0));
        _diagnostics.ReportUnexpectedToken(location, Current.Type, type);
        return new Token(_text, TokenType.BadToken, "", 0, 0);
    }

    private Token Match(TokenType type, string message){return Match(type);}

    private bool Checks(params TokenType[] types)
    {
        return types.Any(Check);
    }

    public MainProgram ParseProgram()
    {
        if (_diagnostics.Any())
            throw new Exception($"词法分析失败：{_diagnostics.First().Message}");
        var members = ParseMembers();
        var eof = Match(TokenType.EOF);
        return new MainProgram(members, eof);
    }


    private ImmutableArray<Statement> ParseMembers()
    {
        var statements = ImmutableArray.CreateBuilder<Statement>();
        _leadingtrivias.Clear();
        _trailtrivias.Clear();

        while (!Check(TokenType.EOF))
        {
            if (Check(TokenType.COMMENT) || Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }

            var statement = ParseMember();
            if (statement != null)
            {
                statements.Add(statement);
            }

            // 消耗语句后的换行符
            Match(TokenType.NEWLINE, "语句没有正确结束换行");
        }

        return statements.ToImmutable();
    }

    private Statement ParseMember()
    {
        if (Current.Type == TokenType.FUNC)
            return ParseFunctionDecl();
        return ParseGlobalStatement();
    }

    private Statement ParseGlobalStatement()
    {
        if (Check(TokenType.IF))
        {
            return ParseIfStatement();
        }
        else if (Check(TokenType.FOR))
        {
            return ParseForStatement();
        }
        else if (Check(TokenType.BREAK))
        {
            return ParseBreakStatement();
        }
        else if (Check(TokenType.CONTINUE))
        {
            return ParseContinueStatement();
        }
        else if (Check(TokenType.RETURN))
        {
            return ParseReturnStatement();
        }
        else if (Check(TokenType.ButtonKeyword) || Check(TokenType.StickKeyword))
        {
            return ParseKeyStatement();
        }
        else if (Check(TokenType.INT) || Check(TokenType.IDENT))
        {
            return ParseSpecIdentStatement();
        }
        return ParseAssignment();
    }

    private AssignmentStatement? ParseAssignment()
    {
        AssignmentStatement? left = null;
        if (Check(TokenType.CONST) || Check(TokenType.VAR))
        {
            var variableToken = Current;
            var variable = ParsePrimary() as VariableExpression;

            Token assignmentOp;
            if (Checks(TokenType.ASSIGN, TokenType.ADD_ASSIGN, TokenType.SUB_ASSIGN,
                     TokenType.MUL_ASSIGN, TokenType.DIV_ASSIGN, TokenType.SlashIAssign,
                     TokenType.MOD_ASSIGN, TokenType.XOR_ASSIGN, TokenType.BitAnd_ASSIGN, TokenType.BitOr_ASSIGN,
                     TokenType.SHL_ASSIGN, TokenType.SHR_ASSIGN))
            {
                assignmentOp = Advance();
            }
            else
            {
                throw new Exception($"不支持的操作符\"{Current.Type}\" 行{Current.Line}");
            }

            var value = ParseExpression();

            return new AssignmentStatement(variableToken, variable, assignmentOp, value);
        }
        return left;
    }

    private Statement ParseSpecIdentStatement()
    {
        var func = Current;
        string name = func.Value;

        if (func.Type == TokenType.INT)
        {
            name = "WAIT";
        }
        else
        {
            Advance();
            if (func.Value.ToLower() == "call")
            {
                var callFn = Advance();
                name = callFn.Value;
            }
        }

        var args = new List<Expression>();

        if (!Check(TokenType.NEWLINE))
        {
            args.Add(ParsePrimary());
        }

        return new CallExpression(func, name, args.ToImmutableArray());
    }

    private IfStatement ParseIfStatement()
    {
        var ifToken = Advance();
        var condition = ParseCondition();

        Match(TokenType.NEWLINE, "if条件语法不正确");

        var thenBranch = new List<Statement>();
        while (!Check(TokenType.ELIF) && !Check(TokenType.ELSE) && !Check(TokenType.ENDIF) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }
            thenBranch.Add(ParseGlobalStatement());
            if (Check(TokenType.NEWLINE)) Advance();
        }

        // else if
        List<ElseIfClause> elseif = [];
        while (Current.Type == TokenType.ELIF)
        {
            var elifToken = Match(TokenType.ELIF, "elif语法不正确");
            var elifCond = ParseCondition();
            var elifBranch = new List<Statement>();
            while (!Check(TokenType.ELSE) && !Check(TokenType.ENDIF) && !Check(TokenType.EOF))
            {
                if (Check(TokenType.NEWLINE))
                {
                    Advance();
                    continue;
                }
                elifBranch.Add(ParseGlobalStatement());
                if (Check(TokenType.NEWLINE)) Advance();
            }
            elseif.Add(new ElseIfClause(elifToken, elifCond, elifBranch.ToImmutableArray()));
        }

        // else
        ElseClause? elseClause = null;
        if (Current.Type == TokenType.ELSE)
        {
            var elseToken = Advance();
            var elseBranch = new List<Statement>();
            while (!Check(TokenType.ENDIF) && !Check(TokenType.EOF))
            {
                if (Check(TokenType.NEWLINE))
                {
                    Advance();
                    continue;
                }
                elseBranch.Add(ParseGlobalStatement());
                if (Check(TokenType.NEWLINE)) Advance();
            }
            elseClause = new(elseToken, elseBranch.ToImmutableArray());
        }

        Match(TokenType.ENDIF, "if语句需要endif结尾");

        return new IfStatement(ifToken, condition, thenBranch.ToImmutableArray(), elseif.ToImmutableArray(), elseClause);
    }

    private ForStatement ParseForStatement()
    {
        var forToken = Advance();

        if (Check(TokenType.NEWLINE))
        {
            // 形式1: 无限循环
            Match(TokenType.NEWLINE, "for语法不正确");
            var body = ParseStatementsUntil(TokenType.NEXT);
            Match(TokenType.NEXT, "for语句需要next结尾");

            return new ForStatement(forToken, null, null, null, null, true, body.ToImmutableArray());
        }

        if (Check(TokenType.VAR) && Peek(1)?.Type == TokenType.ASSIGN)
        {
            // 形式2: 范围循环 for $i = 1 to 10
            var loopVar = Match(TokenType.VAR, "需要初始变量");
            var initVar = new VariableExpression(loopVar, false, false);

            Match(TokenType.ASSIGN, "for语法不正确, 需要： '='");
            var start = ParseExpression();
            Match(TokenType.TO, "for语法不正确, 需要： 'TO'");
            var end = ParseExpression();

            Expression step = null;
            if (Check(TokenType.STEP))
            {
                Advance();
                step = ParsePrimary();
            }

            Match(TokenType.NEWLINE, "for语法不正确");
            var body = ParseStatementsUntil(TokenType.NEXT);
            Match(TokenType.NEXT, "for语句需要next结尾");

            return new ForStatement(forToken, initVar, start, end, null, false, body.ToImmutableArray());
        }
        else
        {
            // 形式3: 计数循环 for 5
            var loopCount = ParsePrimary();
            Match(TokenType.NEWLINE, "for语法不正确");
            var body = ParseStatementsUntil(TokenType.NEXT);
            Match(TokenType.NEXT, "for语句需要next结尾");

            return new ForStatement(forToken, null, null, null, loopCount, false, body.ToImmutableArray());
        }
    }

    private List<Statement> ParseStatementsUntil(TokenType endToken)
    {
        var statements = new List<Statement>();

        while (!Check(endToken) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }
            statements.Add(ParseGlobalStatement());
            if (Check(TokenType.NEWLINE)) Advance();
        }

        return statements;
    }

    private BreakStatement ParseBreakStatement()
    {
        var breakToken = Advance();

        uint circle = 1;
        if (!Check(TokenType.NEWLINE))
        {
            var value = Match(TokenType.INT, "break跳出层数必须为数字");
            circle = uint.Parse(value.Value);
        }
        return new BreakStatement(breakToken, circle);
    }

    private ContinueStatement ParseContinueStatement()
    {
        var continueToken = Advance();
        Match(TokenType.NEWLINE, "continue语法不正确");

        return new ContinueStatement(continueToken);
    }

    private FunctionDefinitionStatement ParseFunctionDecl()
    {
        var functionToken = Advance();
        var functionName = Match(TokenType.IDENT, "定义函数需要函数名");

        var parameters = new List<string>();
        if (Check(TokenType.LeftParen))
        {
            Advance();
            // do
            // {
            //     parameters.Add(Match(TokenType.IDENT, "Expected parameter name").Value);
            // } while (Match(TokenType.COMMA));
            Match(TokenType.RightParen, "参数列表需要')'结尾");
        }

        Match(TokenType.NEWLINE, "函数定义语法不正确, 需要换行");

        var body = new List<Statement>();
        while (!Check(TokenType.ENDFUNC) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }
            body.Add(ParseGlobalStatement());
            if (Check(TokenType.NEWLINE)) Advance();
        }

        Match(TokenType.ENDFUNC, "需要endfunc结尾");

        return new FunctionDefinitionStatement(functionToken, functionName, [], body.ToImmutableArray());
    }

    private ReturnStatement ParseReturnStatement()
    {
        var returnToken = Match(TokenType.RETURN, "错误的return语句");

        return new ReturnStatement(returnToken, null);
    }

    /* TODO */
    private Statement ParseKeyStatement()
    {
        // 键位(+键位) [持续时间(ms)|DOWN|UP]
        // LS|RS 方向|角度 [, 持续时间(ms)]
        // LS|RS RESET
        var keyToken = Advance();

        if (keyToken.Type == TokenType.StickKeyword)
        {
            switch (Current.Type)
            {
                case TokenType.INT:
                case TokenType.ButtonKeyword:
                    var state = Advance();

                    if (Check(TokenType.COMMA))
                    {
                        Advance();
                        var duration = Match(TokenType.INT, "摇杆语法不正确");
                        var value = uint.Parse(duration.Value);
                        return new StickStatement(keyToken, state.Value, false, value);
                    }
                    else
                    {
                        return new StickStatement(keyToken, state.Value, false);
                    }
                case TokenType.ResetKeyword:
                    Match(TokenType.ResetKeyword, "摇杆语法不正确");
                    return new StickStatement(keyToken, "", true);
            }
        }
        else
        {
            if (Check(TokenType.NEWLINE))
            {
                return new ButtonStatement(keyToken);
            }
            else if (Check(TokenType.INT))
            {
                var state = Advance();
                var value = uint.Parse(state.Value);
                return new ButtonStatement(keyToken, value)
;
            }
            else if (Check(TokenType.ButtonKeyword))
            {
                var state = Advance();
                var isDown = state.Value.ToUpper() == "DOWN";
                return new ButtonStStatement(keyToken, isDown);
            }
        }
        throw new Exception($"按键语法不正确 {keyToken.Line}");
    }

    // 计算表达式
    private Expression ParseExpression()
    {
        return ParseAddition();
    }

    // 条件表达式
    private Expression ParseCondition()
    {
        return ParseComparison();
    }

    private Expression ParseLogicalOr()
    {
        var expression = ParseLogicalAnd();

        while (Check(TokenType.LogicOr))
        {
            var op = Advance();
            var right = ParseLogicalAnd();
            expression = new ConditionExpression(expression, op, right);
        }

        return expression;
    }

    private Expression ParseLogicalAnd()
    {
        var expression = ParseComparison();

        while (Check(TokenType.LogicAnd))
        {
            var op = Advance();
            var right = ParseComparison();
            expression = new ConditionExpression(expression, op, right);
        }

        return expression;
    }

    private Expression ParseComparison()
    {
        var expression = ParseExpression();

        while (Checks(TokenType.ASSIGN, TokenType.EQL, TokenType.NEQ, TokenType.LESS, TokenType.LEQ, TokenType.GTR, TokenType.GEQ))
        {
            var op = Advance();
            var right = ParseExpression();
            expression = new ConditionExpression(expression, op, right);
        }

        return expression;
    }

    private Expression ParseAddition()
    {
        var expression = ParseMultiplication();

        while (Checks(TokenType.ADD, TokenType.SUB))
        {
            var op = Advance();
            var right = ParseMultiplication();
            expression = new BinaryExpression(expression, op, right);
        }

        return expression;
    }

    private Expression ParseMultiplication()
    {
        var expression = ParseUnary();

        while (Checks(TokenType.MUL, TokenType.DIV, TokenType.SlashI, TokenType.MOD, TokenType.BitAnd, TokenType.XOR))
        {
            var op = Advance();
            var right = ParseUnary();
            expression = new BinaryExpression(expression, op, right);
        }

        return expression;
    }

    private Expression ParseUnary()
    {
        if (Checks(TokenType.BitNot, TokenType.SUB))
        {
            var op = Advance();
            var right = ParseUnary();
            return new UnaryExpression(op, right);
        }

        return ParsePrimary();
    }

    private Expression ParseIndexExpression()
    {
        var items = new List<Expression>();
        var lb = Match(TokenType.LeftBracket, "参数列表需要'['");

        if (Checks(TokenType.INT, TokenType.CONST, TokenType.VAR))
        {
            do
            {
                if(Check(TokenType.COMMA))Advance();

                items.Add(ParsePrimary());
            } while (Check(TokenType.COMMA));
        }

        var rb = Match(TokenType.RightBracket, "参数列表需要']'");

        return new IndexExpression(lb, items.ToImmutableArray(), rb);
    }

    private Expression ParsePrimary()
    {
        if (Check(TokenType.INT))
        {
            var token = Advance();
            try
            {
                var value = uint.Parse(token.Value);
                if (value < ushort.MinValue || value > ushort.MaxValue)
                {
                    throw new Exception($"整数超出范围({ushort.MinValue} 到 {ushort.MaxValue}) 行{token.Line}");
                }
                return new LiteralExpression(token, value);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"错误的数字格式: {ex.Message} 行{token.Line}");
                throw new Exception($"错误的数字格式: {token.Value} 行{token.Line}");
            }
        }
        else if (Checks(TokenType.CONST, TokenType.VAR, TokenType.EX_VAR))
        {
            var token = Advance();
            bool isConstant = token.Type == TokenType.CONST;
            bool isSpecial = token.Type == TokenType.EX_VAR;
            return new VariableExpression(token, isConstant, isSpecial);
        }
        else if (Check(TokenType.STRING))
        {
            var token = Advance();
            return new LiteralExpression(token, token.Value);
        }
        else if (Check(TokenType.LeftBracket))
        {
            return ParseIndexExpression();
        }

        var location = new TextLocation(_text, new SourceSpan(_position, 0, 0));
        _diagnostics.ReportInvalidExpressionStatement(location, Current.Type);
        throw new Exception($"无效的表达式语句 行{Current.Line}");
    }
}
