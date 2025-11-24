#if true

namespace EasyScript;

internal class Parser(Lexer lexer)
{
    private readonly List<Token> _tokens = lexer.Tokenize();
    private int _position = 0;
    private readonly Stack<int> _indentationStack = new();

    private Token Peek(int offset = 0)
    {
        var index = _position + offset;
        if (index >= _tokens.Count)
            return _tokens[_tokens.Count - 1];

        return _tokens[index];
    }
    private Token Current => Peek();
    private Token Advance()
    {
        var current = Current;
        _position++;
        return current;
    }
    private bool Check(TokenType type) => Current?.Type == type;
    private bool Match(params TokenType[] types)
    {
        if (types.Any(Check))
        {
            Advance();
            return true;
        }
        return false;
    }

    private Token Consume(TokenType type, string message)
    {
        if (Check(type))
            return Advance();

        throw new Exception($"{message}:错误的 {Current.Value} L:{Peek()?.Line ?? 0}");
    }

    public List<Statement> Parse()
    {
        var statements = new List<Statement>();

        while (!Check(TokenType.EOF))
        {
            if (Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }

            var leadingTrivia = new List<string>();
            while(Check(TokenType.CommentTrivia))
            {
                var trivia = Advance();
                leadingTrivia.Add(trivia.Value);
            }

            var statement = ParseStatement();
            if (statement != null)
            {
                statement.LeadingTrivia.AddRange(leadingTrivia);
                statements.Add(statement);
            }

            // TODO trailing trivia

            // 消耗语句后的换行符
            if (Check(TokenType.NEWLINE))
            {
                Advance();
            }
        }

        return statements;
    }

    private Statement ParseStatement()
    {
        if (Check(TokenType.Const) || Check(TokenType.Variable))
        {
            return ParseAssignment();
        }
        else if (Check(TokenType.If))
        {
            return ParseIfStatement();
        }
        else if (Check(TokenType.For))
        {
            return ParseForStatement();
        }
        else if (Check(TokenType.Func))
        {
            return ParseFunctionDecl();
        }
        else if (Check(TokenType.ButtonKeyword))
        {
            return ParseKeyStatement();
        }
        else if (Check(TokenType.Integer))
        {
            return ParseSpecIdentStatement();
        }
        else if (Check(TokenType.Identifier))
        {
            return ParseSpecIdentStatement();
        }

        if (Check(TokenType.CommentTrivia))
        {
            Advance();
            return null;
        }
        else
            throw new Exception($"语法错误:{Current.Type}:L{Current.Line}");
    }

    private AssignmentStatement ParseAssignment()
    {
        var variableToken = Advance();
        string variableName = variableToken.Value;
        bool isConstant = variableToken.Type == TokenType.Const;

        TokenType assignmentOp;
        if (Match(TokenType.Assign, TokenType.PlusAssign, TokenType.MinusAssign,
                 TokenType.MultiplyAssign, TokenType.DivideAssign, TokenType.SlashIAssign,
                 TokenType.ModulusAssign, TokenType.BitXorAssign, TokenType.BitAndAssign, TokenType.BitOrAssign,
                 TokenType.LeftShiftAssign, TokenType.RightShiftAssign))
        {
            assignmentOp = _tokens[_position - 1].Type;
        }
        else
        {
            throw new Exception($"不支持的操作符:{Current.Type}:L{Current.Line}");
        }

        var value = ParseExpression();

        return new AssignmentStatement(variableName, isConstant, assignmentOp, value)
        {
            Line = variableToken.Line,
            Column = variableToken.Column
        };
    }

    private Statement ParseSpecIdentStatement()
    {
        var func = Peek();
        var cur = Peek(1);

        string name;
        var args = new List<Expression>();

        if (Current.Type == TokenType.Integer)
        {
            name = "WAIT";
            args.Add(ParsePrimary());
        }
        else
        {
            Advance();

            name = func.Value;
            if (func.Value.ToUpper() == "CALL")
            {
                name = cur.Value;
            }
            else
            {
                args.Add(ParsePrimary());
            }
        }

        while (!Check(TokenType.NEWLINE))
        {
            Advance();
        }

        return new CallExpression(name, args)
        {
            Line = func.Line,
            Column = func.Column
        };
    }

    private IfStatement ParseIfStatement()
    {
        var ifToken = Consume(TokenType.If, "if语法不正确");
        var condition = ParseCondition();

        Consume(TokenType.NEWLINE, "if条件语法不正确");

        var thenBranch = new List<Statement>();
        while (!Check(TokenType.Elif) && !Check(TokenType.Endif) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }
            thenBranch.Add(ParseStatement());
            if (Check(TokenType.NEWLINE)) Advance();
        }

        var elseBranch = new List<Statement>();
        while (!Check(TokenType.Endif) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }
            elseBranch.Add(ParseStatement());
            if (Check(TokenType.NEWLINE)) Advance();
        }
        ElseClause? elseClause = elseBranch.Count > 0 ? new ElseClause(elseBranch) : null;

        Consume(TokenType.Endif, "需要endif结尾");

        return new IfStatement(condition, thenBranch, null, elseClause)
        {
            Line = ifToken.Line,
            Column = ifToken.Column
        };
    }

    private ForStatement ParseForStatement()
    {
        var forToken = Consume(TokenType.For, "for语法不正确");

        if (Check(TokenType.NEWLINE))
        {
            // 形式1: 无限循环
            Consume(TokenType.NEWLINE, "需要换行");
            var body = ParseStatementsUntil(TokenType.Next);
            Consume(TokenType.Next, "for语句需要next结尾");

            return new ForStatement(null, null, null, null, true, body)
            {
                Line = forToken.Line,
                Column = forToken.Column
            };
        }

        if (Check(TokenType.Variable) && Peek(1)?.Type == TokenType.Assign)
        {
            // 形式2: 范围循环 for $i = 1 to 10 step 2
            var loopVar = Consume(TokenType.Variable, "需要初始变量").Value;
            Consume(TokenType.Assign, "for语法不正确, 需要： '='");
            var start = ParseExpression();
            Consume(TokenType.To, "for语法不正确, 需要： 'TO'");
            var end = ParseExpression();

            Expression step = null;
            if (Match(TokenType.Step))
            {
                step = ParsePrimary();
            }

            Consume(TokenType.NEWLINE, "需要换行");
            var body = ParseStatementsUntil(TokenType.Next);
            Consume(TokenType.Next, "Expected 'next' after for loop body");

            return new ForStatement(loopVar, (VariableExpression)start, (VariableExpression)end, null, false, body)
            {
                Line = forToken.Line,
                Column = forToken.Column
            };
        }
        else
        {
            // 形式3: 计数循环 for 5
            var loopCount = ParseExpression();
            Consume(TokenType.NEWLINE, "需要换行");
            var body = ParseStatementsUntil(TokenType.Next);
            Consume(TokenType.Next, "Expected 'next' after for loop body");

            return new ForStatement(null, null, null, loopCount, false, body)
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
            statements.Add(ParseStatement());
            if (Check(TokenType.NEWLINE)) Advance();
        }

        return statements;
    }

    private BreakStatement ParseBreakStatement()
    {
        var breakToken = Consume(TokenType.Break, "需要break语句");
        return new BreakStatement()
        {
            Line = breakToken.Line,
            Column = breakToken.Column
        };
    }

    private ContinueStatement ParseContinueStatement()
    {
        var breakToken = Consume(TokenType.Continue, "需要continue语句");
        return new ContinueStatement()
        {
            Line = breakToken.Line,
            Column = breakToken.Column
        };
    }

    private FunctionDefinitionStatement ParseFunctionDecl()
    {
        var functionToken = Consume(TokenType.Func, "Expected 'func'");
        var functionName = Consume(TokenType.Identifier, "Expected function name").Value;

        // Consume(TokenType.LeftParen, "Expected '(' after function name");

        // var parameters = new List<string>();
        // if (!Check(TokenType.RightParen))
        // {
        //     do
        //     {
        //         parameters.Add(Consume(TokenType.Identifier, "Expected parameter name").Value);
        //     } while (Match(TokenType.Comma));
        // }

        // Consume(TokenType.RightParen, "Expected ')' after parameters");
        Consume(TokenType.NEWLINE, "Expected newline after function signature");

        var body = new List<Statement>();
        while (!Check(TokenType.EndFunc) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }
            body.Add(ParseStatement());
            if (Check(TokenType.NEWLINE)) Advance();
        }

        Consume(TokenType.EndFunc, "Expected 'endfunc'");

        return new FunctionDefinitionStatement(functionName, null, body)
        {
            Line = functionToken.Line,
            Column = functionToken.Column
        };
    }

    private ReturnStatement ParseReturnStatement()
    {
        var returnToken = Consume(TokenType.Return, "Expected 'return'");
        var value = ParseExpression();

        return new ReturnStatement(value)
        {
            Line = returnToken.Line,
            Column = returnToken.Column
        };
    }

    private Statement ParseKeyStatement()
    {
        var keyToken = Consume(TokenType.ButtonKeyword, "未知的按键命令");

        while (!Check(TokenType.NEWLINE))
        {
            Advance();
        }
        return new KeyStatement(keyToken.Value)
        {
            Line = keyToken.Line,
            Column = keyToken.Column
        };
    }

    // 计算表达式
    private Expression ParseExpression()
    {
        return ParseAddition();
    }

    // 条件表达式
    private Expression ParseCondition()
    {
        return ParseLogicalOr();
    }

    private Expression ParseLogicalOr()
    {
        var expression = ParseLogicalAnd();

        while (Match(TokenType.LogicOr))
        {
            var op = _tokens[_position - 1].Type;
            var right = ParseLogicalAnd();
            expression = new BinaryExpression(expression, op, right);
        }

        return expression;
    }

    private Expression ParseLogicalAnd()
    {
        var expression = ParseComparison();

        while (Match(TokenType.LogicAnd))
        {
            var op = _tokens[_position - 1].Type;
            var right = ParseComparison();
            expression = new BinaryExpression(expression, op, right);
        }

        return expression;
    }

    private Expression ParseComparison()
    {
        var expression = ParseExpression();

        while (Match(TokenType.EqualEqual, TokenType.NotEqual, TokenType.LessThan, TokenType.LessThanEqual, TokenType.GreaterThan, TokenType.GreaterThanEqual))
        {
            var op = _tokens[_position - 1].Type;
            var right = ParseExpression();
            expression = new BinaryExpression(expression, op, right);
        }

        return expression;
    }

    private Expression ParseAddition()
    {
        var expression = ParseMultiplication();

        while (Match(TokenType.Plus, TokenType.Minus))
        {
            var op = _tokens[_position - 1].Type;
            var right = ParseMultiplication();
            expression = new BinaryExpression(expression, op, right);
        }

        return expression;
    }

    private Expression ParseMultiplication()
    {
        var expression = ParseUnary();

        while (Match(TokenType.Multiply, TokenType.Divide, TokenType.SlashI, TokenType.Modulus, TokenType.BitAnd, TokenType.BitXor))
        {
            var op = _tokens[_position - 1].Type;
            var right = ParseUnary();
            expression = new BinaryExpression(expression, op, right);
        }

        return expression;
    }

    private Expression ParseUnary()
    {
        if (Match(TokenType.BitNot, TokenType.Minus))
        {
            var op = _tokens[_position - 1].Type;
            var right = ParseUnary();
            return new BinaryExpression(new LiteralExpression(0), op, right);
        }

        return ParsePrimary();
    }

    private Expression ParsePrimary()
    {
        if (Match(TokenType.Integer))
        {
            var token = _tokens[_position - 1];
            try
            {
                var value = int.Parse(token.Value);
                if (value < short.MinValue || value > short.MaxValue)
                {
                    throw new Exception($"整数超出范围({short.MinValue} 到 {short.MaxValue}):L{token.Line}");
                }
                return new LiteralExpression(value)
                {
                    Line = token.Line,
                    Column = token.Column
                };
            }
            catch (FormatException)
            {
                throw new Exception($"错误的数组格式: {token.Value}:L{token.Line}");
            }
        }
        else if (Match(TokenType.Const, TokenType.Variable, TokenType.ExtVariable))
        {
            var token = _tokens[_position - 1];
            bool isConstant = token.Type == TokenType.Const;
            bool isSpecial = token.Type == TokenType.ExtVariable;
            return new VariableExpression(token.Value, isConstant, isSpecial)
            {
                Line = token.Line,
                Column = token.Column
            };
        }
        else if (Match(TokenType.String))
        {
            var token = _tokens[_position - 1];
            return new LiteralExpression(token.Value)
            {
                Line = token.Line,
                Column = token.Column
            };
        }

        throw new Exception($"未识别的语法{Peek().Type}:L{Peek().Line}");
    }
}

#endif