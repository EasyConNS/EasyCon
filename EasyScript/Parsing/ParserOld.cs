using EasyScript.Statements;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing;

partial class Parser
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

    public List<Statement> Parse(string text)
    {
        int indentnum = 0;
        int linnum = 0;
        int address = 0;
        var list = new List<Statement>();
#if DEBUG
        try
        {
            var tokens = new Lexer(text).Tokenize();
            foreach (var token in tokens)
            {
                Debug.Write(token, ",");
                if (token.Type == TokenType.NEWLINE)
                {
                    Debug.WriteLine("|");
                }
            }
        }
        catch (Exception e) { Debug.WriteLine(e.Message); }
#endif
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
