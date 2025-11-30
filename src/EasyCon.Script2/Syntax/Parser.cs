using EasyCon.Script2.Text;
using EasyCon.Script2.Ast;
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
    private bool Match(params TokenType[] types)
    {
        if (types.Any(Check))
        {
            return true;
        }
        return false;
    }
    private Token Consume(TokenType type, string message)
    {
        if (Check(type))
            return Advance();

        var location = new TextLocation(_text, new SourceSpan(_position, 0, 0));
        _diagnostics.ReportUnexpectedToken(location, Current.Type, type);
        throw new Exception(message + $" 行{Current.Line}");
    }

    public MainProgram ParseProgram()
    {
        if (_diagnostics.Count() > 0)
            throw new Exception($"词法分析失败：{_diagnostics.First().Message}");
        var members = ParseMembers();
        return new MainProgram(members);
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
                if (_leadingtrivias.Count > 0)
                {
                    statement.LeadingTrivia.AddRange(_leadingtrivias);
                    _leadingtrivias.Clear();
                }
                if (_trailtrivias.Count > 0)
                {
                    statement.TrailingTrivia.AddRange(_trailtrivias);
                    _trailtrivias.Clear();
                }
                statements.Add(statement);
            }

            // 消耗语句后的换行符
            Consume(TokenType.NEWLINE, "未结束的语句");
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
            if (Match(TokenType.ASSIGN, TokenType.ADD_ASSIGN, TokenType.SUB_ASSIGN,
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

            return new AssignmentStatement(variable, assignmentOp, value)
            {
                Line = variableToken.Line,
                Column = variableToken.Column
            };
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
                func = Advance();
                name = func.Value;
            }
        }

        var args = new List<Expression>();

        if (!Check(TokenType.NEWLINE))
        {
            args.Add(ParsePrimary());
        }

        return new CallExpression(name, args.ToImmutableArray())
        {
            Line = func.Line,
            Column = func.Column
        };
    }

    private IfStatement ParseIfStatement()
    {
        var ifToken = Advance();
        var condition = ParseCondition();

        Consume(TokenType.NEWLINE, "if条件语法不正确");

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
            var elifToken = Consume(TokenType.ELIF, "elif语法不正确");
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
            elseif.Add(new ElseIfClause(elifCond, elifBranch.ToImmutableArray()));
        }

        // else
        ElseClause? elseClause = null;
        if (Current.Type == TokenType.ELSE)
        {
            Advance();
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
            elseClause = new(elseBranch.ToImmutableArray());
        }

        Consume(TokenType.ENDIF, "if语句需要endif结尾");

        return new IfStatement(condition, thenBranch.ToImmutableArray(), elseif.ToImmutableArray(), elseClause)
        {
            Line = ifToken.Line,
            Column = ifToken.Column
        };
    }

    private ForStatement ParseForStatement()
    {
        var forToken = Advance();

        if (Check(TokenType.NEWLINE))
        {
            // 形式1: 无限循环
            Consume(TokenType.NEWLINE, "for语法不正确");
            var body = ParseStatementsUntil(TokenType.NEXT);
            Consume(TokenType.NEXT, "for语句需要next结尾");

            return new ForStatement(null, null, null, null, true, body.ToImmutableArray())
            {
                Line = forToken.Line,
                Column = forToken.Column
            };
        }

        if (Check(TokenType.VAR) && Peek(1)?.Type == TokenType.ASSIGN)
        {
            // 形式2: 范围循环 for $i = 1 to 10
            var loopVar = Consume(TokenType.VAR, "需要初始变量").Value;
            Consume(TokenType.ASSIGN, "for语法不正确, 需要： '='");
            var start = ParseExpression();
            Consume(TokenType.TO, "for语法不正确, 需要： 'TO'");
            var end = ParseExpression();

            Expression step = null;
            if (Match(TokenType.STEP))
            {
                step = ParsePrimary();
            }

            Consume(TokenType.NEWLINE, "for语法不正确");
            var body = ParseStatementsUntil(TokenType.NEXT);
            Consume(TokenType.NEXT, "for语句需要next结尾");

            return new ForStatement(loopVar, start, end, null, false, body.ToImmutableArray())
            {
                Line = forToken.Line,
                Column = forToken.Column
            };
        }
        else
        {
            // 形式3: 计数循环 for 5
            var loopCount = ParsePrimary();
            Consume(TokenType.NEWLINE, "for语法不正确");
            var body = ParseStatementsUntil(TokenType.NEXT);
            Consume(TokenType.NEXT, "for语句需要next结尾");

            return new ForStatement(null, null, null, loopCount, false, body.ToImmutableArray())
            {
                Line = forToken.Line,
                Column = forToken.Column
            };
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
            var value = Consume(TokenType.INT, "break跳出层数必须为数字");
            circle = uint.Parse(value.Value);
        }
        return new BreakStatement(circle)
        {
            Line = breakToken.Line,
            Column = breakToken.Column
        };
    }

    private ContinueStatement ParseContinueStatement()
    {
        var continueToken = Advance();
        Consume(TokenType.NEWLINE, "continue语法不正确");

        return new ContinueStatement()
        {
            Line = continueToken.Line,
            Column = continueToken.Column
        };
    }

    private FunctionDefinitionStatement ParseFunctionDecl()
    {
        var functionToken = Advance();
        var functionName = Consume(TokenType.IDENT, "定义函数需要函数名");

        var parameters = new List<string>();
        if (Check(TokenType.LeftParen))
        {
            Advance();
            // do
            // {
            //     parameters.Add(Consume(TokenType.IDENT, "Expected parameter name").Value);
            // } while (Match(TokenType.COMMA));
            Consume(TokenType.RightParen, "参数列表需要')'结尾");
        }

        Consume(TokenType.NEWLINE, "函数定义语法不正确, 需要换行");

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

        Consume(TokenType.ENDFUNC, "需要endfunc结尾");

        return new FunctionDefinitionStatement(functionName.Value, null, body.ToImmutableArray())
        {
            Line = functionToken.Line,
            Column = functionToken.Column
        };
    }

    private ReturnStatement ParseReturnStatement()
    {
        var returnToken = Consume(TokenType.RETURN, "需要return语句");

        return new ReturnStatement(null)
        {
            Line = returnToken.Line,
            Column = returnToken.Column
        };
    }

    private Statement ParseKeyStatement()
    {
        // 键位(+键位) [持续时间(ms)|DOWN|UP]
        // LS|RS 方向|角度 [, 持续时间(ms)]
        // LS|RS RESET
        var keyToken = Advance();

        // TODO: 按键命令参数解析
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
                        var duration = Consume(TokenType.INT, "摇杆语法不正确");
                        var value = uint.Parse(duration.Value);
                        return new StickStatement(keyToken.Value, state.Value, false, value)
                        {
                            Line = keyToken.Line,
                            Column = keyToken.Column
                        };
                    }
                    else
                    {
                        return new StickStatement(keyToken.Value, state.Value, false)
                        {
                            Line = keyToken.Line,
                            Column = keyToken.Column
                        };
                    }
                case TokenType.ResetKeyword:
                    Consume(TokenType.ResetKeyword, "摇杆语法不正确");
                    return new StickStatement(keyToken.Value, "", true)
                    {
                        Line = keyToken.Line,
                        Column = keyToken.Column
                    };
            }
        }
        else
        {
            if (Check(TokenType.NEWLINE))
            {
                return new ButtonStatement(keyToken.Value)
                {
                    Line = keyToken.Line,
                    Column = keyToken.Column
                };
            }
            else if (Check(TokenType.INT))
            {
                var state = Advance();
                var value = uint.Parse(state.Value);
                return new ButtonStatement(keyToken.Value, value)
                {
                    Line = keyToken.Line,
                    Column = keyToken.Column
                };
            }
            else if (Check(TokenType.ButtonKeyword))
            {
                var state = Advance();
                var isDown = state.Value.ToUpper() == "DOWN";
                return new ButtonStStatement(keyToken.Value, isDown)
                {
                    Line = keyToken.Line,
                    Column = keyToken.Column
                };
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

        while (Match(TokenType.LogicOr))
        {
            var op = Advance();
            var right = ParseLogicalAnd();
            expression = new ConditionExpression(expression, op.Type, right)
            {
                Line = op.Line,
                Column = op.Column
            };
        }

        return expression;
    }

    private Expression ParseLogicalAnd()
    {
        var expression = ParseComparison();

        while (Match(TokenType.LogicAnd))
        {
            var op = Advance();
            var right = ParseComparison();
            expression = new ConditionExpression(expression, op.Type, right)
            {
                Line = op.Line,
                Column = op.Column
            };
        }

        return expression;
    }

    private Expression ParseComparison()
    {
        var expression = ParseExpression();

        while (Match(TokenType.ASSIGN, TokenType.EQL, TokenType.NEQ, TokenType.LESS, TokenType.LEQ, TokenType.GTR, TokenType.GEQ))
        {
            var op = Advance();
            var right = ParseExpression();
            expression = new ConditionExpression(expression, op.Type, right)
            {
                Line = op.Line,
                Column = op.Column
            };
        }

        return expression;
    }

    private Expression ParseAddition()
    {
        var expression = ParseMultiplication();

        while (Match(TokenType.ADD, TokenType.SUB))
        {
            var op = Advance();
            var right = ParseMultiplication();
            expression = new BinaryExpression(expression, op.Type, right)
            {
                Line = op.Line,
                Column = op.Column
            };
        }

        return expression;
    }

    private Expression ParseMultiplication()
    {
        var expression = ParseUnary();

        while (Match(TokenType.MUL, TokenType.DIV, TokenType.SlashI, TokenType.MOD, TokenType.BitAnd, TokenType.XOR))
        {
            var op = Advance();
            var right = ParseUnary();
            expression = new BinaryExpression(expression, op.Type, right)
            {
                Line = op.Line,
                Column = op.Column
            };
        }

        return expression;
    }

    private Expression ParseUnary()
    {
        if (Match(TokenType.BitNot, TokenType.SUB))
        {
            var op = Advance();
            var right = ParseUnary();
            return new BinaryExpression(new LiteralExpression(0), op.Type, right)
            {
                Line = op.Line,
                Column = op.Column
            };
        }

        return ParsePrimary();
    }

    private Expression ParsePrimary()
    {
        if (Match(TokenType.INT))
        {
            var token = Advance();
            try
            {
                var value = uint.Parse(token.Value);
                if (value < ushort.MinValue || value > ushort.MaxValue)
                {
                    throw new Exception($"整数超出范围({ushort.MinValue} 到 {ushort.MaxValue}) 行{token.Line}");
                }
                return new LiteralExpression(value)
                {
                    Line = token.Line,
                    Column = token.Column
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"错误的数字格式: {ex.Message} 行{token.Line}");
                throw new Exception($"错误的数字格式: {token.Value} 行{token.Line}");
            }
        }
        else if (Match(TokenType.CONST, TokenType.VAR, TokenType.EX_VAR))
        {
            var token = Advance();
            bool isConstant = token.Type == TokenType.CONST;
            bool isSpecial = token.Type == TokenType.EX_VAR;
            return new VariableExpression(token.Value, isConstant, isSpecial)
            {
                Line = token.Line,
                Column = token.Column
            };
        }
        else if (Match(TokenType.STRING))
        {
            var token = Advance();
            return new LiteralExpression(token.Value)
            {
                Line = token.Line,
                Column = token.Column
            };
        }

        var location = new TextLocation(_text, new SourceSpan(_position, 0, 0));
        _diagnostics.ReportInvalidExpressionStatement(location, Current.Type);
        throw new Exception($"无效的表达式语句 行{Current.Line}");
    }
}
