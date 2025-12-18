using EasyScript.Statements;
using EasyCon.Script2.Syntax;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing;

internal partial class Parser
{
    private static bool IsValidVariable(string variable)
    {
        return !string.IsNullOrEmpty(variable) && Regex.Match(variable, $"^{Formats.ValueEx}$", RegexOptions.IgnoreCase).Success;
    }

    private Statement? ParseConstantDecl(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer.Count() != 3 + 2)
            return null;
        if (lexer[0].Type == TokenType.CONST && lexer[1].Type == TokenType.ASSIGN && (lexer[2].Type == TokenType.INT || lexer[2].Type == TokenType.CONST))
        {
            if (_formatter.TryDeclConstant(lexer[0].Value, lexer[2].Value))
                return new Empty($"{lexer[0].Value} = {lexer[2].Value}");
            else
                throw new ParseException($"重复定义的常量：{lexer[0].Value}");
        }
        return null;
    }

    private Statement? ParseAssignment(string text)
    {
        var pre = Regex.Match(text, $@"^{Formats.RegisterEx}\s*=\s*(.*)$", RegexOptions.IgnoreCase);
        if (pre.Success)
        {
            var des = FormatterUtil.GetRegEx(pre.Groups[1].Value, true);
            string exprStr = Regex.Replace(pre.Groups[2].Value, @"\s+", "");

            var vR = Regex.Match(exprStr, $@"^{Formats.ValueEx}");
            if (vR.Success)
            {
                var vr = vR.Value;
                // 尝试找到算术运算符
                Meta? op = null;
                int operatorIndex = -1;

                foreach (var o in OpList())
                {
                    int index = exprStr.IndexOf(o.Operator, StringComparison.Ordinal);
                    if (index > 0) // 运算符不能在开头
                    {
                        op = o;
                        operatorIndex = index;
                        break;
                    }
                }
                if (op != null)
                {
                    // 有算术运算符的情况
                    string var1 = exprStr.Substring(0, operatorIndex);
                    string var2 = exprStr.Substring(operatorIndex + op.Operator.Length);

                    if (IsValidVariable(var1) && IsValidVariable(var2))
                    {
                        return new ExpressionStmt(des, _formatter.GetValueEx(var1), op, _formatter.GetValueEx(var2));
                    }
                }
                else if (IsValidVariable(exprStr))
                {
                    return new ExpressionStmt(des, _formatter.GetValueEx(vR.Groups[1].Value), null, null);
                }
            }
        }

        //aug assign
        foreach (var p in AsmParser())
        {
            var st = p.Parse(new ParserArgument
            {
                Text = text,
                Formatter = _formatter,
            });
            if (st != null) return st;
        }
        return null;
    }

    private Statement? ParseIfelse(string text, bool elif = false)
    {
        foreach (var op in CompareOperator.All)
        {
            var m = Regex.Match(text, $@"^if\s+{Formats.VariableEx}\s*{op.Operator}\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new IfStmt(op, _formatter.GetVar(m.Groups[1].Value), _formatter.GetValueEx(m.Groups[2].Value));
            // else if
            m = Regex.Match(text, $@"^elif\s+{Formats.VariableEx}\s*{op.Operator}\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new ElseIf(op, _formatter.GetVar(m.Groups[1].Value), _formatter.GetValueEx(m.Groups[2].Value));
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
            return new For_Full(FormatterUtil.GetRegEx(m.Groups[1].Value, true), _formatter.GetValueEx(m.Groups[2].Value), _formatter.GetValueEx(m.Groups[3].Value));
        return null;
    }

    private Statement? ParseLoopCtrl(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text).Take(2).ToList();

        switch (tokens[0].Type)
        {
            case TokenType.BREAK:
                if (tokens[1].Type == TokenType.CONST || tokens[1].Type == TokenType.INT)
                {
                    return new Break(_formatter.GetInstant(tokens[1].Value, true));
                }
                else if (tokens[1].Type == TokenType.NEWLINE)
                {
                    return new Break();
                }
                break;
            case TokenType.CONTINUE:
                if (tokens[1].Type == TokenType.NEWLINE)
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

    private Statement? ParseNamedExpression(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text);
        var first = tokens.First()!;
        if(tokens.Count() > 2+2) return null;
        switch (first.Value.ToLower())
        {
            case "func":
                return tokens[1].Type == TokenType.IDENT ? new FunctionStmt(tokens[1].Value) : null;
            case "call":
                return tokens[1].Type == TokenType.IDENT ? new CallStat(tokens[1].Value) : null;
            case "alert":
            case "print":
                if (tokens[1].Type == TokenType.STRING && tokens[2].Type == TokenType.NEWLINE)
                {
                    var contents = ParseContents(tokens[1].Value, out bool _);

                    return new BuildinFunc(first.Value.ToUpper(), contents.ToArray());
                }
                else if (tokens[1].Type == TokenType.NEWLINE)
                {
                    return new BuildinFunc(first.Value.ToUpper(), []);
                }
                break;
            case "time":
                if (tokens[1].Type == TokenType.VAR && tokens[2].Type == TokenType.NEWLINE)
                    return new BuildinFunc("TIME", [new RegParam(FormatterUtil.GetRegEx(tokens[1].Value))]);
                break;
            case "wait":
                if (tokens[1].Type == TokenType.CONST || tokens[1].Type == TokenType.INT || tokens[1].Type == TokenType.VAR)
                {
                    return new Wait(_formatter.GetValueEx(tokens[1].Value));
                }
                break;
            case "amiibo":
                if (tokens[1].Type == TokenType.CONST || tokens[1].Type == TokenType.INT || tokens[1].Type == TokenType.VAR)
                {
                    var amiiboidx = _formatter.GetValueEx(tokens[1].Value);
                    return new AmiiboChanger(amiiboidx);
                }
                break;
        }

        return null;
    }

    private List<Param> ParseContents(string text, out bool cancellinebreak)
    {
        var contents = new List<Param>();
        cancellinebreak = false;

        var strs = text.Split('&');
        for (var i = 0; i < strs.Length; i++)
        {
            var s = strs[i].Trim();
            if (i == strs.Length - 1 && s == "\\")
            {
                contents.Add(new TextParam("", s));
                cancellinebreak = true;
                continue;
            }
            if (s.Length == 0 && strs.Length > 1)
            {
                contents.Add(new TextParam(" ", ""));
                continue;
            }
            var m = Regex.Match(s, Formats.RegisterEx_F);
            if (m.Success)
            {
                contents.Add(new RegParam(FormatterUtil.GetRegEx(s)));
                continue;
            }
            m = Regex.Match(s, Formats.Constant_F);
            if (m.Success)
            {
                var v = _formatter.GetConstant(s);
                contents.Add(new TextParam(v.Val.ToString(), s));
                continue;
            }
            contents.Add(new TextParam(s));
        }

        return contents;
    }

    private Statement? ParseDebug(string text)
    {
#if DEBUG
        var m = Regex.Match(text, @"^sprint\s+(\d+)$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new SerialPrint(uint.Parse(m.Groups[1].Value), false);
        m = Regex.Match(text, @"^smem\s+(\d+)$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new SerialPrint(uint.Parse(m.Groups[1].Value), true);
#endif
        return null;
    }
}
