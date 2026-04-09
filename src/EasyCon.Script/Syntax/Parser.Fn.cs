using EasyScript;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EasyCon.Script.Syntax;

internal partial class Parser
{
    private int _position = 0;

    #region Statement Dispatch

    private Statement? ParseStatement()
    {
        if (_grouptokens.Length == 0 || (_grouptokens.Length == 1 && Current.Type == TokenType.EOF)) return new EmptyStmt();

        if (Current.Type == TokenType.CONST) return ParseConstantDecl();
        else if (Current.Type == TokenType.VAR) return ParseAssignment();
        else if (Current.Type == TokenType.IMPORT) return ParseImport();
        else if (Current.Type == TokenType.IF || Current.Type == TokenType.ELIF) return ParseIfelse();
        else if (Current.Type == TokenType.ELSE && _grouptokens.Length == 1) return new Else(Current);
        else if (Current.Type == TokenType.ENDIF && _grouptokens.Length == 1) return new EndIf(Current);
        else if (Current.Type == TokenType.WHILE) return ParseWhile();
        else if (Current.Type == TokenType.FOR) return ParseFor();
        else if (Current.Type == TokenType.BREAK || Current.Type == TokenType.CONTINUE) return ParseLoopCtrl();
        else if (Current.Type == TokenType.NEXT && _grouptokens.Length == 1) return new Next(Current);
        else if (Current.Type == TokenType.FUNC) return ParseFuncDecl();
        else if (Current.Type == TokenType.ENDFUNC && _grouptokens.Length == 1) return new EndFuncStmt(Current);
        else if (Current.Type == TokenType.RETURN) return ParseReturn();
        else if (Current.Type == TokenType.END && _grouptokens.Length == 1) return new EndBlockStmt(Current);
        else if (Current.Type == TokenType.INT && _grouptokens.Length == 1)
        {
            if (int.TryParse(Current.Value, out int duration)) return new Wait(Current, duration, true);
        }
        else if (Current.Type == TokenType.IDENT) return ParseNamedExpression();
        // Handle key statements
        var fullline = Current.Text.Lines[Current.Line - 1].Text;
        fullline = fullline.Contains('#') ? fullline[..fullline.IndexOf('#')] : fullline;
        return ParseKey(fullline.Trim());
    }

    #endregion

    #region Statement Parsers

    private ConstantDeclStmt? ParseConstantDecl()
    {
        var constvar = Advance();
        var des = (VariableExpr)Formatter.GetValueEx(constvar);
        var op = Match(TokenType.ASSIGN);
        var eexp = ParseExpression();
        if (!CursorEOF) return null;
        return new ConstantDeclStmt(constvar, des, op, eexp);
    }

    private ImportStmt? ParseImport()
    {
        if (_grouptokens.Length != 2) return null;
        if (Peek(1).Type == TokenType.STRING)
        {
            var libSrc = Path.Combine(_filePath, LibPath, Peek(1).STRTrimQ());
            libSrc = Path.GetFullPath(libSrc);
            if (!libSrc.StartsWith(_filePath, StringComparison.OrdinalIgnoreCase)) throw new Exception($"库文件路径非法");
            if (!File.Exists(libSrc)) throw new Exception($"文件不存在:{libSrc}");
            return new ImportStmt(Current, Peek(1), Path.Combine(_filePath, LibPath));
        }
        return null;
    }

    private AssignmentStmt? ParseAssignment()
    {
        var destok = Advance();
        var des = (VariableExpr)Formatter.GetValueEx(destok);
        if (!Current.Type.OperatorIsAug() && Current.Type != TokenType.ASSIGN)
        {
            return null;
        }
        var op = Advance();
        var eexp = ParseExpression();
        if (!CursorEOF) return null;
        return new AssignmentStmt(destok, des, op, eexp);
    }

    private ReturnStmt? ParseReturn()
    {
        var returnTok = Advance();
        if (!CursorEOF)
        {
            var eexp = ParseExpression();
            if (!CursorEOF) return null;
            return new ReturnStmt(returnTok, eexp);
        }
        return new ReturnStmt(returnTok);
    }

    private Statement? ParseIfelse()
    {
        var iftok = Advance();
        if (CursorEOF) return null;
        var expr = ParseExpression();
        if (!CursorEOF) return null;
        switch (iftok.Type)
        {
            case TokenType.IF:
                return new IfStmt(iftok, expr);
            case TokenType.ELIF:
                return new ElseIf(iftok, expr);
        }
        return null;
    }

    private static bool CheckPrimary(TokenType type)
    {
        return type == TokenType.INT || type == TokenType.CONST || type == TokenType.VAR;
    }

    private Statement? ParseFor()
    {
        if (_grouptokens.Length == 1)
        {
            return new For_Infinite(Current);
        }
        if (_grouptokens.Length == 2 && CheckPrimary(Peek(1).Type))
        {
            return new For_Static(Current, Formatter.GetValueEx(Peek(1)));
        }
        if (_grouptokens.Length == 6)
        {
            if (Peek(1).Type == TokenType.VAR && Peek(2).Type == TokenType.ASSIGN
            && CheckPrimary(Peek(3).Type)
            && Peek(4).Type == TokenType.TO
            && CheckPrimary(Peek(5).Type))
            {
                return new For_Full(Current, (VariableExpr)Formatter.GetValueEx(Peek(1)), Formatter.GetValueEx(Peek(3)), Formatter.GetValueEx(Peek(5)));
            }
        }
        return null;
    }

    private WhileStmt? ParseWhile()
    {
        if (_grouptokens.Length < 2) return null;
        var start = Advance();
        var expr = ParseExpression();
        if (!CursorEOF) return null;
        return new WhileStmt(start, expr);
    }

    private Statement? ParseLoopCtrl()
    {
        if (_grouptokens.Length > 2) return null;
        switch (Current.Type)
        {
            case TokenType.BREAK:
                if (_grouptokens.Length == 2 && Peek(1).Type == TokenType.INT)
                {
                    if (!uint.TryParse(Peek(1).Value, out var level)) return null;
                    return new Break(Current, level);
                }
                else if (_grouptokens.Length == 1)
                {
                    return new Break(Current);
                }
                break;
            case TokenType.CONTINUE:
                if (_grouptokens.Length == 1)
                {
                    return new Continue(Current);
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
        m = Regex.Match(text, $@"^({GPKey})\s+{Formatter.ValueEx}$", RegexOptions.IgnoreCase);
        if (m.Success && NSKeys.GetKey(m.Groups[1].Value) != GamePadKey.None)
            return new KeyPress(m.Groups[1].Value, Formatter.GetValueEx(m.Groups[2].Value));
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
            if (NSKeys.GetKey(keyname) == GamePadKey.None) return null;
            return new StickAct(keyname, "RESET");
        }
        m = Regex.Match(text, @"^([lr]s)\s+([a-z0-9]+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var keyname = m.Groups[1].Value;
            var direction = m.Groups[2].Value;
            if (NSKeys.GetKey(keyname) == GamePadKey.None) return null;
            if (!NSKeys.CheckDirection(direction, out _)) return null;
            return new StickAct(keyname, direction);
        }
        m = Regex.Match(text, $@"^([lr]s)\s+([a-z0-9]+)\s*,\s*({Formatter.ValueEx})$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var keyname = m.Groups[1].Value;
            var direction = m.Groups[2].Value;
            var duration = m.Groups[3].Value;
            if (NSKeys.GetKey(keyname) == GamePadKey.None) return null;
            if (!NSKeys.CheckDirection(direction, out _)) return null;
            return new StickPress(keyname, direction, Formatter.GetValueEx(duration));
        }
        return null;
    }

    private Statement? ParseNamedExpression()
    {
        var first = Current;
        switch (first.Value.ToLower())
        {
            case "print":
                Advance();
                var outstr = ParseArgumentList();
                return new CallStmt(first, first.Value.ToUpper(), [.. outstr], CallType.CallStmtWithArgs);
            case "wait":
                if (_grouptokens.Length != 2) return null;
                if (Peek(1).Type == TokenType.CONST || Peek(1).Type == TokenType.INT || Peek(1).Type == TokenType.VAR)
                {
                    return new Wait(first, Formatter.GetValueEx(Peek(1)));
                }
                break;
            case "call":
                if (_grouptokens.Length != 2) return null;
                return Peek(1).Type == TokenType.IDENT ? new CallStmt(first, Peek(1).Value, []) : null;
#if DEBUG
            case "sprint":
            case "smem":
                if (_grouptokens.Length != 2) return null;
                if (Peek(1).Type == TokenType.INT)
                {
                    var ism = first.Value.Equals("smem", StringComparison.CurrentCultureIgnoreCase);
                    return new SerialPrint(uint.Parse(Peek(1).Value), ism);
                }
                break;
#endif
            default:
                if (_grouptokens.Length < 2) return null;
                Advance();
                var args = ParseArgumentList();
                return new CallStmt(first, first.Value, [.. args], CallType.CallStmtWithArgs);
        }
        return null;
    }

    private ImmutableArray<ExprBase> ParseArgumentList()
    {
        if (CursorEOF) return [];
        var args = ParseArguments();
        if (!CursorEOF) throw new Exception("函数传参解析失败");
        return args;
    }

    #endregion

    #region Expression Parsers

    private ExprBase ParseExpression(int parentPrecedence = 0)
    {
        ExprBase left;
        var unaryOperatorPrecedence = Current.Type.GetUnaryOperatorPrecedence();
        if (unaryOperatorPrecedence != 0 && unaryOperatorPrecedence >= parentPrecedence)
        {
            var opToken = Advance();
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
            var right = ParseExpression(precedence);
            left = new BinaryExpression(opToken, left, right);
        }

        return left;
    }

    private ExprBase ParsePrimary()
    {
        switch (Current.Type)
        {
            case TokenType.STRING:
            case TokenType.CONST:
            case TokenType.VAR:
            case TokenType.EX_VAR:
                var token = Advance();
                if (token.Type == TokenType.VAR && Check(TokenType.LeftBracket))
                {
                    return ParseSliceExpression(token);
                }
                return Formatter.GetValueEx(token);
            case TokenType.LeftBracket:
                return ParseIndexDefExpression();
            case TokenType.LeftParen:
                var lp = Advance();
                var expression = ParseExpression();
                var rp = Match(TokenType.RightParen);
                return new ParenthesizedExpression(lp, expression, rp);
            case TokenType.IDENT:
                return ParseCallExpression();
            case TokenType.INT:
            default:
                var toknum = Advance();
                if (!int.TryParse(toknum.Value, out _))
                    throw new Exception($"错误的数字格式: {toknum.Value}");
                return Formatter.GetValueEx(toknum);
        }
    }

    private ExprBase ParseCallExpression()
    {
        var identifier = Match(TokenType.IDENT);
        var openParen = Match(TokenType.LeftParen);
        var arguments = ParseArguments();
        var closeParen = Match(TokenType.RightParen);
        return new Callv1Expression(identifier, openParen, arguments, closeParen);
    }

    // [1,2,3]
    private ExprBase ParseIndexDefExpression()
    {
        var lb = Match(TokenType.LeftBracket, "语法需要'['");
        var items = ImmutableArray.CreateBuilder<ExprBase>();

        var parseNext = true;
        while (parseNext && !Check(TokenType.RightBracket) && !CursorEOF)
        {
            items.Add(ParsePrimary());
            if (Check(TokenType.COMMA))
                Advance();
            else
                parseNext = false;
        }
        if (items.Count == 0) throw new Exception("索引定义语法错误");

        var rb = Match(TokenType.RightBracket, "语法需要']'");
        return new IndexDefExpression(lb, [.. items], rb);
    }

    // $var[expr] or $var[start:end]
    private ExprBase ParseSliceExpression(Token variableToken)
    {
        var lb = Match(TokenType.LeftBracket, "语法需要'['");

        var ommitstart = Check(TokenType.COLON);
        var start = Check(TokenType.COLON) ? new LiteralExpr(0) : ParsePrimary();
        // [expr]
        if (Check(TokenType.RightBracket))
        {
            var rb = Match(TokenType.RightBracket, "语法需要']'");
            return new IndexVisitExpression((VariableExpr)Formatter.GetValueEx(variableToken), lb, start, rb);
        }
        Match(TokenType.COLON, "语法不正确[<start>:<end>]");

        var end = Check(TokenType.RightBracket) ? new LiteralExpr("") : ParsePrimary();
        Match(TokenType.RightBracket, "语法需要']'");
        return new SliceExpression((VariableExpr)Formatter.GetValueEx(variableToken), start, end, ommitstart);
    }

    private ImmutableArray<ExprBase> ParseArguments()
    {
        var args = ImmutableArray.CreateBuilder<ExprBase>();
        var parseNext = true;
        while (parseNext && !Check(TokenType.RightParen) && !CursorEOF)
        {
            args.Add(ParseExpression());
            if (Check(TokenType.COMMA))
                Advance();
            else
                parseNext = false;
        }
        return args.ToImmutable();
    }

    #endregion

    #region Function Declaration Parsers

    private FuncStmt? ParseFuncDecl()
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

        if (!CursorEOF) throw new Exception("函数声明语法错误");
        return new FuncStmt(functionName, parameters, hasPar, typeClause);
    }

    private ImmutableArray<ParameterSyntax> ParseParameterList()
    {
        var parameters = ImmutableArray.CreateBuilder<ParameterSyntax>();

        var parseNext = true;
        while (parseNext && !Check(TokenType.RightParen) && !CursorEOF)
        {
            parameters.Add(ParseParameter());
            if (Check(TokenType.COMMA))
                Advance();
            else
                parseNext = false;
        }
        return parameters.ToImmutable();
    }

    private ParameterSyntax ParseParameter()
    {
        var identifier = Match(TokenType.VAR, "函数参数只能是变量");
        var parameter = (VariableExpr)Formatter.GetValueEx(identifier);
        var type = ParseOptionalTypeClause();
        return new ParameterSyntax(parameter, type);
    }

    private TypeClauseSyntax? ParseOptionalTypeClause()
    {
        if (!Check(TokenType.COLON))
            return null;
        return ParseTypeClause();
    }

    private TypeClauseSyntax ParseTypeClause()
    {
        var colonToken = Match(TokenType.COLON);
        var identifier = Match(TokenType.IDENT);
        return new TypeClauseSyntax(colonToken, identifier);
    }

    #endregion

    private Token Peek(int offset = 0)
    {
        var index = _position + offset;
        if (index >= _grouptokens.Length)
            return _grouptokens[^1];

        return _grouptokens[index];
    }
    private Token Current => Peek();
    private Token Advance()
    {
        var now = Current;
        _position++;
        return now;
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
}
