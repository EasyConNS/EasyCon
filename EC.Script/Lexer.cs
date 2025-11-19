namespace EC.Script.Syntax;

// 定义词法分析器类
partial class Lexer
{
    private string input;     private int position;
    private int lineNumber = 1;
    
    private static readonly List<string> keywords = ["IMPORT", "IF", "ELIF", "ELSE", "ENDIF", "FOR", "TO", "STEP", "BREAK", "CONTINUE", "NEXT", "FUNC", "RETURN", "ENDFUNC", "AND", "OR", "NOT"];

    private static readonly List<string> gamepadKeys = ["A", "B", "X", "Y", "L", "R", "ZL", "ZR", "LCLICK", "RCLICK",
        "DUP", "DDOWN", "DLEFT", "DRIGHT",
        "PLUS", "MINUS", "HOME", "CAPTURE",
        "LS", "RS",
        "UP", "DOWN", "LEFT", "RIGHT",
        "RESET"];

    //public Lexer(string input)
    //{
    //    this.input = input;
    //    this.position = 0;
    //}

    // 获取下一个Token
    public Token GetNextToken()
    {
        SkipWhitespace();

        if (position >= input.Length)
        {
            return new Token(TokenType.EOF, "", lineNumber);
        }

        char currentChar = input[position];

        // 处理注释
        if (currentChar == '#')
        {
            return ParseComment();
        }
        else if (char.IsLetter(currentChar))
        {
            return ParseIdentifier();
        }
        else if (currentChar == '_' || currentChar == '$' || currentChar == '@')
        {
            return ParseVariable();
        }
        else if (char.IsDigit(currentChar))
        {
            return ParseNumber();
        }
        else if (IsOperator(currentChar))
        {
            return ParseOperator();
        }
        else if (IsPunctuation(currentChar))
        {
            return ParsePunctuation();
        }
        else if (currentChar == '"')
        {
            return ParseString();
        }
        else
        {
            throw new Exception($"Unexpected character: {currentChar}");
        }
    }

    // 解析注释
    private Token ParseComment()
    {
        int start = position;
        while (position < input.Length && input[position] != '\n')
        {
            position++;
        }
        string value = input.Substring(start, position - start);
        return new Token(TokenType.CommentTrivia, value, lineNumber);
    }

    // 跳过空白字符
    private void SkipWhitespace()
    {
        while (position < input.Length && char.IsWhiteSpace(input[position]))
        {
            if ("\u000D\u000A\u0085\u2028\u2029\r\n".IndexOf(input[position]) != -1)
            {
                lineNumber++;
            }
            position++;
        }
    }

    // 解析标识符
    private Token ParseIdentifier()
    {
        int start = position;
        while (position < input.Length && (char.IsLetterOrDigit(input[position]) || input[position] == '_'))
        {
            position++;
        }
        string value = input.Substring(start, position - start);
        // 检查是否全大写或者全小写
        bool isAllUpper = value.All(char.IsUpper);
        bool isAllLower = value.All(char.IsLower);
        // 检查是否为关键字，忽略大小写
        string lowerCaseValue = value.ToLower();
        if ((isAllUpper || isAllLower) && keywords.Contains(lowerCaseValue))
        {
            // 识别准确的关键字 Token 类型
            var keywordTokenType = GetKeywordTokenType(lowerCaseValue);
            return new Token(keywordTokenType, value, lineNumber);
        }
        if (gamepadKeys.Contains(value))
        {
            return new Token(TokenType.ButtonKeyword, value, lineNumber);
        }
        return new Token(TokenType.Identifier, value, lineNumber);
    }
    // 获取关键字对应的 Token 类型
    private TokenType GetKeywordTokenType(string keyword)
    {
        switch (keyword)
        {
            case "import":
                return TokenType.Import;
            case "if":
                return TokenType.If;
            case "elif":
                return TokenType.Elif;
            case "else":
                return TokenType.Else;
            case "endif":
                return TokenType.Endif;
            case "for":
                return TokenType.For;
            case "to":
                return TokenType.To;
            case "step":
                return TokenType.Step;
            case "break":
                return TokenType.Break;
            case "continue":
                return TokenType.Continue;
            case "next":
                return TokenType.Next;
            case "func":
                return TokenType.Func;
            case "return":
                return TokenType.Return;
            case "endfunc":
                return TokenType.EndFunc;
            case "and":
                return TokenType.LogicAnd;
            case "or":
                return TokenType.LogicOr;
            case "not":
                return TokenType.LogicNot;
            default:
                throw new Exception($"Unexpected keyword: {keyword}");
        }
    }

    private Token ParseVariable()
    {
        int start = position++;
        if (input[start] == '$' && input[position] == '$') {
            position++;
        }
        while (position < input.Length && (char.IsLetterOrDigit(input[position]) || input[position] == '_' || char.IsLetter(input[position])))
        {
            position++;
        }
        string value = input.Substring(start, position - start);

        switch (value[0]) {
            case '_':
                return new Token(TokenType.Const, value, lineNumber);
            case '$':
                return new Token(TokenType.Variable, value, lineNumber);
            case '@':
                return new Token(TokenType.ExtVariable, value, lineNumber);
            default:
                throw new Exception($"Unexpected character: {value[0]}");
        }
    }

    // 解析数字
    private Token ParseNumber()
    {
        int start = position;
        while (position < input.Length && char.IsDigit(input[position]))
        {
            position++;
        }
        string value = input.Substring(start, position - start);
        return new Token(TokenType.Number, value, lineNumber);
    }

    private Token ParseString()
    {
        int start = position++;
        while (position < input.Length && input[position] != '"')
        {
            position++;
        }
        string value = input.Substring(start, position - start + 1);
        position++;
        return new Token(TokenType.String, value, lineNumber);
    }

    // 解析运算符
    private Token ParseOperator()
    {
        switch (input[position]) {
            case '+':
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.PlusAssign, "+=", lineNumber);
                }
                position++;
                return new Token(TokenType.Plus, "+", lineNumber);
            case '-':
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.MinusAssign, "-=", lineNumber);
                }
                position++;
                return new Token(TokenType.Minus, "-", lineNumber);
            case '*':
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.MultiplyAssign, "*=", lineNumber);
                }
                position++;
                return new Token(TokenType.Multiply, "*", lineNumber);
            case '/':
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.DivideAssign, "/=", lineNumber);
                }
                position++;
                return new Token(TokenType.Divide, "/", lineNumber);
            case '\\':
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.SlashIAssign, "\\=", lineNumber);
                }
                position++;
                return new Token(TokenType.SlashI, "\\", lineNumber);
            case '%':
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.ModulusAssign, "%=", lineNumber);
                }
                position++;
                return new Token(TokenType.Modulus, "%", lineNumber);
            case '&':
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.BitAndAssign, "&=", lineNumber);
                }
                position++;
                return new Token(TokenType.BitAnd, "&", lineNumber);
            case '|':
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.BitOrAssign, "|=", lineNumber);
                }
                position++;
                return new Token(TokenType.BitOr, "|", lineNumber);
            case '^':
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.BitXorAssign, "^=", lineNumber);
                }
                position++;
                return new Token(TokenType.BitXor, "^", lineNumber);
            case '=':
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.EqualEqual, "==", lineNumber);
                }
                position++;
                return new Token(TokenType.Assign, "=", lineNumber);
            case '!':
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.NotEqual, "!=", lineNumber);
                }
                position++;
                return new Token(TokenType.LogicNot, "!", lineNumber);
            case '<':
                if (position + 1 < input.Length && input[position + 1] == '<') {
                    if (position + 2 < input.Length && input[position + 2] == '=') {
                        position += 3;
                        return new Token(TokenType.LeftShiftAssign, "<<=", lineNumber);
                    }
                    position += 2;
                    return new Token(TokenType.LeftShift, "<<", lineNumber);
                }
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.LessThanEqual, "<=", lineNumber);
                }
                position++;
                return new Token(TokenType.LessThan, "<", lineNumber);
            case '>':
                if (position + 1 < input.Length && input[position + 1] == '>') {
                    if (position + 2 < input.Length && input[position + 2] == '=') {
                        position += 3;
                        return new Token(TokenType.RightShiftAssign, ">>=", lineNumber);
                    }
                    position += 2;
                    return new Token(TokenType.RightShift, ">>", lineNumber);
                }
                if (position + 1 < input.Length && input[position + 1] == '=') {
                    position += 2;
                    return new Token(TokenType.GreaterThanEqual, ">=", lineNumber);
                }
                position++;
                return new Token(TokenType.GreaterThan, ">", lineNumber);
            case '~':
                position++;
                return new Token(TokenType.BitNot, "~", lineNumber);
            default:
                throw new Exception($"Unexpected character: {input[position]}");
        }
    }

    // 解析标点符号
    private Token ParsePunctuation()
    {
        string value = input[position].ToString();
        position++;
        return new Token(TokenType.Punctuation, value, lineNumber);
    }

    // 判断是否为运算符
    private bool IsOperator(char c)
    {
        return "+-*/\\=<>~%&|^!".IndexOf(c) != -1;
    }

    // 判断是否为标点符号
    private bool IsPunctuation(char c)
    {
        return "()[]{},.;:".IndexOf(c) != -1;
    }
}
