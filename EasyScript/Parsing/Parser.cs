using EasyScript.Parsing.Statements;
using ECDevice;
using System.Reflection;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing;

class Parser
{
    readonly Formatter _formatter;

    static IEnumerable<Meta> OpList()
    {
        var types = from domainAssembly in AppDomain.CurrentDomain.GetAssemblies()
                    from assemblyType in domainAssembly.GetTypes()
                    where assemblyType.IsSubclassOf(typeof(BinaryOp))
                    where assemblyType.GetField("_Meta", BindingFlags.NonPublic | BindingFlags.Static) != null
                    select assemblyType.GetField("_Meta", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) as Meta;
        return types;
    }

    static IEnumerable<IStatementParser> AsmParser()
    {
        var types = from domainAssembly in AppDomain.CurrentDomain.GetAssemblies()
                    from assemblyType in domainAssembly.GetTypes()
                    where assemblyType.IsSubclassOf(typeof(BinaryOp)) |
                       assemblyType.IsSubclassOf(typeof(UnaryOp)) |
                       assemblyType.IsSubclassOf(typeof(UnaryOpEx))
                    where assemblyType.GetField("Parser") != null
                    select assemblyType.GetField("Parser").GetValue(null) as IStatementParser;
        return types;
    }

    public Parser(Dictionary<string, int> constants, Dictionary<string, ExternalVariable> extVars)
    {
        _formatter = new Formatter(constants, extVars);
    }

    private IEnumerable<ParserArgument> ParseLines(string text)
    {
        var lines = Regex.Split(text, "\r\n|\r|\n");
        foreach (var line in lines)
        {
            // get indent
            text = line.TrimStart();
            var _text = text;
            // get comment
            var m = Regex.Match(text, @"(\s*#.*)$");
            string comment = string.Empty;
            if (m.Success)
            {
                comment = m.Groups[1].Value;
                text = text[..^comment.Length];
            }
            else
            {
                text = text.Trim();
            }
            yield return new ParserArgument
            {
                Text = text,
                Comment = comment,
                Formatter = _formatter,
            };
        }
    }

    #region 解析函数
    private Statement? ParseStatement(string text)
    {
        // empty
        if (text.Length == 0)
            return new Empty();
        if (text.StartsWith('_'))
        {
            return ParseConstantDecl(text);
        }
        if (text.StartsWith('$'))
        {
            return ParseAssignment(text);
        }
        if (text.StartsWith("if", StringComparison.OrdinalIgnoreCase))
        {
            return ParseIfelse(text);
        }
        if (text.StartsWith("elif", StringComparison.OrdinalIgnoreCase))
        {
            return ParseIfelse(text, true);
        }
        if (text.Equals("else", StringComparison.OrdinalIgnoreCase))
        {
            return new Else();
        }
        if (text.Equals("endif", StringComparison.OrdinalIgnoreCase))
        {
            return new EndIf();
        }
        if (text.StartsWith("for", StringComparison.OrdinalIgnoreCase))
        {
            return ParseFor(text);
        }
        if (text.StartsWith("break", StringComparison.OrdinalIgnoreCase) || text.StartsWith("continue", StringComparison.OrdinalIgnoreCase))
        {
            return ParseLoopCtrl(text);
        }
        if (text.Equals("next", StringComparison.OrdinalIgnoreCase))
        {
            return new Next();
        }
        if (text.StartsWith("func", StringComparison.OrdinalIgnoreCase))
        {
            return ParseFunctionDecl(text);
        }
        if (text.StartsWith("call", StringComparison.OrdinalIgnoreCase))
        {
            return ParseCall(text);
        }
        if (text.Equals("endfunc", StringComparison.OrdinalIgnoreCase))
        {
            return new ReturnStat();
        }
        if (text.StartsWith("amiibo", StringComparison.OrdinalIgnoreCase))
        {
            return ParseAmiibo(text);
        }
        if (int.TryParse(text, out int duration))
            return new Wait(duration, true);
        if (text.StartsWith("wait", StringComparison.OrdinalIgnoreCase))
        {
            return ParseWait(text);
        }
        if (text.StartsWith("alert", StringComparison.OrdinalIgnoreCase) || text.StartsWith("print", StringComparison.OrdinalIgnoreCase))
        {
            return ParseAlert(text) ?? ParsePrint(text);
        }
        if (text.StartsWith("TIME", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGetTime(text);
        }
        if (text.Equals("PUSHALL", StringComparison.OrdinalIgnoreCase))
            return new PushAll();
        if (text.Equals("POPALL", StringComparison.OrdinalIgnoreCase))
            return new PopAll();
        else
        {
            return ParseKey(text) ?? ParseDebug(text);
        }
    }

    private Statement? ParseConstantDecl(string text)
    {
        var m = Regex.Match(text, $@"^{Formats.Constant}\s*=\s*{Formats.Instant}$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var val = _formatter.GetInstant(m.Groups[2].Value);
            _formatter.SetConstantTable(m.Groups[1].Value, val.Val);
            return new Empty($"{m.Groups[1].Value} = {m.Groups[2].Value}");
        }
        return null;
    }

    private static bool IsValidVariable(string variable)
    {
        return !string.IsNullOrEmpty(variable) && Regex.Match(variable, $"^{Formats.ValueEx}$", RegexOptions.IgnoreCase).Success;
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
                        return new Expr(des, _formatter.GetValueEx(var1), op, _formatter.GetValueEx(var2));
                    }
                }
                else if (IsValidVariable(exprStr))
                {
                    return new Expr(des, _formatter.GetValueEx(vR.Groups[1].Value), null, null);
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
                return new If(op, _formatter.GetVar(m.Groups[1].Value), _formatter.GetValueEx(m.Groups[2].Value));
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
        // break
        if (text.Equals("break", StringComparison.OrdinalIgnoreCase))
            return new Break();
        var m = Regex.Match(text, $@"^break\s+{Formats.Instant}$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new Break(_formatter.GetInstant(m.Groups[1].Value, true));

        // continue
        if (text.Equals("continue", StringComparison.OrdinalIgnoreCase))
            return new Continue();
        m = Regex.Match(text, $@"^continue\s+{Formats.Instant}$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new Continue(_formatter.GetInstant(m.Groups[1].Value, true));
        return null;
    }

    private Statement? ParseFunctionDecl(string text)
    {
        var m = Regex.Match(text, @"^func\s+(\D[\d\p{L}_]+)$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new Function(m.Groups[1].Value);
        return null;
    }

    private Statement? ParseCall(string text)
    {
        var m = Regex.Match(text, @"^call\s+(\D[\d\p{L}_]+)$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new CallStat(m.Groups[1].Value);
        return null;
    }

    const string GPKey = "[ABXYLR]|Z[LR]|[LR]CLICK|HOME|CAPTURE|PLUS|MINUS|LEFT|RIGHT|UP|DOWN";
    private Statement? ParseKey(string text)
    {
        ECKey key;
        var m = Regex.Match(text, $"^({GPKey})$", RegexOptions.IgnoreCase);
        if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
            return new KeyPress(key);
        m = Regex.Match(text, $@"^({GPKey})\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
            return new KeyPress(key, _formatter.GetValueEx(m.Groups[2].Value));
        m = Regex.Match(text, $@"^({GPKey})\s+(up|down)$", RegexOptions.IgnoreCase);
        if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
        {
            return m.Groups[2].Value.ToUpper() switch
            {
                "UP" => new KeyUp(key),
                "DOWN" => new KeyDown(key),
                _ => null,
            };
        }

        // stick
        m = Regex.Match(text, @"^([lr]s)\s+(reset)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var keyname = m.Groups[1].Value;
            key = NSKeys.GetKey(keyname);
            if (key == null)
                return null;
            return new StickUp(key, keyname);
        }
        m = Regex.Match(text, @"^([lr]s)\s+([a-z0-9]+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var keyname = m.Groups[1].Value;
            var direction = m.Groups[2].Value;
            key = NSKeys.GetKey(keyname, direction);
            if (key == null)
                return null;
            return new StickDown(key, keyname, direction);
        }
        m = Regex.Match(text, $@"^([lr]s)\s+([a-z0-9]+)\s*,\s*({Formats.ValueEx})$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var keyname = m.Groups[1].Value;
            var direction = m.Groups[2].Value;
            var duration = m.Groups[3].Value;
            key = NSKeys.GetKey(keyname, direction);
            if (key == null)
                return null;
            return new StickPress(key, keyname, direction, _formatter.GetValueEx(duration));
        }
        return null;
    }

    private Statement? ParseWait(string text)
    {
        var m = Regex.Match(text, $@"^wait\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new Wait(_formatter.GetValueEx(m.Groups[1].Value));
        return null;
    }

    private Statement? ParseAlert(string text)
    {
        if (text.Equals("alert", StringComparison.OrdinalIgnoreCase))
            return new Alert(Array.Empty<Content>());
        var m = Regex.Match(text, @"^alert\s+(.*)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var contents = ParseContents(m.Groups[1].Value, out _);
            return new Alert(contents.ToArray());
        }

        return null;
    }

    private Statement? ParsePrint(string text)
    {
        if (text.Equals("print", StringComparison.OrdinalIgnoreCase))
            return new Print(Array.Empty<Content>(), false);
        var m = Regex.Match(text, @"^print\s+(.*)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var contents = ParseContents(m.Groups[1].Value, out bool cancellinebreak);
            return new Print(contents.ToArray(), cancellinebreak);
        }
        return null;
    }

    private Statement? ParseGetTime(string text)
    {
        var m = Regex.Match(text, $@"^TIME\s+{Formats.RegisterEx}$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            return new TimeStamp(FormatterUtil.GetRegEx(m.Groups[1].Value));
        }
        return null;
    }

    private List<Content> ParseContents(string text, out bool cancellinebreak)
    {
        var contents = new List<Content>();
        cancellinebreak = false;

        var strs = text.Split('&');
        for (var i = 0; i < strs.Length; i++)
        {
            var s = strs[i].Trim();
            if (i == strs.Length - 1 && s == "\\")
            {
                contents.Add(new TextContent("", "\\"));
                cancellinebreak = true;
                continue;
            }
            if (s.Length == 0 && strs.Length > 1)
            {
                contents.Add(new TextContent(" ", ""));
                continue;
            }
            var m = Regex.Match(s, Formats.RegisterEx_F);
            if (m.Success)
            {
                contents.Add(new RegContent(FormatterUtil.GetRegEx(s)));
                continue;
            }
            m = Regex.Match(s, Formats.Constant_F);
            if (m.Success)
            {
                var v = _formatter.GetConstant(s);
                contents.Add(new TextContent(v.Val.ToString(), s));
                continue;
            }
            contents.Add(new TextContent(s));
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

    private Statement? ParseAmiibo(string text)
    {
        if (text.Equals("AMIIBO_RESET", StringComparison.OrdinalIgnoreCase))
            return new AmiiboReset();
        var m = Regex.Match(text, $@"amiibo\s*{Formats.Value}$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var amiiboidx = _formatter.GetValueEx(m.Groups[1].Value);
            return new AmiiboChanger(amiiboidx);
        }
        return null;
    }
    #endregion

    public List<Statement> Parse(string text)
    {
        int indentnum = 0;
        int linnum = 0;
        int address = 0;
        var list = new List<Statement>();
        foreach (var args in ParseLines(text))
        {
            try
            {
                var st = ParseStatement(args.Text);
                if (st != null)
                {
                    indentnum += st.IndentThis;
                    st.Indent = new string(' ', indentnum < 0 ? 0 : indentnum * 4);
                    st.Comment = args.Comment;
                    // update address
                    st.Address = address;
                    list.Add(st);
                    indentnum += st.IndentNext;
                    address += 1;
                }
                else
                {
                    throw new ParseException("格式错误", linnum);
                }
            }
            catch (OverflowException)
            {
                throw new ParseException("数值溢出", linnum);
            }
            catch (ParseException ex)
            {
                ex.Index = linnum;
                throw;
            }
            linnum++;
        }

        var _blocks = new Stack<Statement>();
        var _elifs = new Stack<BranchOp>();
        var _funcTables = new Dictionary<string, Function>();

        for (var i = 0; i < list.Count; i++)
        {
            var st = list[i];

            if (st is For || (st is If && st is not ElseIf) || st is Function)
            {
                _blocks.Push(st);
                if (st is Function fst)
                {
                    if (_funcTables.ContainsKey(fst.Label))
                    {
                        throw new ParseException("重复定义的函数名", i);
                    }
                    _funcTables[fst.Label] = fst;
                }
                continue;
            }
            // for/next
            if (st is LoopControl loopstat)
            {
                var forcount = (from forstate in _blocks
                                where forstate is Statements.For
                                select forstate).Count();
                if (loopstat.Level.Val > forcount)
                    throw new ParseException("循环层数不足", i);
            }
            if (st is Statements.Next nextstat)
            {
                if (_blocks.Count == 0)
                    throw new ParseException("多余的语句", i);
                var last = _blocks.Peek();
                if (last is Statements.For lastfor)
                {
                    _blocks.Pop();
                    nextstat.For = lastfor;
                    lastfor.Next = nextstat;
                }
                else
                {
                    throw new ParseException("NEXT需要对应的For语句", i);
                }
            }
            // if/elif/elif/else/endif
            if (st is Statements.ElseIf selif)
            {
                if (_blocks.Count == 0)
                    throw new ParseException("多余的语句", i);
                var last = _blocks.Peek();
                if (last is Statements.If lastif)
                {
                    if (lastif.Else != null)
                    {
                        throw new ParseException("多余的Elseif语句", i);
                    }
                    _blocks.Pop();
                    _blocks.Push(selif);
                    lastif.Else = st as Statements.BranchOp;
                    selif.If = lastif;
                    _elifs.Push(lastif);
                }
                else
                {
                    throw new ParseException("elif需要配合if语句使用", i);
                }
            }
            if (st is Statements.Else selse)
            {
                if (_blocks.Count == 0)
                    throw new ParseException("多余的语句", i);
                var last = _blocks.Peek();
                if (last is Statements.If lastif)
                {
                    if (lastif.Else != null)
                    {
                        throw new ParseException("一个If只能对应一个Else", i);
                    }
                    lastif.Else = st as Statements.BranchOp;
                    selse.If = lastif;
                }
                else
                {
                    throw new ParseException("else需要配合if语句使用", i);
                }
            }
            if (st is Statements.EndIf endifst)
            {
                if (_blocks.Count == 0)
                    throw new ParseException("多余的语句", i);
                var last = _blocks.Peek();
                if (last is Statements.If lastif)
                {
                    _blocks.Pop();
                    lastif.EndIf = endifst;
                    endifst.If = lastif;

                    if (_elifs.Count != 0 && _elifs.Peek().Else == last)
                    {
                        while (_elifs.Count > 0)
                        {
                            _elifs.Pop().EndIf = endifst;
                        }
                    }
                }
                else
                {
                    throw new ParseException("endif需要对应的If语句", i);
                }
            }
            // function
            if (st is Statements.ReturnStat rst)
            {
                if (_blocks.Count == 0)
                    throw new ParseException("多余的语句", i);
                var last = _blocks.Peek();
                if (last is Statements.Function lastfunc)
                {
                    _blocks.Pop();
                    lastfunc.Ret = rst;
                    rst.Label = lastfunc.Label;
                }
                else
                {
                    throw new ParseException("Endfunc需要对应的Func语句", i);
                }
            }
        }

        if (_blocks.Count != 0)
            throw new ParseException($"发现未结束的语句：{_blocks.Peek()}", _blocks.Peek().Address);
        // pair Call
        for (var i = 0; i < list.Count; i++)
        {
            var st = list[i];
            if (st is Statements.CallStat cst)
            {
                var @func = _funcTables.GetValueOrDefault(cst.Label, null);
                if (@func == null)
                {
                    throw new ParseException("找不到调用的函数", i);
                }
                cst.Func = @func;
            }
        }

        return list;
    }
}
