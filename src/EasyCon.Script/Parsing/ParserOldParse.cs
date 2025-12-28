using EasyScript.Statements;
using EasyCon.Script2.Syntax;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing;

internal partial class Parser
{
    private Statement? ParseConstantDecl(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer[0].Type == TokenType.CONST && lexer[1].Type == TokenType.ASSIGN)
        {
            var eexp = ParseExpression([.. lexer.Skip(2)], false);
            if (_formatter.TryDeclConstant(lexer[0].Value, eexp))
                return new Empty($"{lexer[0].Value} = {eexp.GetCodeText()}");
            else
                throw new Exception($"重复定义的常量：{lexer[0].Value}");
        }
        return null;
    }
    
    private ValBase ParseExpression(List<Token> tokens, bool allowVar = true)
    {
        // 用于存储数字/变量的节点栈
        Stack<ValBase> valueStack = new();
        // 用于存储运算符的栈
        Stack<Token> opStack = new();
        // 主循环处理所有token
        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];

            switch (token.Type)
            {
                case TokenType.VAR when allowVar:
                case TokenType.EX_VAR:
                case TokenType.INT:
                case TokenType.CONST:
                    valueStack.Push(_formatter.GetValueEx(token.Value));
                    break;
                case TokenType.LeftParen:
                    opStack.Push(token);
                    break;

                case TokenType.RightParen:
                    // 处理到左括号为止的所有运算符
                    while (opStack.Count > 0 && opStack.Peek().Type != TokenType.LeftParen)
                    {
                        ApplyOperator(opStack, valueStack, true);
                    }
                    // 弹出左括号
                    if (opStack.Count > 0) opStack.Pop();
                    break;
                default:
                    if(token.Type.GetBinaryOperatorPrecedence() > 3)
                    {
                        // 当栈顶运算符优先级不低于当前运算符时，先处理栈顶运算符
                        while (opStack.Count > 0 && 
                               opStack.Peek().Type.GetBinaryOperatorPrecedence() >= token.Type.GetBinaryOperatorPrecedence())
                        {
                            ApplyOperator(opStack, valueStack);
                        }
                        
                        opStack.Push(token);
                    }
                    else if(token.Type == TokenType.EOF)break;
                    else
                    {
                        throw new Exception($"不支持的运算符：{token.Value}");
                    }
                    break;
            }
        }

        // 处理剩余的运算符
        while (opStack.Count > 0)
        {
            ApplyOperator(opStack, valueStack);
        }

        if(valueStack.Count != 1)
        {
            throw new Exception("表达式语法错误");
        }
        // 栈顶就是完整的表达式树
        return valueStack.Pop();
    }
    // 应用运算符：从栈中弹出两个操作数和一个运算符，创建BinExpr
    private static void ApplyOperator(Stack<Token> opStack, Stack<ValBase> valueStack, bool hasPr = false)
    {
        if (opStack.Count == 0 || valueStack.Count < 2)
            throw new Exception("表达式语法错误");
        string opStr = opStack.Pop().Value;
        Meta? op = OpList().FirstOrDefault(o=> o.Operator == opStr);
        ValBase right = valueStack.Pop();
        ValBase left = valueStack.Pop();
        valueStack.Push(new BinExpr(left, op, right, hasPr));
    }

    private Statement? ParseAssignment(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer[0].Type == TokenType.VAR && lexer[1].Type == TokenType.ASSIGN)
        {
            var des = FormatterUtil.GetRegEx(lexer[0].Value, true);
            var eexp = ParseExpression([.. lexer.Skip(2)]);

            return new ExpressionStmt(des, eexp);
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
        var tok = SyntaxTree.ParseTokens(text);
        if(tok.Count() > 2+1) return null;
        var tokens = tok.Take(2).ToList();

        switch (tokens[0].Type)
        {
            case TokenType.BREAK:
                if (tokens[1].Type == TokenType.CONST || tokens[1].Type == TokenType.INT)
                {
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

    private Statement? ParseNamedExpression(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text);
        var first = tokens.First()!;
        if(tokens.Count() > 2+1) return null;
        switch (first.Value.ToLower())
        {
            case "func":
                return tokens[1].Type == TokenType.IDENT ? new FunctionStmt(tokens[1].Value) : null;
            case "call":
                return tokens[1].Type == TokenType.IDENT ? new CallStat(tokens[1].Value) : null;
            case "alert":
            case "print":
                if (tokens[1].Type == TokenType.STRING && tokens[2].Type == TokenType.EOF)
                {
                    var contents = ParseContents(tokens[1].Value, out bool _);

                    return new BuildinFunc(first.Value.ToUpper(), contents.ToArray());
                }
                else if (tokens[1].Type == TokenType.EOF)
                {
                    return new BuildinFunc(first.Value.ToUpper(), []);
                }
                break;
            case "time":
            case "rand":
                if (tokens[1].Type == TokenType.VAR && tokens[2].Type == TokenType.EOF)
                    return new BuildinFunc(first.Value.ToUpper(), [new RegParam(FormatterUtil.GetRegEx(tokens[1].Value))]);
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
#if DEBUG
            case "sprint":
            case "smem":
                if(tokens[1].Type == TokenType.INT && tokens[2].Type == TokenType.EOF)
                {
                    return new SerialPrint(uint.Parse(tokens[1].Value), first.Value.Equals("smem", StringComparison.CurrentCultureIgnoreCase));
                }
                break;
#endif
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
}
