using System.Text;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing
{
    class Parser
    {
        readonly Formatter _formatter;

        static readonly List<IStatementParser> _parsers = new();
        public VBF.Compilers.CompilationErrorManager ceMgr = new();
        public ECP.ECScript ECParser = new(ceMgr);

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
                //if(_text == string.Empty)continue;
                var mp = Regex.Match(_text, @"^print\s+(.*)$", RegexOptions.IgnoreCase);
                if (mp.Success)
                {
                    builder.Append($"print \"{mp.Groups[1].Value}\"");
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
                ECParser.Initialize();
                var ast = ECParser.Parse(text, ceMgr..CreateErrorList());
                System.Diagnostics.Debug.WriteLine(ast);
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

            // pair For & Next
            var _fors = new Stack<Statements.For>();
            // pair If /  Elif / Else / Endif
            var _ifs = new Stack<Statements.BranchOp>();
            var _elifs = new Stack<Statements.BranchOp>();
            // pair Func & Ret
            var _funcs = new Stack<Statements.Function>();
            var _funcTables = new Dictionary<string, Statements.Function>();
            // block stack
            var _blockStacks = new Stack<string>();

            for (var i = 0; i < list.Count; i++)
            {
                var st = list[i];
                if (st is Statements.For)
                {
                    _fors.Push(st as Statements.For);
                    _blockStacks.Push("for");
                }
                    
                else if (st is Statements.Next nextst)
                {
                    if(_blockStacks.Peek()!= "for")
                        throw new ParseException($"发现未结束的语句：{_blockStacks.Peek()}", i);
                    if (_fors.Count == 0)
                        throw new ParseException("找不到对应的For语句", i);
                    var @for = _fors.Pop();
                    nextst.For = @for;
                    @for.Next = nextst;
                    _blockStacks.Pop();
                }
                else if (st is Statements.LoopControl loopst)
                {
                    if (loopst.Level.Val > _fors.Count)
                        throw new ParseException("循环层数不足", i);
                }

                if (st is Statements.If)
                {
                    _ifs.Push(st as Statements.If);
                    _blockStacks.Push("if");
                }
                    
                else if (st is Statements.ElseIf || st is Statements.Else || st is Statements.EndIf)
                {
                    if (_ifs.Count == 0)
                        throw new ParseException("找不到对应的If语句", i);
                    var @if = _ifs.Peek();
                    if(st is Statements.ElseIf selif)
                    {
                        if (@if.Else != null)
                        {
                            throw new ParseException("多余的Else语句", i);
                        }
                        @if.Else = st as Statements.BranchOp;
                        selif.If = @if;
                        _elifs.Push(_ifs.Pop());
                        _ifs.Push(selif);
                    }
                    else if (st is Statements.Else)
                    {
                        if (@if.Else != null)
                        {
                            throw new ParseException("一个If只能对应一个Else", i);
                        }
                        var @else = st as Statements.BranchOp;
                        @if.Else = @else;
                        @else.If = @if;
                    }
                    else if (st is Statements.EndIf endifst)
                    {
                        if (_blockStacks.Peek() != "if")
                            throw new ParseException($"发现未结束的语句：{_blockStacks.Peek()}", i);
                        @if.EndIf = endifst;
                        endifst.If = @if;
                        _ifs.Pop();
                        while(_elifs.Count > 0)
                        {
                            _elifs.Pop().EndIf = endifst;
                        }
                        _blockStacks.Pop();
                    }
                    else
                        throw new ArgumentException($"非预期的类型：{st.GetType()}");
                }

                if (st is Statements.Function fst)
                {
                    if(_funcTables.ContainsKey(fst.Label))
                    {
                        throw new ParseException("重复定义的函数名", i);
                    }
                    _funcTables[fst.Label] = fst;
                    _funcs.Push(fst);
                    _blockStacks.Push("func");
                }
                else if (st is Statements.ReturnStat rst)
                {
                    if (_blockStacks.Peek() != "func")
                        throw new ParseException($"发现未结束的语句：{_blockStacks.Peek()}", i);
                    if (_funcs.Count == 0)
                        throw new ParseException("找不到对应的Func定义", i);
                    var @func = _funcs.Peek();
                    @func.Ret = rst;
                    rst.Label = @func.Label;
                    _funcs.Pop();
                    _blockStacks.Pop();
                }
            }
            if (_fors.Count > 0)
                throw new ParseException("For语句需要Next结束", _fors.Peek().Address);
            if (_ifs.Count > 0 || _elifs.Count > 0)
                throw new ParseException("If语句需要Endif结束", _ifs.Peek().Address);
            if (_funcs.Count > 0)
                throw new ParseException("Func语句需要Ret结束", _funcs.Peek().Address);
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
