using EasyScript;
using EasyCon.Script2.Syntax;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EasyCon.Script.Parsing;

internal partial class Parser
{
    const string LibPath = "libs/";
    private AssignmentStmt? ParseConstantDecl(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer.Length < 3 + 1) return null;
        if (lexer[0].Type == TokenType.CONST && lexer[1].Type == TokenType.ASSIGN)
        {
            var des = (VariableExpr)_formatter.GetValueEx(lexer[0]);
            var pr = new ExprParser([.. lexer.Skip(2)], _formatter, allowVar: false);
            var eexp = pr.ParseExpression();
            if (!pr.EOF(out _)) return null;
            return new AssignmentStmt(des, eexp);
        }
        return null;
    }

    private ImportStmt? ParseImport(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer.Length != 2 + 1) return null;
        if (lexer[0].Type == TokenType.IMPORT && lexer[1].Type == TokenType.STRING)
        {
            if (!File.Exists(LibPath + lexer[1].Value))
            {
                throw new Exception($"文件不存在:{LibPath}{lexer[1].Value}");
            }
            return new ImportStmt(lexer[1].Value);
        }
        return null;
    }

    private AssignmentStmt? ParseAssignment(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer.Length < 3 + 1) return null;
        if (lexer[0].Type == TokenType.VAR)
        {
            var des = (VariableExpr)_formatter.GetValueEx(lexer[0]);
            CompareOperator? op = null;
            if(lexer[1].Type.OperatorIsAug())
            {
                //aug assign
                op = CompareOperator.All.FirstOrDefault(o => o.Operator + "=" == lexer[1].Value && o.InstructionType != null);
                if (op == null) return null;
            }

            var pr = new ExprParser([.. lexer.Skip(2)], _formatter);
            var eexp = pr.ParseExpression();
            if (!pr.EOF(out _)) return null;
            return new AssignmentStmt(des, eexp, op);
        }

        return null;
    }

    private Statement? ParseIfelse(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer.Length < 2 + 1) return null;
        if (lexer[0].Type == TokenType.IF || lexer[0].Type == TokenType.ELIF)
        {
            var pr = new ExprParser([.. lexer.Skip(1)], _formatter);
            var expr = pr.ParseExpression();
            if (!pr.EOF(out _)) return null;
            switch (lexer[0].Type)
            {
                case TokenType.IF:
                    return new IfStmt(expr);
                case TokenType.ELIF:
                    return new ElseIf(expr);
            }
        }
        return null;
    }

    private static bool checkPrimary(TokenType type)
    {
        return type == TokenType.INT || type == TokenType.CONST ||type == TokenType.VAR;
    }

    private Statement? ParseFor(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text);
        if (tokens[0].Type == TokenType.FOR)
        {
            if(tokens.Length == 1+1 && tokens[1].Type == TokenType.EOF)
            {
                return new For_Infinite();
            }
            if(tokens.Length == 2+1 && checkPrimary(tokens[1].Type))
            {
                return new For_Static(_formatter.GetValueEx(tokens[1]));
            }
            if(tokens.Length == 6+1)
            {
                if(tokens[1].Type == TokenType.VAR && tokens[2].Type == TokenType.ASSIGN 
                && checkPrimary(tokens[3].Type) 
                && tokens[4].Type == TokenType.TO 
                && checkPrimary(tokens[5].Type))
                {
                    return new For_Full((VariableExpr)_formatter.GetValueEx(tokens[1]), _formatter.GetValueEx(tokens[3]), _formatter.GetValueEx(tokens[5]));
                }
            }
        }
        // var m = Regex.Match(text, $@"^for\s+{Formats.RegisterEx}\s*=\s*{Formats.ValueEx}\s*to\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        // if (m.Success)
        //     return new For_Full((VariableExpr)_formatter.GetValueEx(m.Groups[1].Value), _formatter.GetValueEx(m.Groups[2].Value), _formatter.GetValueEx(m.Groups[3].Value));
        return null;
    }

    private Statement? ParseLoopCtrl(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text);
        if (tokens.Length > 2 + 1) return null;
        switch (tokens[0].Type)
        {
            case TokenType.BREAK:
                if (tokens[1].Type == TokenType.INT)
                {
                    if (tokens[2].Type != TokenType.EOF) return null;
                    return new Break((LiteralExpr)_formatter.GetValueEx(tokens[1]));
                }
                else
                    if (tokens[1].Type == TokenType.EOF)
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

    const string GPKey = "[ABXYLR]|Z[LR]|[LR]CLICK|HOME|CAPTURE|PLUS|MINUS|LEFT|RIGHT|UP|DOWN|DOWNLEFT|DOWNRIGHT|UPLEFT|UPRIGHT";
    private Statement? ParseKey(string text)
    {
        var m = Regex.Match(text, $"^({GPKey})$", RegexOptions.IgnoreCase);
        if (m.Success && NSKeys.GetKey(m.Groups[1].Value) != GamePadKey.None)
            return new KeyPress(m.Groups[1].Value);
        m = Regex.Match(text, $@"^({GPKey})\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        if (m.Success && NSKeys.GetKey(m.Groups[1].Value) != GamePadKey.None)
            return new KeyPress(m.Groups[1].Value, _formatter.GetValueEx(m.Groups[2].Value));
        m = Regex.Match(text, $@"^({GPKey})\s+(up|down)$", RegexOptions.IgnoreCase);
        if (m.Success && NSKeys.GetKey(m.Groups[1].Value) != GamePadKey.None)
        {
            return m.Groups[2].Value.ToUpper() switch
            {
                "UP" => new KeyAct(m.Groups[1].Value, true),
                "DOWN" => new KeyAct(m.Groups[1].Value),
                _ => null,
            };
        }

        // stick
        m = Regex.Match(text, @"^([lr]s)\s+(reset)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var keyname = m.Groups[1].Value;
            if (NSKeys.GetKey(keyname) == GamePadKey.None)return null;
            return new StickAct(keyname, "RESET");
        }
        m = Regex.Match(text, @"^([lr]s)\s+([a-z0-9]+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var keyname = m.Groups[1].Value;
            var direction = m.Groups[2].Value;
            if(NSKeys.GetKey(keyname) == GamePadKey.None)return null;
            if (!NSKeys.CheckDirection(direction, out _))return null;
            return new StickAct(keyname, direction);
        }
        m = Regex.Match(text, $@"^([lr]s)\s+([a-z0-9]+)\s*,\s*({Formats.ValueEx})$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var keyname = m.Groups[1].Value;
            var direction = m.Groups[2].Value;
            var duration = m.Groups[3].Value;
            if(NSKeys.GetKey(keyname) == GamePadKey.None)return null;
            if (!NSKeys.CheckDirection(direction, out _)) return null;
            return new StickPress(keyname, direction, _formatter.GetValueEx(duration));
        }
        return null;
    }

    private FuncStmt? ParseFuncDecl(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text);
        if (tokens[0].Type == TokenType.FUNC)
        {
            if (tokens.Length > 2 && tokens[1].Type == TokenType.IDENT)
            {
                var pams = ParseParamter([.. tokens.Skip(2)]);
                return new FuncStmt(tokens[1].Value, pams);
            }
        }
        return null;
    }

    private ImmutableArray<VariableExpr> ParseParamter(ImmutableArray<Token> toks)
    {
        if (toks.Length == 1 && toks[0].Type == TokenType.EOF)
            return [];

        var pr = new ExprParser(toks, _formatter);
        var paramters = pr.ParseParameterList();
        if (!pr.EOF(out _)) throw new Exception("参数解析失败");
        return paramters;
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
                    return new Wait(_formatter.GetValueEx(tokens[1]));
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
                var args = ParseArguments([.. tokens.Skip(1)]);
                return new CallStmt(first.Value.ToUpper(), [.. args]);
        }
        return null;
    }

    private ImmutableArray<ExprBase> ParseArguments(ImmutableArray<Token> toks)
    {
        if (toks.Length == 1 && toks[0].Type == TokenType.EOF)
            return [];
        var pr = new ExprParser(toks, _formatter, allowStr: true);
        var args = pr.ParseArguments();
        if (!pr.EOF(out _)) throw new Exception("参数解析失败");
        return args;
    }
}

class ExprParser(ImmutableArray<Token> toks, Formatter formatter, bool allowVar = true, bool allowStr = false)
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
        throw new Exception($"{message} 但出现了<{Current.Type}>");
    }

    public ExprBase ParseExpression(int parentPrecedence = 0)
    {
        ExprBase left;
        var unaryOperatorPrecedence = Current.Type.GetUnaryOperatorPrecedence();
        if (unaryOperatorPrecedence != 0 && unaryOperatorPrecedence >= parentPrecedence)
        {
            var opToken = Advance();
            var op = UnaryOperator.All.FirstOrDefault(o => o.KeyWord == opToken.Value) ?? throw new Exception($"不支持的运算符：{opToken.Value}");
            var operand = ParseExpression(unaryOperatorPrecedence);
            left = new UnaryExpression(opToken, operand);
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
            CompareOperator? op = CompareOperator.All.FirstOrDefault(o => o.Operator == opToken.Value)
                ?? throw new Exception($"不支持的运算符：{opToken.Value}");
            var right = ParseExpression(precedence);
            left = new BinaryExpression(opToken, left, right);
        }

        return left;
    }

    public ImmutableArray<VariableExpr> ParseParameterList()
    {
        var nodesAndSeparators = ImmutableArray.CreateBuilder<VariableExpr>();

        _ = Match(TokenType.LeftParen);
        var parseNextParameter = true;
        while (parseNextParameter &&
                Current.Type != TokenType.RightParen &&
                Current.Type != TokenType.EOF)
        {
            var identifier = Match(TokenType.VAR, "函数参数只能是变量");
            var parameter = (VariableExpr)_formatter.GetValueEx(identifier);
            nodesAndSeparators.Add(parameter);

            if (Current.Type == TokenType.COMMA)
            {
                var comma = Advance();
            }
            else
            {
                parseNextParameter = false;
            }
        }
        _ = Match(TokenType.RightParen);
        return nodesAndSeparators.ToImmutable();
    }

    public ImmutableArray<ExprBase> ParseArguments()
    {
        var nodesAndSeparators = ImmutableArray.CreateBuilder<ExprBase>();
        var parseNextArgument = true;
        while (parseNextArgument &&
                Current.Type != TokenType.RightParen &&
                Current.Type != TokenType.EOF)
        {
            var expression = ParseExpression();
            nodesAndSeparators.Add(expression);

            if (Current.Type == TokenType.COMMA)
            {
                var comma = Advance();
            }
            else
            {
                parseNextArgument = false;
            }
        }
        return nodesAndSeparators.ToImmutable();
    }

    private ExprBase ParseCallExpression()
    {
        var identifier = Match(TokenType.IDENT);
        var openParenthesisToken = Match(TokenType.LeftParen);
        var arguments = ParseArguments();
        var closeParenthesisToken = Match(TokenType.RightParen);
        return new Callv1Expression(identifier, openParenthesisToken, arguments, closeParenthesisToken);
    }

    private ExprBase ParsePrimary()
    {
        switch (Current.Type)
        {
            case TokenType.STRING when allowStr:
            case TokenType.CONST:
            case TokenType.VAR when allowVar:
            case TokenType.EX_VAR when allowVar:
                var token = Advance();
                return _formatter.GetValueEx(token);
            case TokenType.LeftParen:
                Advance();
                var expression = ParseExpression();
                Match(TokenType.RightParen);
                return new ParenthesizedExpression(expression);
            case TokenType.IDENT:
                return ParseCallExpression();
            case TokenType.INT:
            default:
                var toknum = Advance();
                var ok = int.TryParse(toknum.Value, out _);
                if (!ok) throw new Exception($"错误的数字格式: {toknum.Value}");

                return _formatter.GetValueEx(toknum);
        }
    }
}