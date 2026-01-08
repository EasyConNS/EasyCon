using EasyCon.Script2.Syntax;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EasyCon.Script.Parsing;

internal partial class Parser
{
    private ConstDeclStmt? ParseConstantDecl(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer[0].Type == TokenType.CONST && lexer[1].Type == TokenType.ASSIGN)
        {
            var pr = new ExprParser([.. lexer.Skip(2)], _formatter, allowVar: false);
            var eexp = pr.ParseExpression();
            if (pr.EOF(out _)) return null;

            if (_formatter.TryDeclConstant(lexer[0].Value, eexp))
                return new ConstDeclStmt(lexer[0].Value, eexp.GetCodeText());
            throw new Exception($"重复定义的常量：{lexer[0].Value}");
        }
        return null;
    }

    private ImportStmt? ParseImport(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer.Length != 2 + 1) return null;
        if (lexer[0].Type == TokenType.IMPORT && lexer[1].Type == TokenType.STRING)
        {
            return new ImportStmt(lexer[1].Value);
        }
        return null;
    }


    private UnaryOp? ParseUnary(VariableExpr dest, List<Token> tokens)
    {
        if (tokens.Count == 3 && tokens[0].Type.GetUnaryOperatorPrecedence() > 0 && tokens[1].Type == TokenType.VAR && tokens[2].Type == TokenType.EOF)
        {
            // 一元运算符
            return tokens[0].Type switch
            {
                TokenType.SUB => new Negative(dest, _formatter.GetVar(tokens[1].Value)),
                TokenType.BitNot => new Not(dest, _formatter.GetVar(tokens[1].Value)),
                _ => null
            };
        }
        return null;
    }

    private Statement? ParseAssignment(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer.Length < 3 + 1) return null;
        if (lexer[0].Type == TokenType.VAR && lexer[1].Type == TokenType.ASSIGN)
        {
            var des = _formatter.GetVar(lexer[0].Value);

            var sm = ParseUnary(des, [.. lexer.Skip(2)]);
            if (sm != null)
            {
                return sm;
            }
            var pr = new ExprParser([.. lexer.Skip(2)], _formatter);
            var eexp = pr.ParseExpression();
            if (pr.EOF(out _)) return null;
            return new AssignmentStmt(des, eexp);
        }

        //aug assign
        if (lexer[0].Type == TokenType.VAR && lexer[1].Type.OperatorIsAug())
        {
            if (lexer.Count() != 3 + 1) return null;
            var des = _formatter.GetVar(lexer[0].Value);
            MetaOperator? op = MetaOperator.All.FirstOrDefault(o => o.Operator + "=" == lexer[1].Value);
            if (op == null) return null;
            if (op.OnlyInstant)
            {
                if (lexer[2].Type != TokenType.INT && lexer[2].Type != TokenType.CONST)
                    throw new Exception("只能使用常量或数字");
            }
            return new AssignmentStmt(des, _formatter.GetValueEx(lexer[2].Value), op);
        }

        return null;
    }

    private Statement? ParseIfelse(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer.Length < 3 + 1) return null;
        if (lexer[0].Type == TokenType.IF || lexer[0].Type == TokenType.ELIF)
        {
            var pr = new ExprParser([.. lexer.Skip(1)], _formatter);
            var eexp = pr.ParseExpression();
            if (pr.EOF(out _)) return null;
            if (eexp is CmpExpression expr)
            {
                switch (lexer[0].Type)
                {
                    case TokenType.IF:
                        return new IfStmt(expr);
                    case TokenType.ELIF:
                        return new ElseIf(expr);
                }
            }
        }
        return null;
    }

    private Statement? ParseFor(string text)
    {
        if (text.Equals("for", StringComparison.OrdinalIgnoreCase))
            return new For_Infinite();
        var m = Regex.Match(text, $@"^for\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new For_Static(_formatter.GetValueEx(m.Groups[1].Value));
        m = Regex.Match(text, $@"^for\s+{Formats.RegisterEx}\s*=\s*{Formats.ValueEx}\s*to\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new For_Full(_formatter.GetVar(m.Groups[1].Value), _formatter.GetValueEx(m.Groups[2].Value), _formatter.GetValueEx(m.Groups[3].Value));
        return null;
    }

    private Statement? ParseLoopCtrl(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text);
        if (tokens.Length > 2 + 1) return null;
        switch (tokens[0].Type)
        {
            case TokenType.BREAK:
                if (tokens[1].Type == TokenType.CONST || tokens[1].Type == TokenType.INT)
                {
                    if (tokens[2].Type != TokenType.EOF) return null;
                    return new Break(_formatter.GetInstant(tokens[1].Value, true));
                }
                else if (tokens[1].Type == TokenType.EOF)
                {
                    return new Break();
                }
                break;
            case TokenType.CONTINUE:
                if (tokens[1].Type == TokenType.EOF)
                {
                    return new Continue();
                }
                break;
        }
        return null;
    }

    const string GPKey = "[ABXYLR]|Z[LR]|[LR]CLICK|HOME|CAPTURE|PLUS|MINUS|LEFT|RIGHT|UP|DOWN";
    private Statement? ParseKey(string text)
    {
        var m = Regex.Match(text, $"^({GPKey})$", RegexOptions.IgnoreCase);
        if (m.Success && (NSKeys.Get(m.Groups[1].Value)) != null)
            return new KeyPress(m.Groups[1].Value);
        m = Regex.Match(text, $@"^({GPKey})\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        if (m.Success && (NSKeys.Get(m.Groups[1].Value)) != null)
            return new KeyPress(m.Groups[1].Value, _formatter.GetValueEx(m.Groups[2].Value));
        m = Regex.Match(text, $@"^({GPKey})\s+(up|down)$", RegexOptions.IgnoreCase);
        if (m.Success && (NSKeys.Get(m.Groups[1].Value)) != null)
        {
            return m.Groups[2].Value.ToUpper() switch
            {
                "UP" => new KeyUp(m.Groups[1].Value),
                "DOWN" => new KeyDown(m.Groups[1].Value),
                _ => null,
            };
        }

        // stick
        m = Regex.Match(text, @"^([lr]s)\s+(reset)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var keyname = m.Groups[1].Value;
            if (NSKeys.GetKey(keyname) == null)
                return null;
            return new StickUp(keyname);
        }
        m = Regex.Match(text, @"^([lr]s)\s+([a-z0-9]+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var keyname = m.Groups[1].Value;
            var direction = m.Groups[2].Value;
            if (NSKeys.GetKey(keyname, direction) == null)
                return null;
            return new StickDown(keyname, direction);
        }
        m = Regex.Match(text, $@"^([lr]s)\s+([a-z0-9]+)\s*,\s*({Formats.ValueEx})$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var keyname = m.Groups[1].Value;
            var direction = m.Groups[2].Value;
            var duration = m.Groups[3].Value;
            if (NSKeys.GetKey(keyname, direction) == null)
                return null;
            return new StickPress(keyname, direction, _formatter.GetValueEx(duration));
        }
        return null;
    }

    private FuncStmt? ParseFuncDecl(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text);
        if (tokens.Length == 3)
        {
            if (tokens[0].Type == TokenType.FUNC && tokens[1].Type == TokenType.IDENT && tokens[2].Type == TokenType.EOF)
                return new FuncStmt(tokens[1].Value);
        }

        return null;
    }

    private Statement? ParseNamedExpression(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text);
        var first = tokens.First()!;
        if (first.Type != TokenType.IDENT) return null;
        switch (first.Value.ToLower())
        {
            case "wait":
                if (tokens.Length != 2 + 1) return null;
                if (tokens[1].Type == TokenType.CONST || tokens[1].Type == TokenType.INT || tokens[1].Type == TokenType.VAR)
                {
                    return new Wait(_formatter.GetValueEx(tokens[1].Value));
                }
                break;
            case "call":
                if (tokens.Length != 2 + 1) return null;
                return tokens[1].Type == TokenType.IDENT && tokens[2].Type == TokenType.EOF ? new CallStmt(tokens[1].Value, [], false) : null;
#if DEBUG
            case "sprint":
            case "smem":
                if (tokens.Length != 2 + 1) return null;
                if (tokens[1].Type == TokenType.INT && tokens[2].Type == TokenType.EOF)
                {
                    var ism = first.Value.Equals("smem", StringComparison.CurrentCultureIgnoreCase);
                    return new SerialPrint(uint.Parse(tokens[1].Value), ism);
                }
                break;
#endif
            // 内置函数
            default:
                ImmutableArray<Token> argtoks = [.. tokens.Skip(1)];
                if(argtoks.Length == 1)return new CallStmt(first.Value.ToUpper(), []);
                var args = new List<ExprBase>();
                var pr = new ExprParser(argtoks, _formatter);
                args.Add(pr.ParseExpression());
                while (!pr.EOF(out var idx) && argtoks[idx].Type==TokenType.COMMA)
                {
                    idx++; // 跳过逗号
                    argtoks = argtoks[idx..];
                    pr = new ExprParser(argtoks, _formatter);
                    args.Add(pr.ParseExpression());
                }
                if (!pr.EOF(out _)) return null;
                return new CallStmt(first.Value.ToUpper(), [.. args]);
        }
        return null;
    }
}

class ExprParser(ImmutableArray<Token> toks, Formatter formatter, bool allowVar = true)
{
    private readonly ImmutableArray<Token> _tokens = toks;
    private int _position = 0;
    readonly Formatter _formatter = formatter;
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
        _position++;
        return now;
    }

    public bool EOF(out int pos)
    {
        pos = _position;
        return _position == _tokens.Length - 1;
    }

    private Token Match(TokenType type, string message = "")
    {
        if (Current.Type == type)
        {
            return Advance();
        }
        throw new Exception($"{Current.Type}");
    }

    public ExprBase ParseExpression(int parentPrecedence = 0)
    {
        ExprBase left;
        var unaryOperatorPrecedence = Current.Type.GetUnaryOperatorPrecedence();
        if (unaryOperatorPrecedence != 0 && unaryOperatorPrecedence >= parentPrecedence)
        {
            throw new Exception("不支持的语句");
        }
        else
        {
            left = ParsePrimary();
        }

        while (true)
        {
            var precedence = Current.Type.GetBinaryOperatorPrecedence();
            if (precedence == 0 || precedence <= parentPrecedence)
                break;

            var opToken = Advance();
            MetaOperator? op = MetaOperator.All.FirstOrDefault(o => o.Operator == opToken.Value);
            CompareOperator? cmp = CompareOperator.All.FirstOrDefault(o => o.Operator == opToken.Value);
            var right = ParseExpression(precedence);
            left = op != null ? new BinaryExpression(op!, left, right) : new CmpExpression(cmp!, left, right);
        }

        return left;
    }

    private ExprBase ParsePrimary()
    {
        switch (Current.Type)
        {
            case TokenType.STRING:
                var tokstr = Advance();
                return _formatter.GetValueEx(tokstr);
            case TokenType.CONST:
            case TokenType.VAR when allowVar:
            case TokenType.EX_VAR:
                var token = Advance();
                bool isConstant = token.Type == TokenType.CONST;
                bool isSpecial = token.Type == TokenType.EX_VAR;
                return _formatter.GetValueEx(token);
            case TokenType.LeftParen:
                var left = Advance();
                var expression = ParseExpression();
                var right = Match(TokenType.RightParen);
                return new ParenthesizedExpression(expression);
            case TokenType.INT:
            default:
                var toknum = Advance();
                try
                {
                    var value = uint.Parse(toknum.Value);
                    if (value < ushort.MinValue || value > ushort.MaxValue)
                    {
                        throw new Exception($"整数超出范围({ushort.MinValue} 到 {ushort.MaxValue}) 行{toknum.Line}");
                    }

                    return _formatter.GetValueEx(toknum);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"错误的数字格式: {ex.Message} 行{toknum.Line}");
                    throw new Exception($"错误的数字格式: {toknum.Value} 行{toknum.Line}");
                }
        }
    }
}