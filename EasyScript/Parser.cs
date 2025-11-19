using EasyScript.Parsing;
using EasyScript.Statements;
using System.Linq.Expressions;

#if false

namespace EasyScript;

internal class Parser(Lexer lexer)
{
    private readonly List<Token> _tokens = lexer.Tokenize();
    private int _position = 0;
    private readonly Stack<int> _indentationStack = new Stack<int>();

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

        throw new Exception($"{message} at line {Peek()?.Line ?? 0}"); // column {Peek()?.Column ?? 0}
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
        //else if (Check(TokenType.If))
        //{
        //    return ParseIfStatement();
        //}
        //else if (Check(TokenType.For))
        //{
        //    return ParseForStatement();
        //}
        //else if (Check(TokenType.Func))
        //{
        //    return ParseFunctionDefinition();
        //}
        //else if (Check(TokenType.ButtonKeyword))
        //{
        //    return ParseKeyStatement();
        //}
        else if (Check(TokenType.Integer))
        {
            return ParseSpecIdentStatement();
        }
        else if (Check(TokenType.Identifier))
        {
            return ParseSpecIdentStatement();
        }
        throw new Exception($"Unexpected token {Peek().Type} at line {Peek().Line}, column {Peek().Column}");
    }

    private Statement ParseAssignment()
    {
        var variableToken = Advance();
        string variableName = variableToken.Value;
        bool isConstant = variableToken.Type == TokenType.Const;

        TokenType assignmentType;
        if (Match(TokenType.Assign, TokenType.PlusAssign, TokenType.MinusAssign,
                 TokenType.MultiplyAssign, TokenType.DivideAssign, TokenType.SlashIAssign,
                 TokenType.ModulusAssign, TokenType.BitXorAssign, TokenType.BitAndAssign, TokenType.BitOrAssign,
                 TokenType.LeftShiftAssign, TokenType.RightShiftAssign))
        {
            assignmentType = _tokens[_position - 1].Type;
        }
        else
        {
            throw new Exception($"Expected assignment operator at line {Peek().Line}");
        }

        var value = ParseExpression();

        if (isConstant)
        {
            return new Empty($"{variableName} = {value}");
        }
        else
        {
            return new Expr(variableToken, assignmentType, value);
        }
    }

    private Statement ParseSpecIdentStatement()
    {
        if(Current.Type == TokenType.Integer)
        {
            Advance();
            return new Wait(int.Parse(Current.Value), true);
        }
        return null;
    }

#if false
    private IfStatement ParseIfStatement()
    {
        var ifToken = Consume(TokenType.If, "Expected 'if'");
        var condition = ParseExpression();

        Consume(TokenType.LineBreakTrivia, "Expected newline after if condition");

        var thenBranch = new List<Statement>();
        while (!Check(TokenType.Elif) && !Check(TokenType.Endif) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.LineBreakTrivia))
            {
                Advance();
                continue;
            }
            thenBranch.Add(ParseStatement());
            if (Check(TokenType.LineBreakTrivia)) Advance();
        }

        Consume(TokenType.LineBreakTrivia, "Expected newline");

        var elseBranch = new List<Statement>();
        while (!Check(TokenType.Endif) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.LineBreakTrivia))
            {
                Advance();
                continue;
            }
            elseBranch.Add(ParseStatement());
            if (Check(TokenType.LineBreakTrivia)) Advance();
        }

        Consume(TokenType.Endif, "Expected 'endif'");

        return new IfStatement(condition, thenBranch, elseBranch)
        {
            Line = ifToken.Line,
            Column = ifToken.Column
        };
    }

    private ForStatement ParseForStatement()
    {
        var forToken = Consume(TokenType.For, "Expected 'for'");

        if (Check(TokenType.LineBreakTrivia))
        {
            Consume(TokenType.LineBreakTrivia, "Expected newline after 'for'");
            var body = ParseStatementsUntil(TokenType.Next);
            Consume(TokenType.Next, "Expected 'next' after for loop body");

            return new ForStatement(null, null, null, null, null, true, false, body)
            {
                Line = forToken.Line,
                Column = forToken.Column
            };
        }

        if (Check(TokenType.Variable) && Peek(1)?.Type == TokenType.Assign)
        {
            var loopVar = Consume(TokenType.Variable, "Expected loop variable").Value;
            Consume(TokenType.Assign, "Expected '=' after loop variable");
            var start = ParseExpression();
            Consume(TokenType.To, "Expected 'to' after start value");
            var end = ParseExpression();

            Expression step = null;
            if (Match(TokenType.Step))
            {
                step = ParseExpression();
            }

            Consume(TokenType.LineBreakTrivia, "Expected newline after for loop header");
            var body = ParseStatementsUntil(TokenType.Next);
            Consume(TokenType.Next, "Expected 'next' after for loop body");

            return new ForStatement(loopVar, start, end, step, null, false, true, body)
            {
                Line = forToken.Line,
                Column = forToken.Column
            };
        }
        else
        {
            var loopCount = ParseExpression();
            Consume(TokenType.LineBreakTrivia, "Expected newline after for loop count");
            var body = ParseStatementsUntil(TokenType.Next);
            Consume(TokenType.Next, "Expected 'next' after for loop body");

            return new ForStatement(null, null, null, null, loopCount, false, false, body)
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
            if (Check(TokenType.LineBreakTrivia))
            {
                Advance();
                continue;
            }
            statements.Add(ParseStatement());
            if (Check(TokenType.LineBreakTrivia)) Advance();
        }

        return statements;
    }

    private BreakStatement ParseBreakStatement()
    {
        var breakToken = Consume(TokenType.Break, "Expected 'break'");
        return new BreakStatement()
        {
            Line = breakToken.Line,
            Column = breakToken.Column
        };
    }

    private FunctionDefinitionStatement ParseFunctionDefinition()
    {
        var functionToken = Consume(TokenType.Func, "Expected 'func'");
        var functionName = Consume(TokenType.Identifier, "Expected function name").Value;

        Consume(TokenType.LeftParen, "Expected '(' after function name");

        var parameters = new List<string>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                parameters.Add(Consume(TokenType.Identifier, "Expected parameter name").Value);
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightParen, "Expected ')' after parameters");
        Consume(TokenType.LineBreakTrivia, "Expected newline after function signature");

        var body = new List<Statement>();
        while (!Check(TokenType.EndFunc) && !Check(TokenType.EOF))
        {
            if (Check(TokenType.LineBreakTrivia))
            {
                Advance();
                continue;
            }
            body.Add(ParseStatement());
            if (Check(TokenType.LineBreakTrivia)) Advance();
        }

        Consume(TokenType.EndFunc, "Expected 'endfunc'");

        return new FunctionDefinitionStatement(functionName, parameters, body)
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

    private Expression ParseArray()
    {
        var elements = new List<Expression>();

        if (!Check(TokenType.RightBracket))
        {
            do
            {
                elements.Add(ParseExpression());
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightBracket, "Expected ']' after array elements");

        return new ArrayExpression(elements);
    }

    private Expression ParseFunctionCall(string functionName)
    {
        Consume(TokenType.LeftParen, "Expected '(' after function name");

        var arguments = new List<Expression>();
        if (!Check(TokenType.LeftParen))
        {
            do
            {
                arguments.Add(ParseExpression());
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightParen, "Expected ')' after function arguments");

        return new FunctionCallExpression(functionName, arguments);
    }
#endif

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

    private Expression ParseLogicalNot()
    {
        Match(TokenType.LogicNot);
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
        var expression = ParseAddition();

        while (Match(TokenType.EqualEqual, TokenType.NotEqual, TokenType.LessThan, TokenType.LessThanEqual, TokenType.GreaterThan, TokenType.GreaterThanEqual))
        {
            var op = _tokens[_position - 1].Type;
            var right = ParseAddition();
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
                    throw new Exception($"整数超出范围 ({short.MinValue} 到 {short.MaxValue}) at line {token.Line}, column {token.Column}");
                }
                return new LiteralExpression(value)
                {
                    Line = token.Line,
                    Column = token.Column
                };
            }
            catch (FormatException)
            {
                throw new Exception($"Invalid integer format: {token.Value} at line {token.Line}, column {token.Column}");
            }
        }
        else if (Match(TokenType.Number))
        {
            var token = _tokens[_position - 1];
            try
            {
                var value = double.Parse(token.Value);
                // 验证小数位数
                var decimalPart = Math.Abs(value - Math.Truncate(value));
                var decimalDigits = decimalPart.ToString("F10").TrimEnd('0').Split('.').Length > 1 ?
                    decimalPart.ToString("F10").TrimEnd('0').Split('.')[1].Length : 0;
                if (decimalDigits > 2)
                {
                    throw new Exception($"小数只支持两位小数 at line {token.Line}, column {token.Column}");
                }
                return new LiteralExpression(value)
                {
                    Line = token.Line,
                    Column = token.Column
                };
            }
            catch (FormatException)
            {
                throw new Exception($"Invalid decimal format: {token.Value} at line {token.Line}, column {token.Column}");
            }
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
        //else if (Match(TokenType.LeftBracket))
        //{
        //    return ParseArray();
        //}
        else if (Match(TokenType.Identifier))
        {
            var identifier = _tokens[_position - 1];

            if (Check(TokenType.LeftParen))
            {
                return ParseFunctionCall(identifier.Value);
            }
            else
            {
                return new VariableExpression(identifier.Value, false, false)
                {
                    Line = identifier.Line,
                    Column = identifier.Column
                };
            }
        }
        else if (Match(TokenType.LeftParen))
        {
            var expression = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after expression");
            return expression;
        }

        throw new Exception($"Unexpected token {Peek().Type} in expression at line {Peek().Line}");
    }
}

#endif