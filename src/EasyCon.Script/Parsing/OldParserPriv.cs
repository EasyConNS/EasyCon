using EasyCon.Script2.Syntax;
using EasyScript.Statements;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing;

internal partial class Parser
{
    private Empty? ParseConstantDecl(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer[0].Type == TokenType.CONST && lexer[1].Type == TokenType.ASSIGN)
        {
            var eexp = ParseExpression([.. lexer.Skip(2)], false);
            if (_formatter.TryDeclConstant(lexer[0].Value, eexp))
                return new Empty($"{lexer[0].Value} = {eexp.GetCodeText()}");
            throw new Exception($"重复定义的常量：{lexer[0].Value}");
        }
        return null;
    }
    
    private UnaryOp? ParseUnary(VariableExpr dest, List<Token> tokens)
    {
        if(tokens.Count == 3 && tokens[0].Type.GetUnaryOperatorPrecedence() > 0 && tokens[1].Type == TokenType.VAR && tokens[2].Type == TokenType.EOF)
        {
            // 一元运算符
            return tokens[0].Type switch
            {
                TokenType.SUB => new Negative(dest, _formatter.GetReg(tokens[1].Value)),
                TokenType.BitNot => new Not(dest, _formatter.GetReg(tokens[1].Value)),
                _ => null
            };
        }
        return null;
    }

    private ExprBase ParseExpression(List<Token> tokens, bool allowVar = true)
    {
        // 用于存储数字/变量的节点栈
        Stack<ExprBase> valueStack = new();
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
                    if(token.Type.GetBinaryOperatorPrecedence() > 0)
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
    private static void ApplyOperator(Stack<Token> opStack, Stack<ExprBase> valueStack, bool hasPr = false)
    {
        if (opStack.Count == 0 || valueStack.Count < 2)
            throw new Exception("表达式语法错误");
        string opStr = opStack.Pop().Value;
        MetaOperator? op = MetaOperator.All.FirstOrDefault(o=> o.Operator == opStr);
        CompareOperator? cmp = CompareOperator.All.FirstOrDefault(o => o.Operator == opStr);
        ExprBase right = valueStack.Pop();
        ExprBase left = valueStack.Pop();
        if(op == null && cmp  == null) throw new Exception($"不支持的运算符：{opStr}");

        ExprBase result = op != null ? new BinaryExpression(op!, left, right, hasPr) : new CmpExpression(cmp!, left, right, hasPr);
        valueStack.Push(result);
    }

    private Statement? ParseAssignment(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if(lexer.Length < 3+1)return null;
        if (lexer[0].Type == TokenType.VAR && lexer[1].Type == TokenType.ASSIGN)
        {
            var des = _formatter.GetReg(lexer[0].Value, true);

            var sm = ParseUnary(des, [.. lexer.Skip(2)]);
            if(sm!=null)
            {
                return sm;
            }
            var eexp = ParseExpression([.. lexer.Skip(2)]);

            return new ExpressionStmt(des, eexp);
        }

        //aug assign
        if (lexer[0].Type == TokenType.VAR && lexer[1].Type.OperatorIsAug())
        {
            if(lexer.Count() != 3+1)return null;
            var des = _formatter.GetReg(lexer[0].Value, true);
            MetaOperator? op = MetaOperator.All.FirstOrDefault(o=> o.Operator+"=" == lexer[1].Value);
            if(op.OnlyInstant)
            {
                if(lexer[2].Type != TokenType.INT && lexer[2].Type != TokenType.CONST)
                    throw new Exception("Only Instant can be augmented.");
            }
            return (Activator.CreateInstance(op.StatementType, des, _formatter.GetValueEx(lexer[2].Value)) as Statement);
        }

        return null;
    }

    private Statement? ParseIfelse(string text)
    {
        var lexer = SyntaxTree.ParseTokens(text);
        if (lexer.Length < 3 + 1) return null;
        if(lexer[0].Type == TokenType.IF || lexer[0].Type == TokenType.ELIF)
        {
            var eexp = ParseExpression([.. lexer.Skip(1)]);
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
            return new For_Full(_formatter.GetReg(m.Groups[1].Value, true), _formatter.GetValueEx(m.Groups[2].Value), _formatter.GetValueEx(m.Groups[3].Value));
        return null;
    }

    private Statement? ParseLoopCtrl(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text);
        if(tokens.Length > 2+1) return null;
        switch (tokens[0].Type)
        {
            case TokenType.BREAK:
                if (tokens[1].Type == TokenType.CONST || tokens[1].Type == TokenType.INT)
                {
                    if(tokens[2].Type != TokenType.EOF)return null;
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

    private FunctionStmt? ParseFuncDecl(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text);
        if(tokens.Length == 3)
        {
            if(tokens[0].Type == TokenType.FUNC && tokens[1].Type == TokenType.IDENT && tokens[2].Type == TokenType.EOF)
            return new FunctionStmt(tokens[1].Value);
        }

        return null;
    }

    private Statement? ParseNamedExpression(string text)
    {
        var tokens = SyntaxTree.ParseTokens(text);
        if (tokens.Length < 2 + 1) return null;
        var first = tokens.First()!;
        if (first.Type != TokenType.IDENT) return null;
        switch (first.Value.ToLower())
        {
            case "wait":
                if (tokens[1].Type == TokenType.CONST || tokens[1].Type == TokenType.INT || tokens[1].Type == TokenType.VAR)
                {
                    return new Wait(_formatter.GetValueEx(tokens[1].Value));
                }
                break;
            case "amiibo":
                if (tokens[1].Type == TokenType.CONST || tokens[1].Type == TokenType.INT || tokens[1].Type == TokenType.VAR)
                {
                    return new AmiiboChanger(_formatter.GetValueEx(tokens[1].Value));
                }
                break;
            case "call":
                return tokens[1].Type == TokenType.IDENT ? new CallStat(tokens[1].Value, [], false) : null;
#if DEBUG
            case "sprint":
            case "smem":
                if(tokens[1].Type == TokenType.INT && tokens[2].Type == TokenType.EOF)
                {
                    var ism = first.Value.Equals("smem", StringComparison.CurrentCultureIgnoreCase);
                    return new SerialPrint(uint.Parse(tokens[1].Value), ism);
                }
                break;
#endif
            // 内置函数
            default:
                var args = tokens.Skip(1).ToArray();
                var contents = ParseContents(args);
                contents.Reverse();
                return new CallStat(first.Value.ToUpper(), contents);
        }
        return null;
    }

    private Param[] ParseContents(IEnumerable<Token> toks)
    {
        Stack<Param> contents = [];

        var combin = false;
        foreach (var token in toks)
        {
            Param cur = null;
            switch (token.Type)
            {
                case TokenType.STRING:
                    cur = new TextParam(token.Value);
                    break;
                case TokenType.BitAnd:
                    combin = true;
                    continue;
                case TokenType.VAR:
                    cur = new RegParam(_formatter.GetReg(token.Value));
                    break;
                case TokenType.CONST:
                case TokenType.INT:
                    cur = new LiterParam(_formatter.GetInstant(token.Value));
                    break;
                case TokenType.EOF:
                    return contents.ToArray();
                default:
                    throw new Exception($"非法的参数：{token.Value}");
            }
            if (combin)
            {
                if(contents.Count > 0)
                {
                    var prearg = contents.Pop();
                    if(prearg is TextVarParam trpre)
                    {
                        trpre.Params.Add(cur);
                        contents.Push(trpre);
                    }
                    else
                    {
                        contents.Push(new TextVarParam([prearg, cur]));
                    }
                    combin = false;
                }
                else
                {
                    break;
                }
            }
            else
            {
                contents.Push(cur!);
            }
        }
        throw new Exception("参数语句格式错误");
    }
}
