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
        // advance next non-comment token
        while(Current.Type == TokenType.COMMENT)
        {
            if(current.Line == Current.Line)
            {
                Console.WriteLine($"行{current.Line}存在注释：{Current.Value}");
                Console.WriteLine($"leading token: {current.Type}");
            }else{
                Console.WriteLine($"行{Current.Line}存在quan注释：{Current.Value}");
            }
            _position++;
        }
        return current;
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

        throw new Exception($"{message}:错误的 \"{Current.Value}\" 行{Peek()?.Line ?? 0}");
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

            var statement = ParseStatement();
            if (statement != null)
            {
                statements.Add(statement);
            }

            // 消耗语句后的换行符
            Consume(TokenType.NEWLINE, "未结束的语句");
        }

        return statements;
    }

    private Statement ParseStatement()
    {
        if (Check(TokenType.CONST) || Check(TokenType.VAR))
        {
            return ParseAssignment();
        }
        else if (Check(TokenType.IF))
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
        else if (Check(TokenType.FUNC))
        {
            return ParseFunctionDecl();
        }
        else if (Check(TokenType.ButtonKeyword))
        {
            return ParseKeyStatement();
        }
        else if (Check(TokenType.INT) || Check(TokenType.IDENT))
        {
            return ParseSpecIdentStatement();
        }
        else if (Check(TokenType.COMMENT))
        {
            Console.WriteLine($"行{Current.Line}存在quan注释：{Current.Value}");
            Advance();
            return null;
        }

        throw new Exception($"语法错误:{Current.Type}  行{Current.Line}");
    }

    private AssignmentStatement ParseAssignment()
    {
        var variableToken = Advance();
        string variableName = variableToken.Value;
        bool isConstant = variableToken.Type == TokenType.CONST;

        TokenType assignmentOp;
        if (Match(TokenType.ASSIGN, TokenType.ADD_ASSIGN, TokenType.SUB_ASSIGN,
                 TokenType.MUL_ASSIGN, TokenType.DIV_ASSIGN, TokenType.SlashIAssign,
                 TokenType.MOD_ASSIGN, TokenType.XOR_ASSIGN, TokenType.BitAnd_ASSIGN, TokenType.BitOr_ASSIGN,
                 TokenType.SHL_ASSIGN, TokenType.SHR_ASSIGN))
        {
            assignmentOp = Advance().Type;
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
        var func = Current;
        string name = func.Value;

        if (func.Type == TokenType.INT)
        {
            name = "WAIT";
        }
        else if(func.Value.ToLower() == "call")
        {
            Advance();
            func = Current;
            name = func.Value;
        }

        var args = new List<Expression>();

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
        var ifToken = Consume(TokenType.IF, "if语法不正确");
        var condition = ParseCondition();

        Consume(TokenType.NEWLINE, "if条件语法不正确");

        var thenBranch = new List<Statement>(); // ParseStatementsUntil(TokenType.ENDIF);
        while (!Check(TokenType.ELIF) && !Check(TokenType.ELSE) && !Check(TokenType.ENDIF) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }
            thenBranch.Add(ParseStatement());
            if (Check(TokenType.NEWLINE)) Advance();
        }

        // else if
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
                elifBranch.Add(ParseStatement());
                if (Check(TokenType.NEWLINE)) Advance();
            }
            Console.WriteLine($"{elifToken.Type} : {elifCond.Line}, {elifBranch.Count} ");
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
                elseBranch.Add(ParseStatement());
                if (Check(TokenType.NEWLINE)) Advance();
            }
            elseClause = new(elseBranch);
        }

        Consume(TokenType.ENDIF, "需要endif结尾");

        return new IfStatement(condition, thenBranch, null, elseClause)
        {
            Line = ifToken.Line,
            Column = ifToken.Column
        };
    }

    private ForStatement ParseForStatement()
    {
        var forToken = Consume(TokenType.FOR, "for语法不正确");

        if (Check(TokenType.NEWLINE))
        {
            // 形式1: 无限循环
            Consume(TokenType.NEWLINE, "需要换行");
            var body = ParseStatementsUntil(TokenType.NEXT);
            Consume(TokenType.NEXT, "for语句需要next结尾");

            return new ForStatement(null, null, null, null, true, body)
            {
                Line = forToken.Line,
                Column = forToken.Column
            };
        }

        if (Check(TokenType.VAR) && Peek(1)?.Type == TokenType.ASSIGN)
        {
            // 形式2: 范围循环 for $i = 1 to 10 step 2
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

            Consume(TokenType.NEWLINE, "需要换行");
            var body = ParseStatementsUntil(TokenType.NEXT);
            Consume(TokenType.NEXT, "for语法需要next结尾");

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
            var body = ParseStatementsUntil(TokenType.NEXT);
            Consume(TokenType.NEXT, "for语法需要next结尾");

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
        var breakToken = Consume(TokenType.BREAK, "需要break语句");
        return new BreakStatement()
        {
            Line = breakToken.Line,
            Column = breakToken.Column
        };
    }

    private ContinueStatement ParseContinueStatement()
    {
        var breakToken = Consume(TokenType.CONTINUE, "需要continue语句");
        return new ContinueStatement()
        {
            Line = breakToken.Line,
            Column = breakToken.Column
        };
    }

    private FunctionDefinitionStatement ParseFunctionDecl()
    {
        var functionToken = Consume(TokenType.FUNC, "Expected 'func'");
        var functionName = Consume(TokenType.IDENT, "Expected function name").Value;

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
        while (!Check(TokenType.ENDFUNC) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.NEWLINE))
            {
                Advance();
                continue;
            }
            body.Add(ParseStatement());
            if (Check(TokenType.NEWLINE)) Advance();
        }

        Consume(TokenType.ENDFUNC, "Expected 'endfunc'");

        return new FunctionDefinitionStatement(functionName, null, body)
        {
            Line = functionToken.Line,
            Column = functionToken.Column
        };
    }

    private ReturnStatement ParseReturnStatement()
    {
        var returnToken = Consume(TokenType.RETURN, "Expected 'return'");
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
        return ParseComparison();
        //return ParseLogicalOr();
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
            expression = new ConditionExpression(expression, op.Type, right)
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
            expression = new ConditionExpression(expression, op.Type, right)
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
                Console.WriteLine(ex.Message);
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

        throw new Exception($"未识别的语法：\"{Peek().Value}, {Peek().Type}\" 行{Peek().Line}");
    }
}

#endif