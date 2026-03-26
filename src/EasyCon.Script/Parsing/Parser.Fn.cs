using EasyCon.Script.Binding;
using EasyCon.Script2;
using EasyCon.Script2.Syntax;
using EasyScript;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EasyCon.Script.Parsing;

internal partial class Parser
{
    private Statement? ParseStatement(ImmutableArray<Token> toks)
    {
        if (toks.Length == 0 || (toks.Length == 1 && toks[0].Type == TokenType.EOF)) return new EmptyStmt();
        var firstToken = toks[0];
        if (firstToken.Type == TokenType.CONST) return ParseConstantDecl(toks);
        else if (firstToken.Type == TokenType.VAR) return ParseAssignment(toks);
        else if (firstToken.Type == TokenType.IMPORT) return ParseImport(toks);
        else if (firstToken.Type == TokenType.IF || firstToken.Type == TokenType.ELIF)return ParseIfelse(toks);
        else if (firstToken.Type == TokenType.ELSE) return new Else();
        else if (firstToken.Type == TokenType.ENDIF) return new EndIf();
        else if (firstToken.Type == TokenType.WHILE) return ParseWhile(toks);
        else if (firstToken.Type == TokenType.FOR) return ParseFor(toks);
        else if (firstToken.Type == TokenType.BREAK || firstToken.Type == TokenType.CONTINUE)return ParseLoopCtrl(toks);
        else if (firstToken.Type == TokenType.NEXT) return new Next();
        else if (firstToken.Type == TokenType.FUNC) return ParseFuncDecl(toks);
        else if (firstToken.Type == TokenType.ENDFUNC) return new EndFuncStmt();
        else if (firstToken.Type == TokenType.RETURN) return ParseReturn(toks);
        else if (firstToken.Type == TokenType.END) return new EndBlockStmt();
        else if (firstToken.Type == TokenType.INT)
        {
            if (int.TryParse(firstToken.Value, out int duration)) return new Wait(duration, true);
        }
        else if (firstToken.Type == TokenType.IDENT)
        {
            if(firstToken.Value.Equals("print", StringComparison.OrdinalIgnoreCase) || firstToken.Value.Equals("alert", StringComparison.OrdinalIgnoreCase))
            {
                var curline = firstToken.Text.Lines[firstToken.Line-1].Text;
                curline = curline.Contains('#') ? curline.Substring(0, curline.IndexOf('#')) : curline;
                return ParsePrintStmt(firstToken, curline.Trim());
            }
        }
        // Handle key statements
        var fullline = firstToken.Text.Lines[firstToken.Line - 1].Text;
        fullline = fullline.Contains('#') ? fullline.Substring(0, fullline.IndexOf('#')) : fullline;
        return ParseKey(fullline.Trim()) ?? ParseNamedExpression(toks);
    }
    [GeneratedRegex(@"(\s*#.*)$")]
    private static partial Regex CommentRegex();
    private AssignmentStmt? ParseConstantDecl(ImmutableArray<Token> toks)
    {
        if (toks.Length < 3) return null;
        if (toks[1].Type == TokenType.ASSIGN)
        {
            var des = (VariableExpr)_formatter.GetValueEx(toks[0]);
            var pr = new ExprParser([.. toks.Skip(2)], _formatter, allowVar: false);
            var eexp = pr.ParseExpression();
            if (!pr.EOF(out _)) return null;
            return new AssignmentStmt(des, eexp);
        }
        return null;
    }

    private ImportStmt? ParseImport(ImmutableArray<Token> toks)
    {
        if (toks.Length != 2) return null;
        if (toks[0].Type == TokenType.IMPORT && toks[1].Type == TokenType.STRING)
        {
            var libSrc = Path.Combine(_filePath, LibPath, toks[1].Value);
            if (!File.Exists(libSrc))
            {
                throw new Exception($"文件不存在:{libSrc}");
            }
            return new ImportStmt(toks[1].Value, Path.Combine(_filePath, LibPath));
        }
        return null;
    }

    private AssignmentStmt? ParseAssignment(ImmutableArray<Token> toks)
    {
        if (toks.Length < 3) return null;

        var des = (VariableExpr)_formatter.GetValueEx(toks[0]);
        CompareOperator? op = null;
        if(toks[1].Type.OperatorIsAug())
        {
            //aug assign
            op = CompareOperator.All.FirstOrDefault(o => o.Operator + "=" == toks[1].Value);
            if (op == null) return null;
        }

        var pr = new ExprParser([.. toks.Skip(2)], _formatter);
        var eexp = pr.ParseExpression();
        if (!pr.EOF(out _)) return null;
        return new AssignmentStmt(des, eexp, op);
    }

    private ReturnStmt? ParseReturn(ImmutableArray<Token> toks)
    {
        if (toks.Length > 1)
        {
            var pr = new ExprParser([.. toks.Skip(1)], _formatter);
            var eexp = pr.ParseExpression();
            if (!pr.EOF(out _)) return null;
            return new ReturnStmt(eexp);
        }
        return new ReturnStmt();
    }

    private Statement? ParseIfelse(ImmutableArray<Token> tokens)
    {
        if (tokens.Length < 2) return null;
        if (tokens[0].Type == TokenType.IF || tokens[0].Type == TokenType.ELIF)
        {
            var pr = new ExprParser([.. tokens.Skip(1)], _formatter);
            var expr = pr.ParseExpression();
            if (!pr.EOF(out _)) return null;
            switch (tokens[0].Type)
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

    private Statement? ParseFor(ImmutableArray<Token> tokens)
    {
        if(tokens.Length == 1)
        {
            return new For_Infinite();
        }
        if(tokens.Length == 2 && checkPrimary(tokens[1].Type))
        {
            return new For_Static(_formatter.GetValueEx(tokens[1]));
        }
        if(tokens.Length == 6)
        {
            if(tokens[1].Type == TokenType.VAR && tokens[2].Type == TokenType.ASSIGN 
            && checkPrimary(tokens[3].Type) 
            && tokens[4].Type == TokenType.TO 
            && checkPrimary(tokens[5].Type))
            {
                return new For_Full((VariableExpr)_formatter.GetValueEx(tokens[1]), _formatter.GetValueEx(tokens[3]), _formatter.GetValueEx(tokens[5]));
            }
        }
        return null;
    }

    private WhileStmt? ParseWhile(ImmutableArray<Token> tokens)
    {
        if (tokens.Length < 2) return null;
        var pr = new ExprParser([.. tokens.Skip(1)], _formatter);
        var expr = pr.ParseExpression();
        if (!pr.EOF(out _)) return null;
        return new WhileStmt(expr);
    }

    private Statement? ParseLoopCtrl(ImmutableArray<Token> tokens)
    {
        if (tokens.Length > 2) return null;
        switch (tokens[0].Type)
        {
            case TokenType.BREAK:
                if (tokens.Length == 2 && tokens[1].Type == TokenType.INT)
                {
                    if (tokens.Length > 2) return null;
                    if(!uint.TryParse(tokens[1].Value, out var level))return null;
                    return new Break(level);
                }
                else
                    if (tokens.Length == 1)
                    {
                        return new Break();
                    }
                break;
            case TokenType.CONTINUE:
                if (tokens.Length == 1)
                {
                    return new Continue();
                }
                break;
        }
        return null;
    }

    /// <summary>
    /// 解析手柄按键语句
    // 键位(+键位) [持续时间(ms)|DOWN|UP]
    // LS|RS 方向|角度 [, 持续时间(ms)]
    // LS|RS RESET
    /// </summary>
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

    private FuncStmt? ParseFuncDecl(ImmutableArray<Token> tokens)
    {
        var pr = new ExprParser(tokens, _formatter);
        var func = pr.ParseFunctionDecl();
        return func;
    }

    private Statement? ParseNamedExpression(ImmutableArray<Token> tokens)
    {
        var first = tokens.First()!;
        if (first.Type != TokenType.IDENT) return null;
        switch (first.Value.ToLower())
        {
            case "wait":
                if (tokens.Length != 2) return null;
                if (tokens[1].Type == TokenType.CONST || tokens[1].Type == TokenType.INT || tokens[1].Type == TokenType.VAR)
                {
                    return new Wait(_formatter.GetValueEx(tokens[1]));
                }
                break;
            case "call":
                if (tokens.Length != 2) return null;
                return tokens[1].Type == TokenType.IDENT ? new CallStmt(tokens[1].Value, []) : null;
#if DEBUG
            case "sprint":
            case "smem":
                if (tokens.Length != 2) return null;
                if (tokens[1].Type == TokenType.INT)
                {
                    var ism = first.Value.Equals("smem", StringComparison.CurrentCultureIgnoreCase);
                    return new SerialPrint(uint.Parse(tokens[1].Value), ism);
                }
                break;
#endif
            default:
                if (tokens.Length < 2) return null;
                var args = ParseArguments([.. tokens.Skip(1)]);
                return new CallStmt(first.Value.ToUpper(), [.. args]);
        }
        return null;
    }

    private ImmutableArray<ExprBase> ParseArguments(ImmutableArray<Token> toks)
    {
        var pr = new ExprParser(toks, _formatter);
        var args = pr.ParseArguments();
        if (!pr.EOF(out _)) throw new Exception("函数传参解析失败");
        return args;
    }

    // print兼容语法特殊解析
    private CallStmt ParsePrintStmt(Token firstToken, string curline)
    {
        var bitandtoks = SyntaxTree.ParseTokens("&");
        var bitand = bitandtoks[0];

        var args = curline[6..].Split('&').Select(t =>
        {
            t = t.Trim();
            try
            {
                return _formatter.GetValueEx(t);
            }
            catch (FormatException)
            {
                return new LiteralExpr(t);
            }
        });
        var res = args.Aggregate((a,b)=> new BinaryExpression(bitand!, a, b));
        return new CallStmt(firstToken.Value.ToUpper(), [res]);
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
        return _position >= _tokens.Length;
    }
    private bool Check(TokenType type) => Current?.Type == type;
    private Token Match(TokenType type, string message = "")
    {
        if (Current.Type == type)
        {
            return Advance();
        }
        throw new Exception($"{message} 需要<{type}>但是意外的<{Current.Value}>");
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

    public FuncStmt ParseFunctionDecl()
    {
        var functionToken = Advance();
        var functionName = Match(TokenType.IDENT, "函数声明需要函数名");

        var hasPar = false;
        var parameters = ImmutableArray<ParameterSyntax>.Empty;
        if (Check(TokenType.LeftParen))
        {
            Match(TokenType.LeftParen, "函数声明缺少左括号");
            hasPar = true;
            parameters = ParseParameterList();
            Match(TokenType.RightParen, "函数声明缺少右括号");
        }
        var typeClause = ParseOptionalTypeClause();

        if (_position < _tokens.Length) throw new Exception("函数声明语法错误");
        return new FuncStmt(functionName, parameters, hasPar, typeClause);
    }

    private ImmutableArray<ParameterSyntax> ParseParameterList()
    {
        var nodesAndSeparators = ImmutableArray.CreateBuilder<ParameterSyntax>();

        var parseNextParameter = true;
        while (parseNextParameter &&
                Current.Type != TokenType.RightParen &&
                _position != _tokens.Length)
        {
            var parameter = ParseParameter();
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
        return nodesAndSeparators.ToImmutable();
    }
    private ParameterSyntax ParseParameter()
    {
        var identifier = Match(TokenType.VAR, "函数参数只能是变量");
        var parameter = (VariableExpr)_formatter.GetValueEx(identifier);
        var type = ParseOptionalTypeClause();
        return new ParameterSyntax(parameter, type);
    }

    private TypeClauseSyntax? ParseOptionalTypeClause()
    {
        if (Current.Type != TokenType.COLON)
            return null;

        return ParseTypeClause();
    }

    private TypeClauseSyntax ParseTypeClause()
    {
        var colonToken = Match(TokenType.COLON);
        var identifier = Match(TokenType.IDENT);
        return new TypeClauseSyntax(colonToken, identifier);
    }

    public ImmutableArray<ExprBase> ParseArguments()
    {
        var nodesAndSeparators = ImmutableArray.CreateBuilder<ExprBase>();
        var parseNextArgument = true;
        while (parseNextArgument &&
                Current.Type != TokenType.RightParen &&
                _position != _tokens.Length)
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
    
    private Statement ParsePadButtonStatement()
    {
        List<Token> keyTokens = [];
        var firstKey = Advance();
        keyTokens.Add(firstKey);

        if (firstKey.Type == TokenType.StickKeyword)
        {
            switch (Current.Type)
            {
                case TokenType.INT:
                case TokenType.CONST:
                case TokenType.VAR:
                case TokenType.ButtonKeyword:
                    var state = Advance();

                    if (Check(TokenType.COMMA))
                    {
                        Advance();
                        var duration = Match(TokenType.INT, "摇杆语法不正确");
                        var value = uint.Parse(duration.Value);
                        //return new StickStatement([.. keyTokens], state.Value, false, value);
                    }
                    else
                    {
                        //return new StickStatement([.. keyTokens], state.Value, false);
                    }
                    return null;
                case TokenType.ResetKeyword:
                    Match(TokenType.ResetKeyword, "摇杆语法不正确");
                    return new StickAct(firstKey.Value, "RESET");
            }
        }
        else
        {
            if (EOF(out _))
            {
                return new KeyPress(firstKey.Value);
            }
            else if (Check(TokenType.ButtonKeyword))
            {
                var state = Advance();
                var isUp = state.Value.ToUpper() == "UP";
                return new KeyAct(firstKey.Value, isUp);
            }
            else
            {
                var pars = ParsePrimary();
                return new KeyPress(firstKey.Value, pars);
            }
        }
        return null;
    }

    // [1,2,3]
    private ExprBase ParseIndexDefExpression()
    {
        allowVar = false;
        var lb = Match(TokenType.LeftBracket, "语法需要'['");
        var nodesAndSeparators = ImmutableArray.CreateBuilder<ExprBase>();

        var parseNext = true;
        while (parseNext &&
                Current.Type != TokenType.RightBracket &&
                _position != _tokens.Length)
        {
            var expression = ParsePrimary();
            nodesAndSeparators.Add(expression);

            if (Current.Type == TokenType.COMMA)
            {
                var comma = Advance();
            }
            else
            {
                parseNext = false;
            }
        }

        var rb = Match(TokenType.RightBracket, "语法需要']'");
        return new IndexDefExpression(lb, [.. nodesAndSeparators], rb);
    }

    // [1..3]
    private ExprBase ParseSliceExpression(Token variableToken)
    {
        var lb = Match(TokenType.LeftBracket, "语法需要'['");

        var ommitstart = Check(TokenType.COLON);
        var start = Check(TokenType.COLON) ? new LiteralExpr(0) : ParsePrimary();
        // [expr]
        if (Check(TokenType.RightBracket))
        {
            var rb = Match(TokenType.RightBracket, "语法需要']'");
            return new IndexVisitExpression((VariableExpr)_formatter.GetValueEx(variableToken), lb, start, rb);
        }
        Match(TokenType.COLON, "语法不正确[<start>:<end>]");

        var end = Check(TokenType.RightBracket) ? new LiteralExpr("") : ParsePrimary();
        Match(TokenType.RightBracket, "语法需要']'");
        return new SliceExpression((VariableExpr)_formatter.GetValueEx(variableToken), start, end, ommitstart);
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
            case TokenType.STRING:
            case TokenType.CONST:
            case TokenType.VAR when allowVar:
            case TokenType.EX_VAR when allowVar:
                var token = Advance();
                if(token.Type == TokenType.VAR && Current.Type == TokenType.LeftBracket)
                {
                    return ParseSliceExpression(token);
                }
                return _formatter.GetValueEx(token);
            case TokenType.LeftBracket:
                return ParseIndexDefExpression();
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