using EasyCon.Script2.Text;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyCon.Script2.Syntax;

internal sealed partial class Lexer(SyntaxTree syntaxTree)
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly SyntaxTree _syntaxTree = syntaxTree;
    private readonly SourceText _text = syntaxTree.Text;

    private readonly string _input = Compat(syntaxTree.Text.Lines);
    private int _position = 0;
    private int _line = 1;
    private int _column = 1;
    private readonly List<Token> _tokens = [];

    private ImmutableArray<Token>.Builder _triviaBuilder = ImmutableArray.CreateBuilder<Token>();

    public DiagnosticBag Diagnostics => _diagnostics;

    private static string Compat(ImmutableArray<TextLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var _line = line.Text;
            var m = Regex.Match(_line, @"(\s*#.*)$");
            string comment = string.Empty;
            if (m.Success)
            {
                comment = m.Groups[1].Value;
                _line = _line[..^comment.Length];
            }

            var mp = printRex().Match(_line);
            if (mp.Success)
            {
                builder.Append($"PRINT \"{mp.Groups[1].Value}\"");
            }
            else
            {
                mp = alertRex().Match(_line);
                if (mp.Success)
                {
                    builder.Append($"ALERT \"{mp.Groups[1].Value}\"");
                }
                else
                {
                    builder.Append(_line);
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
        while (_position < _input.Length)
        {
            SkipWhitespace();
            var current = Current;
            if (current == '\0') break;
            if (current == '#')
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

        _tokens.Add(new Token(_text, TokenType.EOF, "", 0, 0));
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

    private void AddToken(TokenType type, string value, int start)
    {
        _tokens.Add(new Token(_text, type, value, _line, start));
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
                {
                    AddToken(TokenType.NEWLINE, " ", _position);
                    _position++;
                    _line++;
                    _column = 0;
                }
            }
            Advance();
        }
    }

    private void ReadComment()
    {
        var start = _position;
        Advance();
        var done = false;

        while (!done)
        {
            switch (Current)
            {
                case '\0':
                case '\r':
                case '\n':
                    done = true;
                    break;
                default:
                    Advance();
                    break;
            }
        }

        var comment = _input.Substring(start, _position - start);
        AddToken(TokenType.COMMENT, comment.Trim(), start);
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
            AddToken(TokenType.Number, number, start);
        }
        else
        {
            AddToken(TokenType.INT, number, start);
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
        AddToken(TokenType.STRING, sb.ToString(), start+1);
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
                AddToken(TokenType.CONST, identifier, start);
                break;
            case '$':
                AddToken(TokenType.VAR, identifier, start);
                break;
            case '@':
                AddToken(TokenType.EX_VAR, identifier, start);
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
            AddToken(keywords[word.ToLower()], word.ToUpper(), start);
        }
        else if ((isAllUpper || isAllLower) && logicwords.ContainsKey(word.ToLower()))
        {
            // and, or, not关键字小写
            AddToken(logicwords[word.ToLower()], word.ToLower(), start);
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
            AddToken(ktype, word.ToUpper(), start);
        }
        else
        {
            AddToken(TokenType.IDENT, word, start);
        }
    }

    private void ReadOperatorOrSymbol()
    {
        var start = _position;
        var current = Advance();
        var next = Peek();

        switch (current)
        {
            case '=':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.EQL, "==", start);
                }
                else
                {
                    AddToken(TokenType.ASSIGN, "=", start);
                }
                break;
            case '+':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.ADD_ASSIGN, "+=", start);
                }
                else
                {
                    AddToken(TokenType.ADD, "+", start);
                }
                break;
            case '-':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.SUB_ASSIGN, "-=", start);
                }
                else
                {
                    AddToken(TokenType.SUB, "-", start);
                }
                break;
            case '*':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.MUL_ASSIGN, "*=", start);
                }
                else
                {
                    AddToken(TokenType.MUL, "*", start);
                }
                break;
            case '/':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.DIV_ASSIGN, "/=", start);
                }
                else
                {
                    AddToken(TokenType.DIV, "/", start);
                }
                break;
            case '\\':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.SlashIAssign, "\\=", start);
                }
                else
                {
                    AddToken(TokenType.SlashI, "\\", start);
                }
                break;
            case '%':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.MOD_ASSIGN, "%=", start);
                }
                else
                {
                    AddToken(TokenType.MOD, "%", start);
                }
                break;
            case '^':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.XOR_ASSIGN, "^=", start);
                }
                else
                {
                    AddToken(TokenType.XOR, "^", start);
                }
                break;
            case '&':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.BitAnd_ASSIGN, "&=", start);
                }
                else
                {
                    AddToken(TokenType.BitAnd, "&", start);
                }
                break;
            case '|':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.BitOr_ASSIGN, "|=", start);
                }
                else
                {
                    AddToken(TokenType.BitOr, "|", start);
                }
                break;
            case '!':
                if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.NEQ, "!=", start);
                }
                break;
            case '>':
                if (next == '>')
                {
                    Advance();
                    if (_position < _input.Length && _input[_position] == '=')
                    {
                        Advance();
                        AddToken(TokenType.SHR_ASSIGN, ">>=", start);
                    }
                    else
                    {
                        AddToken(TokenType.SHR, ">>", start);
                    }
                }
                else if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.GEQ, ">=", start);
                }
                else
                {
                    AddToken(TokenType.GTR, ">", start);
                }
                break;
            case '<':
                if (next == '<')
                {
                    Advance();
                    if (_position < _input.Length && _input[_position] == '=')
                    {
                        Advance();
                        AddToken(TokenType.SHL_ASSIGN, "<<=", start);
                    }
                    else
                    {
                        AddToken(TokenType.SHL, "<<", start);
                    }
                }
                else if (next == '=')
                {
                    Advance();
                    AddToken(TokenType.LEQ, "<=", start);
                }
                else
                {
                    AddToken(TokenType.LESS, "<", start);
                }
                break;
            case '~':
                AddToken(TokenType.BitNot, "~", start);
                break;
            case ',':
                AddToken(TokenType.COMMA, ",", start);
                break;
            case '(':
                AddToken(TokenType.LeftParen, "(", start);
                break;
            case ')':
                AddToken(TokenType.RightParen, ")", start);
                break;
            case '[':
                AddToken(TokenType.LeftBracket, "[", start);
                break;
            case ']':
                AddToken(TokenType.RightBracket, "]", start);
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
 