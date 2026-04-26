using EasyCon.Script.Text;
using System.Collections.Immutable;
using System.Text;

namespace EasyCon.Script.Syntax;

internal sealed partial class Lexer(SyntaxTree syntaxTree)
{
    private readonly DiagnosticBag _diagnostics = [];
    private readonly SyntaxTree _syntaxTree = syntaxTree;
    private readonly SourceText _text = syntaxTree.Text;

    private readonly string _input = syntaxTree.Text.ToString();
    private int _position = 0;
    private readonly List<Token> _tokens = [];

    public DiagnosticBag Diagnostics => _diagnostics;

    private bool _expectEqualAfterIf = false;
    private bool _expectDirectionAgterStick = false;
    private bool _expectUPDOWNAgterBtn = false;

    private void cleanFlags()
    {
        _expectEqualAfterIf = false;
        _expectUPDOWNAgterBtn = false;
        _expectDirectionAgterStick = false;
    }

    // 关键字字典
    private static readonly Dictionary<string, TokenType> keywords = new()
    {
            { "import", TokenType.IMPORT },
            { "if", TokenType.IF },
            { "elif", TokenType.ELIF },
            { "else", TokenType.ELSE },
            { "endif", TokenType.ENDIF },
            { "while", TokenType.WHILE },
            { "end", TokenType.END },
            { "for", TokenType.FOR },
            { "to", TokenType.TO },
            { "in", TokenType.IN },
            { "step", TokenType.STEP },
            { "break", TokenType.BREAK },
            { "continue", TokenType.CONTINUE },
            { "next", TokenType.NEXT },

            { "func", TokenType.FUNC },
            { "endfunc", TokenType.ENDFUNC },
            { "return", TokenType.RETURN },

            { "true", TokenType.TRUE },
            { "false", TokenType.FALSE },
            { "reset", TokenType.ResetKeyword },
            { "extern", TokenType.EXTERN },
            { "from", TokenType.FROM },
        };
    private static readonly Dictionary<string, TokenType> logicwords = new()
    {
            { "and", TokenType.LogicAnd },
            { "or", TokenType.LogicOr },
            { "not", TokenType.LogicNot },
        };

    // 按键关键字
    private static readonly List<string> gamepadKeywords = ["A", "B", "X", "Y", "L", "R", "ZL", "ZR",
        "MINUS", "PLUS", "HOME", "CAPTURE",
        "LCLICK", "RCLICK",
        "DOWNLEFT", "DOWNRIGHT", "UPLEFT", "UPRIGHT",
        "UP", "DOWN", "LEFT", "RIGHT"];
    private static readonly List<string> stickKeywords = ["LS", "RS"];

    // 方向关键字
    private static readonly List<string> direcKeywords = ["UP", "DOWN", "LEFT", "RIGHT",
        "DOWNLEFT", "DOWNRIGHT", "UPLEFT", "UPRIGHT"];
    private static readonly List<string> statKeywords = ["UP", "DOWN"];
    public ImmutableArray<Token> Tokenize()
    {
        _tokens.Clear();
        while (_position < _input.Length)
        {
            SkipWhitespace();
            var current = Current;
            if (current == '\0') break;
            if (current == '#')
            {
                ReadComment();
            }
            else if (char.IsDigit(current))
            {
                ReadNumber();
            }
            else if (current == '"' || current == '\'')
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

        AddToken(TokenType.EOF, "", _position);
        return [.. _tokens];
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
        }
        return current;
    }

    private void AddToken(TokenType type, string value, int start)
    {
        _tokens.Add(new Token(_text, type, value, start));
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
                if (Current == '\n')
                {
                    AddToken(TokenType.NEWLINE, " ", _position);
                    cleanFlags();
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

        var comment = _input[start.._position];
        AddToken(TokenType.COMMENT, comment.Trim(), start);
    }

    private void ReadNumber()
    {
        var start = _position;
        while (_position < _input.Length && char.IsDigit(Current))
        {
            Advance();
        }

        // 处理小数点（如 1.5）
        if (_position < _input.Length && Current == '.' && _position + 1 < _input.Length && char.IsDigit(Lookahead))
        {
            Advance(); // consume '.'
            while (_position < _input.Length && char.IsDigit(Current))
            {
                Advance();
            }
        }

        var length = _position - start;
        var number = _input.Substring(start, length);
        if (number.Contains('.'))
        {
            if (!double.TryParse(number, out _))
            {
                var span = new SourceSpan(start, length);
                var location = new TextLocation(_text, span);
                _diagnostics.ReportInvalidNumber(location, number);
            }
            AddToken(TokenType.Number, number, start);
        }
        else
        {
            if (!int.TryParse(number, out _))
            {
                var span = new SourceSpan(start, length);
                var location = new TextLocation(_text, span);
                _diagnostics.ReportInvalidNumber(location, number);
            }
            AddToken(TokenType.INT, number, start);
        }
    }

    private void ReadString()
    {
        var start = _position;
        var sb = new StringBuilder();

        var quote = Advance();
        sb.Append(quote); // 开始的"

        while (_position < _input.Length && Current != quote)
        {
            if (Current == '\n' || Current == '\r')
            {
                break;
            }
            if (Current == '\\') // 转义字符
            {
                if (_position >= _input.Length)
                {
                    break;
                }

                var escaped = Lookahead switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\'' => '\'',
                    '"' => '"',
                    '\\' => '\\',
                    // 可根据需要添加更多转义
                    _ => '\0',
                };
                if (escaped != '\0')
                {
                    sb.Append(escaped);
                    Advance();
                    Advance();
                    continue;
                }
                else
                {
                    break;
                }
            }

            sb.Append(Advance());
        }

        if (Current != quote)
        {
            var span = new SourceSpan(start, sb.ToString().Length);
            var location = new TextLocation(_text, span);
            _diagnostics.ReportUnterminatedString(location);
        }
        sb.Append(Advance()); // 结束的"
        AddToken(TokenType.STRING, sb.ToString(), start);
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

        while (_position < _input.Length && IsIdentifierChar(Current))
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

    private void ReadPrintArguments()
    {
        while (_position < _input.Length && Current != '\n' && Current != '\r')
        {
            // 1. 跳过空白，但不生成 NEWLINE Token（由 SkipWhitespace 统一处理或此处忽略）
            if (char.IsWhiteSpace(Current))
            {
                Advance();
                continue;
            }

            var start = _position;

            // 2. 识别 & 符号
            if (Current == '&')
            {
                AddToken(TokenType.BitAnd, "&", start); // 假设 & 对应 BitAnd，或根据你的定义修改
                Advance();
                continue;
            }

            // 3. 识别变量或常量 ($, _, @)
            if (Current == '$' || Current == '_' || Current == '@')
            {
                ReadVariable();
                continue;
            }

            // 4. 识别普通字符串（直到遇到下一个 & 或 换行）
            var sb = new StringBuilder();
            while (_position < _input.Length && Current != '&' && Current != '\n' && Current != '\r')
            {
                sb.Append(Advance());
            }

            var text = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(text))
            {
                // 注意：这里的位置指向字符串开始的地方
                AddToken(TokenType.STRING, text, start);
            }
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
            var tokenType = keywords[word.ToLower()];
            AddToken(tokenType, word.ToUpper(), start);
            if (SyntaxTree.LegacyCompat)
            {
                if (tokenType == TokenType.IF || tokenType == TokenType.ELIF)
                    _expectEqualAfterIf = true;
            }
        }
        else if ((isAllUpper || isAllLower) && logicwords.ContainsKey(word.ToLower()))
        {
            // and, or, not关键字小写
            AddToken(logicwords[word.ToLower()], word.ToLower(), start);
        }
        else if ((isAllUpper || isAllLower) && gamepadKeywords.Contains(word.ToUpper()))
        {
            var ktype = TokenType.ButtonKeyword;
            if (_expectUPDOWNAgterBtn)
            {
                switch (word.ToUpper())
                {
                    case "UP":
                    case "DOWN":
                        ktype = TokenType.StateKeyword;
                        break;
                }
                _expectUPDOWNAgterBtn = false;
            }
            else
            {
                _expectUPDOWNAgterBtn = true;
            }
            if (_expectDirectionAgterStick)
            {
                if (direcKeywords.Contains(word.ToUpper()))
                {
                    ktype = TokenType.DirectionKeyword;
                }
                _expectDirectionAgterStick = false;
            }
            // 手柄按键大写
            AddToken(ktype, word.ToUpper(), start);
        }
        else if ((isAllUpper || isAllLower) && stickKeywords.Contains(word.ToUpper()))
        {
            _expectDirectionAgterStick = true;
            AddToken(TokenType.StickKeyword, word.ToUpper(), start);
        }
        else
        {
            AddToken(TokenType.IDENT, word, start);
            if (word.Equals("print", StringComparison.OrdinalIgnoreCase) || word.Equals("alert", StringComparison.OrdinalIgnoreCase))
                ReadPrintArguments();
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
                else if (_expectEqualAfterIf && SyntaxTree.LegacyCompat)
                {
                    AddToken(TokenType.EQL, "==", start);
                    _expectEqualAfterIf = false;
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
                    AddToken(TokenType.SlashI_ASSIGN, "\\=", start);
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
            case '{':
                AddToken(TokenType.OpenBrace, "{", start);
                break;
            case '}':
                AddToken(TokenType.CloseBrace, "}", start);
                break;
            case ':':
                AddToken(TokenType.COLON, ":", start);
                break;
            case '.':
                AddToken(TokenType.DOT, ".", start);
                break;
            default:
                var span = new SourceSpan(start, 1);
                var location = new TextLocation(_text, span);
                AddToken(TokenType.BadToken, _input[start..start++], start);
                _diagnostics.ReportBadCharacter(location, current);
                break;
        }
    }
}