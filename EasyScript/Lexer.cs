using System.Text;
using System.Text.RegularExpressions;

namespace EasyScript;

internal partial class Lexer(string input)
{
    private readonly string _input = Compat(input);
    private int _position = 0;
    private int _line = 1;
    private int _column = 1;
    private readonly List<Token> _tokens = [];

    private static string Compat(string text)
    {
        var builder = new StringBuilder();
        foreach (var line in Regex.Split(text, "\r\n|\r|\n"))
        {
            var _text = line.TrimStart();
            var m = Regex.Match(_text, @"(\s*#.*)$");
            string comment = string.Empty;
            if (m.Success)
            {
                comment = m.Groups[1].Value;
                _text = _text[..^comment.Length];
            }
            _text = _text.Trim();

            var mp = printRex().Match(_text);
            if (mp.Success)
            {
                builder.Append($"PRINT \"{mp.Groups[1].Value}\"");
            }
            else
            {
                mp = alertRex().Match(_text);
                if (mp.Success)
                {
                    builder.Append($"ALERT \"{mp.Groups[1].Value}\"");
                }
                else
                {
                    builder.Append(_text);
                }
            }

            builder.Append(comment);
            builder.Append("\r\n");
        }
        return builder.ToString();
    }

    // 关键字字典
    private static readonly Dictionary<string, TokenType> keywords = new()
    {
            { "import", TokenType.Import },
            { "if", TokenType.If },
            { "elif", TokenType.Elif },
            { "else", TokenType.Else },
            { "endif", TokenType.Endif },
            { "for", TokenType.For },
            { "to", TokenType.To },
            { "step", TokenType.Step },
            { "break", TokenType.Break },
            { "continue", TokenType.Continue },
            { "next", TokenType.Next },

            { "func", TokenType.Func },
            { "endfunc", TokenType.EndFunc },
            { "return", TokenType.Return },

            { "true", TokenType.True },
            { "false", TokenType.False },
            { "reset", TokenType.ResetKeyword },
        };
    private static readonly Dictionary<string, TokenType> logicwords = new()
    {
            { "and", TokenType.LogicAnd },
            { "or", TokenType.LogicOr },
            { "not", TokenType.LogicNot },
        };

    // 按键关键字
    private static readonly List<string> gamepadKeywords = ["A", "B", "X", "Y", "L", "R", "ZL", "ZR",
        "MINUS", "PLUS", "LCLICK", "RCLICK", "HOME", "CAPTURE",
        "DUP", "DDOWN", "DLEFT", "DRIGHT",
        "LS", "RS",
        "UP", "DOWN", "LEFT", "RIGHT"];

    public List<Token> Tokenize()
    {
        SkipWhitespace();
        while (_position < _input.Length)
        {
            var current = Current;

            if (char.IsWhiteSpace(current))
            {
                if (current == '\n')
                {
                    AddToken(TokenType.NEWLINE, "");
                    _line++;
                    _column = 1;
                }
                Advance();
            }
            else if (current == '#')
            {
                ReadComment();
            }
            else if (char.IsDigit(current) || current == '.')
            {
                ReadNumber();
            }
            else if (current == '"')
            {
                ReadString();
            }
            else if (current == '_' || current == '$' || current == '@')
            {
                ReadVariable();
            }
            else if (IsIdentifierStart(current))
            {
                ReadIdentifier();
            }
            else
            {
                ReadOperatorOrSymbol();
            }
        }

        _tokens.Add(new Token(TokenType.EOF, "", _line, _column));
        return _tokens;
    }

    private char Current => Peek();

    private char Lookahead => Peek(1);

    private char Peek(int offset = 0)
    {
        var index = _position + offset;
        return index < _input.Length ? _input[index] : '\0';
    }
    private char Advance()
    {
        var current = Current;
        if (_position < _input.Length)
        {
            _position++;
            _column++;
        }
        return current;
    }

    private void AddToken(TokenType type, string value)
    {
        _tokens.Add(new Token(type, value, _line, _column - value.Length));
    }

    // 检查是否为标识符起始字符
    private bool IsIdentifierStart(char c)
    {
        return char.IsLetter(c) || c == '_' || IsChineseCharacter(c);
    }

    // 检查是否为标识符字符
    private bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_' || IsChineseCharacter(c);
    }

    // 简单的中文字符检查
    private bool IsChineseCharacter(char c)
    {
        // 中文字符的Unicode范围
        return (c >= '\u4E00' && c <= '\u9FFF') ||
               (c >= '\u3400' && c <= '\u4DBF') ||
               (c >= '\uF900' && c <= '\uFAFF');
    }

    // 跳过空白字符
    private void SkipWhitespace()
    {
        while (_position < _input.Length && char.IsWhiteSpace(Current))
        {
            if ("\u000D\u000A\u0085\u2028\u2029\r\n".IndexOf(Current) != -1)
            {
                if(Current == '\r' && Lookahead == '\n')
                    _line++;
            }
            _position++;
        }
    }

    private void ReadComment()
    {
        var start = _position;
        while (_position < _input.Length && (Current != '\n'))
        {
            Advance();
        }

        var comment = _input.Substring(start, _position - start);
        AddToken(TokenType.CommentTrivia, comment);
    }

    private void ReadNumber()
    {
        var start = _position;
        while (_position < _input.Length && (char.IsDigit(Current) || Current == '.'))
        {
            Advance();
        }

        var number = _input.Substring(start, _position - start);
        if (number.Contains('.'))
        {
            if (!double.TryParse(number, out _))
            {
                throw new Exception($"数字字面量格式不正确 在行：{_line}");
            }
            AddToken(TokenType.Number, number);
        }
        else
        {
            AddToken(TokenType.Integer, number);
        }
    }

    private void ReadString()
    {
        Advance();
        var sb = new StringBuilder();

        while (_position < _input.Length && Current != '"')
        {
            if (Current == '\n' || Current == '\r')
            {
                break;
            }
            sb.Append(Advance());
        }

        if (Current != '"')
        {
            throw new Exception($"字符串没有结束引号 在行：{_line}");
        }
        Advance(); // 跳过结束的引号
        AddToken(TokenType.String, sb.ToString());
    }

    private void ReadVariable()
    {
        var start = _position;
        var firstChar = Current;

        Advance();
        if (firstChar == '$' && Peek() == '$')
        {
            Advance();
        }

        while (_position < _input.Length && IsIdentifierChar(Peek()))
        {
            Advance();
        }

        var identifier = _input.Substring(start, _position - start);

        switch (firstChar)
        {
            case '_':
                AddToken(TokenType.Const, identifier);
                break;
            case '$':
                AddToken(TokenType.Variable, identifier);
                break;
            case '@':
                AddToken(TokenType.ExtVariable, identifier);
                break;
        }
    }

    private void ReadIdentifier()
    {
        var start = _position;
        while (_position < _input.Length && IsIdentifierChar(Current))
        {
            Advance();
        }

        var word = _input.Substring(start, _position - start);

        bool isAllUpper = word.All(char.IsUpper);
        bool isAllLower = word.All(char.IsLower);

        if ((isAllUpper || isAllLower) && keywords.ContainsKey(word.ToLower()))
        {
            // 关键字默认大写
            AddToken(keywords[word.ToLower()], word.ToUpper());
        }
        else if ((isAllUpper || isAllLower) && logicwords.ContainsKey(word.ToLower()))
        {
            // and, or, not关键字小写
            AddToken(logicwords[word.ToLower()], word.ToLower());
        }
        else if ((isAllUpper || isAllLower) && gamepadKeywords.Contains(word.ToUpper()))
        {
            // 手柄按键大写
            AddToken(TokenType.ButtonKeyword, word.ToUpper());
        }
        else
        {
            AddToken(TokenType.Identifier, word);
        }
    }

    private void ReadOperatorOrSymbol()
    {
        var current = Advance();
        var next = Peek();

        switch (current)
        {
            case '=':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.EqualEqual, "==");
                }
                else
                {
                    AddToken(TokenType.Assign, "=");
                }
                break;
            case '+':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.PlusAssign, "+=");
                }
                else
                {
                    AddToken(TokenType.Plus, "+");
                }
                break;
            case '-':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.MinusAssign, "-=");
                }
                else
                {
                    AddToken(TokenType.Minus, "-");
                }
                break;
            case '*':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.MultiplyAssign, "*=");
                }
                else
                {
                    AddToken(TokenType.Multiply, "*");
                }
                break;
            case '/':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.DivideAssign, "/=");
                }
                else
                {
                    AddToken(TokenType.Divide, "/");
                }
                break;
            case '\\':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.SlashIAssign, "\\=");
                }
                else
                {
                    AddToken(TokenType.SlashI, "\\");
                }
                break;
            case '%':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.ModulusAssign, "%=");
                }
                else
                {
                    AddToken(TokenType.Modulus, "%");
                }
                break;
            case '^':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.BitXorAssign, "^=");
                }
                else
                {
                    AddToken(TokenType.BitXor, "^");
                }
                break;
            case '&':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.BitAndAssign, "&=");
                }
                else
                {
                    AddToken(TokenType.BitAnd, "&");
                }
                break;
            case '|':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.BitOrAssign, "|=");
                }
                else
                {
                    AddToken(TokenType.BitOr, "|");
                }
                break;
            case '!':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.NotEqual, "!=");
                }
                break;
            case '>':
                if (next == '>')
                {
                    Advance();
                    if (_position < _input.Length && _input[_position] == '=')
                    {
                        Advance();
                        AddToken(TokenType.LeftShiftAssign, ">>=");
                    }
                    else
                    {
                        AddToken(TokenType.LeftShiftAssign, ">>");
                    }
                }
                else if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.LessThanEqual, ">=");
                }
                else
                {
                    AddToken(TokenType.LessThan, ">");
                }
                break;
            case '<':
                if (next == '<')
                {
                    Advance();
                    if (_position < _input.Length && _input[_position] == '=')
                    {
                        Advance();
                        AddToken(TokenType.LeftShiftAssign, "<<=");
                    }
                    else
                    {
                        AddToken(TokenType.LeftShiftAssign, "<<");
                    }
                }
                else if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.LessThanEqual, "<=");
                }
                else
                {
                    AddToken(TokenType.LessThan, "<");
                }
                break;
            case '~':
                AddToken(TokenType.BitNot, "~");
                break;
            case ',':
                AddToken(TokenType.Comma, ",");
                break;
            case '(':
                AddToken(TokenType.LeftParen, "(");
                break;
            case ')':
                AddToken(TokenType.RightParen, ")");
                break;
            case '[':
                AddToken(TokenType.LeftBracket, "[");
                break;
            case ']':
                AddToken(TokenType.RightBracket, "]");
                break;
            default:
                throw new Exception($"无法识别的字符：'{current}' 在行：{_line}");
        }
    }

    [GeneratedRegex(@"^print\s+(.*)$", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex printRex();


    [GeneratedRegex(@"^alert\s+(.*)$", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex alertRex();
}
