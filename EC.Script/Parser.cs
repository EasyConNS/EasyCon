namespace EC.Script.Syntax;
#if false
// 定义语法分析器类
public class Parser
{
    private Lexer lexer;
    private Token currentToken;

    public Parser(Lexer lexer)
    {
        this.lexer = lexer;
        currentToken = lexer.GetNextToken();
    }

    // 推进到下一个Token
    private void Advance()
    {
        currentToken = lexer.GetNextToken();
    }

    // 匹配当前Token类型
    private void Match(TokenType type)
    {
        if (currentToken.Type == type)
        {
            Advance();
        }
        else
        {
            throw new Exception($"Unexpected token: {currentToken.Value} at line {currentToken.LineNumber}");
        }
    }

    // 解析语句
    public void Parse()
    {
        while (currentToken.Type != TokenType.EOF)
        {
            int currentLine = currentToken.LineNumber;
            ParseStatementOnSameLine(currentLine);
        }
    }

    // 解析同一行的语句
    private void ParseStatementOnSameLine(int lineNumber)
    {
        Console.WriteLine("Parsing statements on line " + lineNumber);
        while (currentToken.Type != TokenType.EOF && currentToken.LineNumber == lineNumber)
        {
            ParseStatement();
        }
    }

    // 解析单个语句
    private void ParseStatement()
    {
        if (currentToken.Type == TokenType.Const || currentToken.Type == TokenType.Variable || currentToken.Type == TokenType.ExtVariable)
        {
            ParseAssignment();
        }
        else if (currentToken.Type == TokenType.If)
        {
            ParseIfStatement();
        }
        else if (currentToken.Type == TokenType.Elif)
        {
            ParseElifStatement();
        }
        else if (currentToken.Type == TokenType.Else)
        {
            ParseElseStatement();
        }
        else if (currentToken.Type == TokenType.Endif)
        {
            ParseEndIfStatement();
        }
        else if (currentToken.Type == TokenType.For)
        {
            ParseForStatement();
        }
        else
        {
            throw new Exception($"Unexpected token: {currentToken.Value} at line {currentToken.LineNumber}");
        }
    }

    // 解析赋值语句
    private void ParseAssignment()
    {
        Token variable = currentToken;
        Advance();
        if (IsAssignOperator(currentToken.Type))
        {
            Advance();
            ParseExpression();
            Console.WriteLine($"Assignment: {variable.Value}");
        }
    }

    private bool IsAssignOperator(TokenType type)
    {
        return type == TokenType.Assign || type == TokenType.PlusAssign || type == TokenType.MinusAssign || type == TokenType.MultiplyAssign || type == TokenType.DivideAssign || type == TokenType.SlashIAssign || type == TokenType.ModulusAssign || type == TokenType.BitwiseAndAssign || type == TokenType.BitwiseOrAssign ||type == TokenType.BitwiseXorAssign || type == TokenType.LeftShiftAssign || type == TokenType.RightShiftAssign;
    }

    // 解析条件判断语句
    private void ParseIfStatement()
    {
        Match(TokenType.If);
        ParseConditionExpression();
        Console.WriteLine("If statement");
    }

    private void ParseElseStatement()
    {
        Match(TokenType.Else);
        Console.WriteLine("Else statement");
    }

        private void ParseElifStatement()
    {
        Match(TokenType.Elif);
        Console.WriteLine("ElseIf statement");
    }
    

    private void ParseEndIfStatement()
    {
        Match(TokenType.Endif);
        Console.WriteLine("End if statement");
    }


    private void ParseForStatement()
    {
        Match(TokenType.For);
        Console.WriteLine("For statement");
    }

    // 解析判断表达式
    private void ParseConditionExpression()
    {
        if (currentToken.Type == TokenType.LogicNot)
        {
            Match(TokenType.LogicNot);
        }
        ParseComparisonExpression();
    }

    // 解析表达式
    private void ParseExpression()
    {
        ParseTerm();
        if (IsArithmeticOperator(currentToken.Type))
        {
            ParseOperator();
            ParseTerm();
        }
    }

    // 解析比较表达式
    private void ParseComparisonExpression()
    {
        ParseTerm();
        if (IsComparisonOperator(currentToken.Type))
        {
            ParseOperator();
            ParseTerm();
            if (IsLogicalOperator(currentToken.Type))
            {
                ParseLogicalOperator();
                ParseComparisonExpression();
            }
        }
    }

    // 解析逻辑操作符
    private void ParseLogicalOperator()
    {
        if (IsLogicalOperator(currentToken.Type))
        {
            Advance();
        }
        else
        {
            throw new Exception($"Unexpected token: {currentToken.Value} at line {currentToken.LineNumber}");
        }
    }

    // 判断是否为算术操作符
    private bool IsArithmeticOperator(TokenType type)
    {
        return type == TokenType.Plus || type == TokenType.Minus || type == TokenType.Multiply || type == TokenType.Divide || type == TokenType.SlashI || type == TokenType.Modulus;
    }

    // 判断是否为比较操作符
    private bool IsComparisonOperator(TokenType type)
    {
        return type == TokenType.GreaterThan || type == TokenType.LessThan || type == TokenType.EqualEqual || type == TokenType.NotEqual || type == TokenType.GreaterThanEqual || type == TokenType.LessThanEqual;
    }

    // 判断是否为逻辑操作符
    private bool IsLogicalOperator(TokenType type)
    {
        return type == TokenType.LogicAnd || type == TokenType.LogicOr || type == TokenType.LogicNot;
    }

    // 解析操作符
    private void ParseOperator()
    {
        if (IsArithmeticOperator(currentToken.Type) || IsComparisonOperator(currentToken.Type))
        {
            Advance();
        }
        else
        {
            throw new Exception($"Unexpected token: {currentToken.Value} at line {currentToken.LineNumber}");
        }
    }

    // 解析项
    private void ParseTerm()
    {
        if (currentToken.Type == TokenType.Number || currentToken.Type == TokenType.Const || currentToken.Type == TokenType.Variable || currentToken.Type == TokenType.ExtVariable)
        {
            Advance();
        }
        else
        {
            throw new Exception($"Unexpected token: {currentToken.Value} at line {currentToken.LineNumber}");
        }
    }
}
#endif