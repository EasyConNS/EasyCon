using EasyCon.Script2.Text;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyCon.Script2.Syntax;

internal sealed partial class Lexer(SyntaxTree syntaxTree)
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly SyntaxTree _syntaxTree = syntaxTree;
    private readonly SourceText _text = syntaxTree.Text;

    private readonly string _input = Compat(syntaxTree.Text.ToString());
    private int _position = 0;
    private int _line = 1;
    private int _column = 1;
    private readonly List<Token> _tokens = [];

    public DiagnosticBag Diagnostics => _diagnostics;

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
            { "import", TokenType.IMPORT },
            { "if", TokenType.IF },
            { "elif", TokenType.ELIF },
            { "else", TokenType.ELSE },
            { "endif", TokenType.ENDIF },
            { "for", TokenType.FOR },
            { "to", TokenType.TO },
            { "step", TokenType.STEP },
            { "break", TokenType.BREAK },
            { "continue", TokenType.CONTINUE },
            { "next", TokenType.NEXT },

            { "func", TokenType.FUNC },
            { "endfunc", TokenType.ENDFUNC },
            { "return", TokenType.RETURN },

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
                    AddToken(TokenType.NEWLINE, " ");
                    _line++;
                    _column = 0;
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
            if ("\u000D\u000A\u0085\u2028\u2029\r\n".Contains(Current))
            {
                if(Current == '\r' && Lookahead == '\n')
                    _line++;
            }
            Advance();
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
        AddToken(TokenType.COMMENT, comment);
    }

    private void ReadNumber()
    {
        var start = _position;
        while (_position < _input.Length && (char.IsDigit(Current) || Current == '.'))
        {
            Advance();
        }

        var length = _position - start;
        var number = _input.Substring(start, length);
        if (number.Contains('.'))
        {
            if (!double.TryParse(number, out _))
            {
                var span = new SourceSpan(start, length, _column);
                var location = new TextLocation(_text, span);
                _diagnostics.ReportInvalidNumber(location, number);
            }
            AddToken(TokenType.Number, number);
        }
        else
        {
            AddToken(TokenType.INT, number);
        }
    }

    private void ReadString()
    {
        var start = _position;
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
            var span = new SourceSpan(start, sb.ToString().Length, _column);
            var location = new TextLocation(_text, span);
            _diagnostics.ReportUnterminatedString(location);
        }
        Advance(); // 跳过结束的引号
        AddToken(TokenType.STRING, sb.ToString());
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
                AddToken(TokenType.CONST, identifier);
                break;
            case '$':
                AddToken(TokenType.VAR, identifier);
                break;
            case '@':
                AddToken(TokenType.EX_VAR, identifier);
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
            var ktype = TokenType.ButtonKeyword;
            switch(word.ToUpper())
            {
                case "LS":
                case "RS":
                    ktype = TokenType.StickKeyword;
                    break;
            }
            // 手柄按键大写
            AddToken(ktype, word.ToUpper());
        }
        else
        {
            AddToken(TokenType.IDENT, word);
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
                    AddToken(TokenType.EQL, "==");
                }
                else
                {
                    AddToken(TokenType.ASSIGN, "=");
                }
                break;
            case '+':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.ADD_ASSIGN, "+=");
                }
                else
                {
                    AddToken(TokenType.ADD, "+");
                }
                break;
            case '-':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.SUB_ASSIGN, "-=");
                }
                else
                {
                    AddToken(TokenType.SUB, "-");
                }
                break;
            case '*':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.MUL_ASSIGN, "*=");
                }
                else
                {
                    AddToken(TokenType.MUL, "*");
                }
                break;
            case '/':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.DIV_ASSIGN, "/=");
                }
                else
                {
                    AddToken(TokenType.DIV, "/");
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
                    AddToken(TokenType.MOD_ASSIGN, "%=");
                }
                else
                {
                    AddToken(TokenType.MOD, "%");
                }
                break;
            case '^':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.XOR_ASSIGN, "^=");
                }
                else
                {
                    AddToken(TokenType.XOR, "^");
                }
                break;
            case '&':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.BitAnd_ASSIGN, "&=");
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
                    AddToken(TokenType.BitOr_ASSIGN, "|=");
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
                    AddToken(TokenType.NEQ, "!=");
                }
                break;
            case '>':
                if (next == '>')
                {
                    Advance();
                    if (_position < _input.Length && _input[_position] == '=')
                    {
                        Advance();
                        AddToken(TokenType.SHR_ASSIGN, ">>=");
                    }
                    else
                    {
                        AddToken(TokenType.SHR, ">>");
                    }
                }
                else if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.GEQ, ">=");
                }
                else
                {
                    AddToken(TokenType.GTR, ">");
                }
                break;
            case '<':
                if (next == '<')
                {
                    Advance();
                    if (_position < _input.Length && _input[_position] == '=')
                    {
                        Advance();
                        AddToken(TokenType.SHL_ASSIGN, "<<=");
                    }
                    else
                    {
                        AddToken(TokenType.SHL, "<<");
                    }
                }
                else if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.LEQ, "<=");
                }
                else
                {
                    AddToken(TokenType.LESS, "<");
                }
                break;
            case '~':
                AddToken(TokenType.BitNot, "~");
                break;
            case ',':
                AddToken(TokenType.COMMA, ",");
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
                var span = new SourceSpan(_position, 1, _line);
                var location = new TextLocation(_text, span);
                _diagnostics.ReportBadCharacter(location, current);
                break;
        }
    }

    [GeneratedRegex(@"^print\s+(.*)$", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex printRex();


    [GeneratedRegex(@"^alert\s+(.*)$", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex alertRex();
}
 