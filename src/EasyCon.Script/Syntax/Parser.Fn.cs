using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

internal partial class Parser
{
    private Statement ParseStatement()
    {
        switch (Current.Type)
        {
            case TokenType.CONST:
                return ParseConstantDecl();
            case TokenType.VAR:
                return ParseAssignment();
            case TokenType.IMPORT:
                return ParseImport();
            case TokenType.IF:
            case TokenType.ELIF:
                return ParseIfelse();
            case TokenType.ELSE when _grouptokens.Length == 1:
                return new Else(Current);
            case TokenType.ENDIF when _grouptokens.Length == 1:
                return new EndIf(Current);
            case TokenType.WHILE:
                return ParseWhile();
            case TokenType.FOR:
                return ParseFor();
            case TokenType.BREAK:
            case TokenType.CONTINUE:
                return ParseLoopCtrl();
            case TokenType.NEXT when _grouptokens.Length == 1:
                return new Next(Current);
            case TokenType.EXTERN:
                return ParseExternFunc();
            case TokenType.FUNC:
                return ParseFuncDecl();
            case TokenType.ENDFUNC when _grouptokens.Length == 1:
                return new EndFuncStmt(Current);
            case TokenType.RETURN:
                return ParseReturn();
            case TokenType.END when _grouptokens.Length == 1:
                return new EndBlockStmt(Current);
            case TokenType.INT when _grouptokens.Length == 1:
                var ok = int.TryParse(Current.Value, out var duration);
                if (!ok) _diagnostics.ReportInvalidNumber(Current.Location, Current.Value);
                return new Wait(Current, new LiteralExpr(Current, duration), true);
            case TokenType.ButtonKeyword or TokenType.StickKeyword:
                {
                    return ParsePadButtonStatement();
                }
            case TokenType.IDENT:
            default:
                return ParseNamedExpression();
        }
    }

    #region Statement Parsers

    private ConstantDeclStmt ParseConstantDecl()
    {
        var constvar = Advance();
        var des = (VariableExpr)Formatter.GetValueEx(constvar);
        var op = Match(TokenType.ASSIGN);
        var eexp = ParseExpression();
        MatchEOF();
        return new ConstantDeclStmt(constvar, des, op, eexp);
    }

    private ImportStmt ParseImport()
    {
        var keyword = Match(TokenType.IMPORT);
        var mod = Match(TokenType.STRING);
        MatchEOF();
        var libSrc = Path.GetFullPath(Path.Combine(_filePath, LibPath, mod.STRTrimQ()));
        if (!libSrc.StartsWith(_filePath, StringComparison.OrdinalIgnoreCase) || !File.Exists(libSrc))
            _diagnostics.ReportInvalidImport(mod.Location, mod);
        return new ImportStmt(keyword, mod, Path.Combine(_filePath, LibPath));
    }

    private AssignmentStmt ParseAssignment()
    {
        var destok = Advance();
        var des = (VariableExpr)Formatter.GetValueEx(destok);
        var op = Match(t => t == TokenType.ASSIGN || t.OperatorIsAug());
        var eexp = ParseExpression();
        MatchEOF();
        return new AssignmentStmt(destok, des, op, eexp);
    }

    private ReturnStmt ParseReturn()
    {
        var returnTok = Match(TokenType.RETURN);
        var eexp = CursorEOF ? null : ParseExpression();
        MatchEOF();
        return new ReturnStmt(returnTok, eexp);
    }

    private Statement ParseIfelse()
    {
        var iftok = Advance();
        var expr = ParseExpression();
        MatchEOF();
        return iftok.Type switch
        {
            TokenType.IF => new IfStmt(iftok, expr),
            _ => new ElseIf(iftok, expr),
        };
    }

    private Statement ParseFor()
    {
        var forToken = Advance();
        if (CursorEOF) // for next
        {
            return new For_Infinite(forToken);
        }
        var loopc = Match(type => type == TokenType.INT || type == TokenType.CONST || type == TokenType.VAR);
        if (CursorEOF) // for count next
        {
            return new For_Static(forToken, Formatter.GetValueEx(loopc));
        }
        if (loopc.Type != TokenType.VAR)
            _diagnostics.ReportUnexpectedToken(loopc.Location, loopc, TokenType.VAR);
        Match(TokenType.ASSIGN);
        var lower = Match(type => type == TokenType.INT || type == TokenType.CONST || type == TokenType.VAR);
        Match(TokenType.TO);
        var upper = Match(type => type == TokenType.INT || type == TokenType.CONST || type == TokenType.VAR);
        return new For_Full(forToken, (VariableExpr)Formatter.GetValueEx(loopc), Formatter.GetValueEx(lower), Formatter.GetValueEx(upper));
    }

    private WhileStmt ParseWhile()
    {
        var start = Advance();
        var expr = ParseExpression();
        MatchEOF();
        return new WhileStmt(start, expr);
    }

    private Statement ParseLoopCtrl()
    {
        if (Check(TokenType.BREAK))
        {
            var berk = Match(TokenType.BREAK);
            if (CursorEOF) return new Break(berk);
            var levelT = Match(TokenType.INT);
            MatchEOF();
            if (!uint.TryParse(levelT.Value, out var level)) _diagnostics.ReportInvalidNumber(levelT.Location, levelT.Value);
            return new Break(berk, level);
        }
        else
        {
            var contT = Match(TokenType.CONTINUE);
            MatchEOF();
            return new Continue(contT);
        }
    }

    /// <summary>
    /// 解析手柄按键语句
    // 键位码 [持续时间(ms)|DOWN|UP]
    // LS|RS 方向|角度 [, 持续时间(ms)]
    // LS|RS RESET
    /// </summary>
    private Statement ParsePadButtonStatement()
    {
        var firstKey = Advance();

        if (firstKey.Type == TokenType.StickKeyword)
        {
            switch (Current.Type)
            {
                case TokenType.INT:
                case TokenType.DirectionKeyword:
                    var state = Advance();

                    if (Check(TokenType.COMMA))
                    {
                        Advance();
                        var duration = Match(type => type == TokenType.INT || type == TokenType.CONST || type == TokenType.VAR);
                        MatchEOF();
                        var value = int.Parse(duration.Value);
                        return new StickPress(firstKey, state.Value, Formatter.GetValueEx(duration));
                    }
                    else
                    {
                        MatchEOF();
                        return new StickAct(firstKey, state.Value);
                    }
                case TokenType.ResetKeyword:
                    Advance();
                    MatchEOF();
                    return new StickAct(firstKey, "RESET");
            }
        }
        else
        {
            if (CursorEOF)
            {
                return new KeyPress(firstKey);
            }
            else if (Check(TokenType.INT) || Check(TokenType.CONST) || Check(TokenType.VAR))
            {
                var state = Advance();
                MatchEOF();
                return new KeyPress(firstKey, Formatter.GetValueEx(state));
            }
            else
            {
                var state = Match(TokenType.StateKeyword);
                MatchEOF();
                var isUp = state.Value.Equals("UP", StringComparison.CurrentCultureIgnoreCase);
                return new KeyAct(firstKey, isUp);
            }
        }
        _diagnostics.ReportInvalidKeyActionStatement(firstKey.Location, firstKey);
        return new KeyAct(firstKey);
    }
    const string GPKey = "[ABXYLR]|Z[LR]|[LR]CLICK|HOME|CAPTURE|PLUS|MINUS|LEFT|RIGHT|UP|DOWN|DOWNLEFT|DOWNRIGHT|UPLEFT|UPRIGHT";

    private Statement ParseNamedExpression()
    {
        var first = Match(TokenType.IDENT);
        switch (first.Value.ToLower())
        {
            case "wait":
                var durat = Match(type => type == TokenType.INT || type == TokenType.CONST || type == TokenType.VAR);
                MatchEOF();
                return new Wait(first, Formatter.GetValueEx(durat));
            case "call":
                var fnname = Match(TokenType.IDENT);
                MatchEOF();
                return new CallStmt(first, fnname.Value, []);
#if DEBUG
            case "sprint":
            case "smem":
                var idxreg = Match(TokenType.INT);
                MatchEOF();
                var ism = first.Value.Equals("smem", StringComparison.CurrentCultureIgnoreCase);
                return new SerialPrint(uint.Parse(Peek(1).Value), ism);
#endif
            default:
                if (CursorEOF)
                {
                    _diagnostics.ReportInvalidExpressionStatement(first.Location);
                }
                var args = ParseArguments();
                if (SyntaxTree.LegacyCompat && first.Value.Equals("print", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (Check(TokenType.BitAnd)) Advance();
                }
                MatchEOF();
                return new CallStmt(first, first.Value, [.. args], CallType.CallStmtWithArgs);
        }
    }

    #endregion

    #region Expression Parsers

    private ExprBase ParseExpression(int parentPrecedence = 0)
    {
        ExprBase left;
        var unaryOperatorPrecedence = Current.Type.GetUnaryOperatorPrecedence();
        if (unaryOperatorPrecedence != 0 && unaryOperatorPrecedence >= parentPrecedence && !CursorEOF)
        {
            var opToken = Advance();
            var operand = ParseExpression(unaryOperatorPrecedence);
            left = new UnaryExpression(opToken, operand);
        }
        else
        {
            left = ParsePrimary();
        }

        while (true && !CursorEOF)
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
                var tokenct = Advance();
                return Formatter.GetValueEx(tokenct);
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
                var toknum = Advance();
                var ok = int.TryParse(toknum.Value, out var intval);
                if (!ok) _diagnostics.ReportInvalidNumber(toknum.Location, toknum.Value);
                return new LiteralExpr(toknum, intval);
            case TokenType.Number:
                var tokdbl = Advance();
                var okd = double.TryParse(tokdbl.Value, out var dblval);
                if (!okd) _diagnostics.ReportInvalidNumber(tokdbl.Location, tokdbl.Value);
                return new LiteralExpr(tokdbl, dblval);
            default:
                var tokdef = Advance();
                _diagnostics.ReportUnexpectedToken(tokdef.Location, tokdef, TokenType.INT);
                return new LiteralExpr(tokdef, 0);
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
        var rb = Match(TokenType.RightBracket, "语法需要']'");
        if (items.Count == 0)
        {
            // TODO
        }
        return new IndexDefExpression(lb, [.. items], rb);
    }

    // $var[expr] or $var[start:end]
    private ExprBase ParseSliceExpression(Token variableToken)
    {
        var lb = Match(TokenType.LeftBracket, "语法需要'['");

        var ommitstart = Check(TokenType.COLON);
        var start = Check(TokenType.COLON) ? new LiteralExpr(Current, 0) : ParsePrimary();
        // [expr]
        if (Check(TokenType.RightBracket))
        {
            var rb = Advance();
            return new IndexVisitExpression(variableToken, lb, start, rb);
        }
        // [start:end]
        Match(TokenType.COLON, "语法不正确[<start>:<end>]");

        var end = Check(TokenType.RightBracket) ? new LiteralExpr(Current, "") : ParsePrimary();
        Match(TokenType.RightBracket, "语法需要']'");
        return new SliceExpression(variableToken, start, end, ommitstart);
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

    private FuncStmt ParseFuncDecl()
    {
        var functionToken = Advance();
        var functionName = Match(TokenType.IDENT, "函数声明需要函数名");

        var hasPar = false;
        var parameters = ImmutableArray<ParameterSyntax>.Empty;
        if (Check(TokenType.LeftParen))
        {
            Match(TokenType.LeftParen);
            hasPar = true;
            parameters = ParseParameterList();
            Match(TokenType.RightParen, "函数声明缺少右括号");
        }
        var typeClause = ParseOptionalTypeClause();

        MatchEOF();
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

    private ExternFuncStmt ParseExternFunc()
    {
        var externToken = Advance(); // consume EXTERN

        // Expect FUNC keyword
        var funcToken = Match(TokenType.FUNC, "EXTERN 后需要 FUNC 关键字");

        // Function name
        var functionName = Match(TokenType.IDENT, "EXTERN FUNC 需要函数名");

        // Parameter list (parentheses required)
        Match(TokenType.LeftParen, "EXTERN FUNC 声明需要左括号");
        var parameters = ParseParameterList();
        Match(TokenType.RightParen, "EXTERN FUNC 声明缺少右括号");

        // Return type (required)
        var returnType = ParseTypeClause();

        // FROM keyword
        var fromToken = Match(TokenType.FROM, "EXTERN FUNC 声明需要 FROM 关键字");

        // Library path string
        var libraryPath = Match(TokenType.STRING, "FROM 后需要库路径字符串");

        MatchEOF();
        return new ExternFuncStmt(externToken, functionName, parameters, returnType, fromToken, libraryPath);
    }

    #endregion
}