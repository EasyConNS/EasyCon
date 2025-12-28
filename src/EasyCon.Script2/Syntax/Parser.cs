using EasyCon.Script2.Ast;
using EasyCon.Script2.Text;
using System.Collections.Immutable;

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
        _tokens = lexer.Tokenize();
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
        while (Check(TokenType.COMMENT))
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

    private Token Match(TokenType type, string message = "")
    {
        if (Current.Type == type)
        {
            return Advance();
        }
        var location = new TextLocation(_text, new SourceSpan(_position, 0, 0));
        _diagnostics.ReportUnexpectedToken(location, Current.Type, type);
        return new Token(_text, TokenType.BadToken, message, 0, 0);
    }

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
        var imports = members.OfType<ImportStatement>().ToImmutableArray();
        return new MainProgram(imports, members.Except(imports).ToImmutableArray(), eof);
    }


    private ImmutableArray<Member> ParseMembers()
    {
        var statements = ImmutableArray.CreateBuilder<Member>();

        _leadingtrivias.Clear();
        _trailtrivias.Clear();

        while (!Check(TokenType.EOF))
        {
            var startToken = Current;

            if (Check(TokenType.COMMENT) || Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }

            var statement = ParseMember();
            statements.Add(statement);
            if(startToken == Current)Advance();
        }

        return statements.ToImmutable();
    }

    private T WithTrivia<T>(Func<T> factory) where T : Statement
    {
        // 保存当前的注释状态
        var leadingTrivia = new List<TriviaNode>(_leadingtrivias);

        // 清空现有的注释列表
        _leadingtrivias.Clear();

        // 创建节点
        var node = factory();
        // 消耗语句后的换行符
        Match(TokenType.NEWLINE, "语句没有正确结束换行");

        // var trailingTrivia = new List<TriviaNode>(_trailtrivias);
        // _trailtrivias.Clear();

        // 关联注释
        node.LeadingTrivia.AddRange(leadingTrivia);
        // node.TrailingTrivia.AddRange(trailingTrivia);

        return node;
    }

    private KeywordStatement ParseTokenStatement<T>(TokenType ttype, string msg) where T : Statement
    {
        var keyword = Match(ttype, msg);

        return new KeywordStatement(keyword);
    }

    private Member ParseMember()
    {
        if (Check(TokenType.IMPORT))
            return ParseImportDecl();
        if (Check(TokenType.FUNC))
            return ParseFunctionDecl();
        return ParseGlobalStatement();
    }

    private Member ParseGlobalStatement()
    {
        var stmt = WithTrivia(() => ParseStatementInternal());
        return new GlobalStatement(stmt);
    }

    private Statement ParseStatementInternal()
    {
        switch (Current.Type)
        {
            case TokenType.IF:
                return ParseIfStatement();
            case TokenType.FOR:
                return ParseForStatement();
            case TokenType.BREAK:
                return ParseBreakStatement();
            case TokenType.CONTINUE:
                return ParseContinueStatement();
            case TokenType.RETURN:
                return ParseReturnStatement();
            case TokenType.ButtonKeyword:
            case TokenType.StickKeyword:
                return ParsePadButtonStatement();
            case TokenType.INT:
            case TokenType.IDENT:
                return ParseSpecIdentStatement();
            default:
                return ParseAssignment();
        }
    }

    private ImportStatement ParseImportDecl()
    {
        var import = Match(TokenType.IMPORT);
        if (Check(TokenType.STRING))
        {
            var path = Match(TokenType.STRING);

            var name = "";
            if (Check(TokenType.IDENT)) name = Advance().Value;

            return new ImportStatement(import, name, new LiteralExpression(path, path.value));
        }
        throw new Exception($"导入语句错误\"{import.Type}\" 行{import.Line}");
    }

    private AssignmentStatement ParseAssignment()
    {
        if (Check(TokenType.CONST) || Check(TokenType.VAR))
        {
            var variableToken = Current;
            var variable = ParsePrimary() as VariableExpression;

            Token assignmentOp;
            if (Checks(TokenType.ASSIGN, TokenType.ADD_ASSIGN, TokenType.SUB_ASSIGN,
                     TokenType.MUL_ASSIGN, TokenType.DIV_ASSIGN, TokenType.SlashI_ASSIGN,
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
        throw new Exception($"不支持的赋值操作\"{Current.Type}\" 行{Current.Line}");
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
        var ifcond = WithTrivia(() => ParseIfCondition());

        var thenBranch = ParseStatementsUntil(TokenType.ELIF, TokenType.ELSE, TokenType.ENDIF);

        // else if
        List<ElseIfClause> elseif = [];
        while (Check(TokenType.ELIF))
        {
            var elifcond = WithTrivia(() => ParseIfCondition());

            var elifBranch = ParseStatementsUntil(TokenType.ELIF, TokenType.ELSE, TokenType.ENDIF);
            elseif.Add(new(elifcond, elifBranch.ToImmutableArray()));
        }

        // else
        ElseClause? elseClause = null;
        if (Check(TokenType.ELSE))
        {
            var elsestmt = WithTrivia(() => ParseTokenStatement<KeywordStatement>(TokenType.ELSE, "else解析失败"));

            var elseBranch = ParseStatementsUntil(TokenType.ENDIF);
            elseClause = new(elsestmt, elseBranch.ToImmutableArray());
        }

        var endif = ParseTokenStatement<KeywordStatement>(TokenType.ENDIF, "if语句需要endif结尾");

        return new IfStatement(ifcond, thenBranch.ToImmutableArray(), elseif.ToImmutableArray(), elseClause, endif);
    }

    private IfCondition ParseIfCondition()
    {
        var ifToken = Advance();
        var condition = ParseCondition();
        return new IfCondition(ifToken, condition);
    }

    private ForStatement ParseForStatement()
    {
        var forToken = Advance();

        var infinite = false;

        VariableExpression initVar = null;
        Expression start = null;
        Expression end = null;

        if (Check(TokenType.NEWLINE))
        {
            // 形式1: 无限循环
            infinite = true;
        }
        else if (Check(TokenType.VAR) && Peek(1)?.Type == TokenType.ASSIGN)
        {
            // 形式2: 范围循环 for $i = 1 to 10
            var loopVar = Match(TokenType.VAR, "需要初始变量");
            initVar = new VariableExpression(loopVar, false, false);

            Match(TokenType.ASSIGN, "for语法不正确, 需要： '='");
            start = ParsePrimary();
            Match(TokenType.TO, "for语法不正确, 需要： 'TO'");
            end = ParsePrimary();

            Expression step = null;
            if (Check(TokenType.STEP))
            {
                Advance();
                step = ParsePrimary();
            }
        }
        else
        {
            // 形式3: 计数循环 for 5
            end = ParsePrimary();
        }

        Match(TokenType.NEWLINE, "for语法不正确");

        var body = ParseStatementsUntil(TokenType.NEXT);
        var nextTok = WithTrivia(() => ParseTokenStatement<KeywordStatement>(TokenType.NEXT, "for语句需要next结尾"));

        return new ForStatement(new ForExpr(forToken, initVar, start, end, infinite), body.ToImmutableArray(), nextTok);
    }

    private ImmutableArray<Statement> ParseStatementsUntil(params TokenType[] endToken)
    {
        var statements = ImmutableArray.CreateBuilder<Statement>();

        while (!Checks(endToken) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }
            statements.Add(WithTrivia(() => ParseStatementInternal()));
            if (Check(TokenType.NEWLINE)) Advance();
        }

        return statements.ToImmutable();
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
        
        var parameters = ImmutableArray<VariableExpression>.Empty;
        if (Check(TokenType.LeftParen))
        {
            var openParenthesisToken = Match(TokenType.LeftParen);
            parameters = ParseParameterList();
            var closeParenthesisToken = Match(TokenType.RightParen);
        }

        Match(TokenType.NEWLINE, "函数定义语法不正确, 需要换行");

        var body = ParseStatementsUntil(TokenType.ENDFUNC);

        var endFunc = Match(TokenType.ENDFUNC, "需要endfunc结尾");

        return new FunctionDefinitionStatement(new FuncDeclare(functionToken, functionName, parameters), body.ToImmutableArray(), new KeywordStatement(endFunc));
    }

    private ImmutableArray<VariableExpression> ParseParameterList()
    {
        var nodesAndSeparators = ImmutableArray.CreateBuilder<VariableExpression>();

        var parseNextParameter = true;
        while (parseNextParameter &&
                Current.Type != TokenType.RightParen &&
                Current.Type != TokenType.EOF)
        {
            var parameter = ParseParameter();
            nodesAndSeparators.Add(parameter);

            if (Check(TokenType.COMMA))
            {
                var comma = Advance();
                // nodesAndSeparators.Add(comma);
            }
            else
            {
                parseNextParameter = false;
            }
        }
        return nodesAndSeparators.ToImmutable();
    }
    private VariableExpression ParseParameter()
    {
        var identifier = Match(TokenType.VAR);
        return new VariableExpression(identifier, false, false);
    }

    private ReturnStatement ParseReturnStatement()
    {
        var returnToken = Match(TokenType.RETURN, "错误的return语句");

        return new ReturnStatement(returnToken, null);
    }

    /// <summary>
    /// 解析手柄按键语句
    /// </summary>
    private Statement ParsePadButtonStatement()
    {
        // 键位(+键位) [持续时间(ms)|DOWN|UP]
        // LS|RS 方向|角度 [, 持续时间(ms)]
        // LS|RS RESET
        List<Token> keyTokens = [];
        var firstKey = Advance();
        keyTokens.Add(firstKey);

        if (firstKey.Type == TokenType.StickKeyword)
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
                        return new StickStatement([.. keyTokens], state.Value, false, value);
                    }
                    else
                    {
                        return new StickStatement([.. keyTokens], state.Value, false);
                    }
                case TokenType.ResetKeyword:
                    Match(TokenType.ResetKeyword, "摇杆语法不正确");
                    return new StickStatement([.. keyTokens], "", true);
            }
        }
        else
        {
            while (Check(TokenType.ADD))
            {
                Match(TokenType.ADD);
                var key = Match(TokenType.ButtonKeyword);
                keyTokens.Add(key);
            }
            if (Check(TokenType.NEWLINE))
            {
                return new ButtonStatement([.. keyTokens]);
            }
            else if (Check(TokenType.INT))
            {
                var state = Advance();
                var value = uint.Parse(state.Value);
                return new ButtonStatement([.. keyTokens], value)
;
            }
            else if (Check(TokenType.ButtonKeyword))
            {
                var state = Advance();
                var isDown = state.Value.ToUpper() == "DOWN";
                return new ButtonStStatement([.. keyTokens], isDown);
            }
        }
        throw new Exception($"按键语法不正确 {keyTokens.First().Line}");
    }

    // 计算表达式
    private Expression ParseExpression()
    {
        return ParseBinaryExpression();
    }

    private Expression ParseBinaryExpression(int parentPrecedence = 0)
    {
        Expression left;
        var unaryOperatorPrecedence = Current.Type.GetUnaryOperatorPrecedence();
        if (unaryOperatorPrecedence != 0 && unaryOperatorPrecedence >= parentPrecedence)
        {
            var operatorToken = Advance();
            var operand = ParseBinaryExpression(unaryOperatorPrecedence);
            left = new UnaryExpression(operatorToken, operand);
        }
        else
        {
            left = ParsePrimary();
        }

        while (true)
        {
            var precedence = Current.Type.GetBinaryOperatorPrecedence();
            if (precedence == 0 || precedence <= parentPrecedence)
                break;

            var operatorToken = Advance();
            var right = ParseBinaryExpression(precedence);
            left = new BinaryExpression(left, operatorToken, right);
        }

        return left;
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

        while (Checks(TokenType.EQL, TokenType.NEQ, TokenType.LESS, TokenType.LEQ, TokenType.GTR, TokenType.GEQ))
        {
            var op = Advance();
            var right = ParseExpression();
            expression = new ConditionExpression(expression, op, right);
        }

        return expression;
    }

    private Expression ParseIndexExpression()
    {
        var items = new List<Expression>();
        var lb = Match(TokenType.LeftBracket, "参数列表需要'['");

        if (Checks(TokenType.INT, TokenType.CONST, TokenType.VAR))
        {
            do
            {
                if (Check(TokenType.COMMA)) Advance();

                items.Add(ParsePrimary());
            } while (Check(TokenType.COMMA));
        }

        var rb = Match(TokenType.RightBracket, "参数列表需要']'");

        return new IndexExpression(lb, items.ToImmutableArray(), rb);
    }

    private Expression ParsePrimary()
    {
        switch (Current.Type)
        {
            case TokenType.STRING:
                var tokstr = Advance();
                return new LiteralExpression(tokstr, tokstr.Value);
            case TokenType.CONST:
            case TokenType.VAR:
            case TokenType.EX_VAR:
                var token = Advance();
                bool isConstant = token.Type == TokenType.CONST;
                bool isSpecial = token.Type == TokenType.EX_VAR;
                return new VariableExpression(token, isConstant, isSpecial);
            case TokenType.LeftBracket:
                return ParseIndexExpression();
            case TokenType.LeftParen:
                var left = Advance();
                var expression = ParseExpression();
                var right = Match(TokenType.RightParen);
                return new ParenthesizedExpression(left, expression, right);
            case TokenType.INT:
            default:
                var toknum = Advance();
                try
                {
                    var value = uint.Parse(toknum.Value);
                    if (value < ushort.MinValue || value > ushort.MaxValue)
                    {
                        throw new Exception($"整数超出范围({ushort.MinValue} 到 {ushort.MaxValue}) 行{toknum.Line}");
                    }
                    return new LiteralExpression(toknum, value);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"错误的数字格式: {ex.Message} 行{toknum.Line}");
                    throw new Exception($"错误的数字格式: {toknum.Value} 行{toknum.Line}");
                }
        }
    }
}
