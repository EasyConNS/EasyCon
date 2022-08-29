using System.Text;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing
{
    class Parser
    {
        readonly Formatter _formatter;

        static readonly List<IStatementParser> _parsers = new();

        static Parser()
        {
            var types = (from domainAssembly in AppDomain.CurrentDomain.GetAssemblies()
                         from assemblyType in domainAssembly.GetTypes()
                         where assemblyType.IsTypePlugin(typeof(IStatementParser))
                         select assemblyType).ToArray();
            foreach (var t in types)
            {
                IStatementParser? activate;
                try { activate = (IStatementParser?)Activator.CreateInstance(t); }
                catch (Exception) { continue; }
                if (activate != null)
                    _parsers.Add(activate);
            }
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

        private static string compat(string text)
        {
            var builder = new StringBuilder();
            var lines = Regex.Split(text, "\r\n|\r|\n");
            foreach (var line in lines)
            {
                var _text = line.Trim();
                var mp = Regex.Match(_text, @"^print\s+(.*)$", RegexOptions.IgnoreCase);
                if (mp.Success)
                {
                    builder.Append($"PRINT \"{mp.Groups[1].Value}\"");
                }
                else
                {
                    builder.Append(_text);
                }
                builder.Append("\r\n");
            }
            return builder.ToString();
        }

        public List<Statement> Parse(string text)
        {
            var list = new List<Statement>();
            int indentnum = 0;
            int linnum = 0;
            int address = 0;
            System.Diagnostics.Debug.WriteLine("v2 start---");
            try
            {
                //var program = new ECP.ECScript();
                //program.Initialize();
                //foreach(var lxm in program.Parse(compat(text)))
                //{
                //    System.Diagnostics.Debug.WriteLine(lxm);
                //}
                var ceMgr = new VBF.Compilers.CompilationErrorManager();
                var ECParser = new ECP.VBFECScript(ceMgr);
                ECParser.Initialize();
                var ast = ECParser.Parse(compat(text), ceMgr.CreateErrorList());
                var visitor = new ECP.SimpleVisitor();
                visitor.Visit(ast);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e);
            }
            System.Diagnostics.Debug.WriteLine("v2   end---");
            foreach (var args in ParseLines(text))
            {
                try
                {
                    Statement st = null;
                    // enumerate generators
                    foreach (var parser in _parsers)
                    {
                        st = parser.Parse(args);
                        if (st != null)
                        {
                            indentnum += st.IndentThis;
                            st.Indent = new string(' ', indentnum < 0 ? 0 : indentnum * 4);
                            st.Comment = args.Comment;
                            // update address
                            st.Address = address;
                            list.Add(st);
                            indentnum += st.IndentNext;
                            address+=1;
                            break;
                        }
                    }
                    if (st == null)
                        throw new ParseException("格式错误", linnum);
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
            var _elifs = new Stack<Statements.BranchOp>();
            var _funcTables = new Dictionary<string, Statements.Function>();

            for (var i = 0; i < list.Count; i++)
            {
                var st = list[i];

                if (st is Statements.For || (st is Statements.If && st is not Statements.ElseIf) || st is Statements.Function)
                {
                    _blocks.Push(st);
                    if (st is Statements.Function fst)
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
                if (st is Statements.LoopControl loopstat)
                {
                    var forcount = (from forstate in _blocks
                                   where forstate is Statements.For
                                   select forstate).Count();
                    if (loopstat.Level.Val > forcount)
                        throw new ParseException("循环层数不足", i);
                }
                if (st is Statements.Next nextstat)
                {
                    if(_blocks.Count == 0)
                        throw new ParseException("多余的语句", i);
                    var last = _blocks.Peek();
                    if (last is Statements.For lastfor)
                    {
                        _blocks.Pop();
                        nextstat.For = lastfor;
                        lastfor.Next = nextstat;
                    } else
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

                        if(_elifs.Count != 0 && _elifs.Peek().Else == last)
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
                    if(@func == null)
                    {
                        throw new ParseException("找不到调用的函数", i);
                    }
                    cst.Func = @func;
                }
            }

            return list;
        }
    }

    public class ParseException : Exception
    {
        public int Index;

        public ParseException(string message, int index = -1)
            : base(message)
        {
            Index = index;
        }
    }
}
