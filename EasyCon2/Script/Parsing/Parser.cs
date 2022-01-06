using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing
{
    class Parser
    {
        readonly Dictionary<string, int> _constants;
        readonly Dictionary<string, ExternalVariable> _extVars;
        List<IStatementParser> _parsers = new();

        public Parser(Dictionary<string, int> constants, Dictionary<string, ExternalVariable> extVars)
        {
            _constants = constants;
            _extVars = extVars;

            // constant declaration
            _parsers.Add(new StatementParser(args =>
            {
                var m = Regex.Match(args.Text, $@"^{Formats.Constant}\s*=\s*(\d+)$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    _constants[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
                    return new Statements.Empty($"{m.Groups[1].Value} = {m.Groups[2].Value}");
                }
                return null;
            }));
            // others
            var types = (from domainAssembly in AppDomain.CurrentDomain.GetAssemblies()
                         from assemblyType in domainAssembly.GetTypes()
                         where assemblyType.IsSubclassOf(typeof(Statement))
                         select assemblyType).ToArray();
            foreach (var type in types)
            {
                var field = type.GetField("Parser");
                if (field != null)
                    _parsers.Add(field.GetValue(null) as IStatementParser);
            }
        }

        public List<Statement> Parse(string text)
        {
            List<Statement> list = new List<Statement>();
            var lines = text.Replace("\r", "").Split('\n');
            int indentnum = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                text = lines[i];
                Match m;
                string indent;
                string comment;
                // get indent
                m = Regex.Match(text, @"^(\s*)([^\s]?.*)$");
                indent = m.Groups[1].Value;
                text = m.Groups[2].Value;
                // get comment
                m = Regex.Match(text, @"(\s*#.*)$");
                if (m.Success)
                {
                    comment = m.Groups[1].Value;
                    text = text.Substring(0, text.Length - comment.Length);
                }
                else
                {
                    comment = string.Empty;
                    text = text.Trim();
                }
                try
                {
                    // enumerate generators
                    var args = new ParserArgument();
                    args.Text = text;
                    args.Formatter = new Formats.Formatter(_constants, _extVars);
                    Statement st = null;
                    foreach (var parser in _parsers)
                    {
                        st = parser.Parse(args);
                        if (st != null)
                        {
                            indentnum += st.IndentThis;
                            st.Indent = new string(' ', indentnum < 0 ? 0 : indentnum * 4);
                            st.Comment = comment;
                            list.Add(st);
                            indentnum += st.IndentNext;
                            break;
                        }
                    }
                    if (st == null)
                        throw new ParseException("格式错误", i);
                }
                catch (OverflowException)
                {
                    throw new ParseException("数值溢出", i);
                }
                catch (ParseException ex)
                {
                    ex.Index = i;
                    throw;
                }
            }

            // update address
            for (var i = 0; i < list.Count; i++)
                list[i].Address = i;

            // pair For & Next
            var _fors = new Stack<Statements.For>();
            for (int i = 0; i < list.Count; i++)
            {
                var st = list[i];
                if (st is Statements.For)
                    _fors.Push(st as Statements.For);
                else if (st is Statements.Next)
                {
                    if (_fors.Count == 0)
                        throw new ParseException("找不到对应的For语句", i);
                    var @for = _fors.Pop();
                    var next = st as Statements.Next;
                    next.For = @for;
                    @for.Next = next;
                }
                else if (st is Statements.LoopControl)
                {
                    if ((st as Statements.LoopControl).Level.Val > _fors.Count)
                        throw new ParseException("循环层数不足", i);
                }
            }
            if (_fors.Count > 0)
                throw new ParseException("For语句需要Next结束", _fors.Peek().Address);

            // pair If & Else & Endif
            var _ifs = new Stack<Statements.If>();
            for (int i = 0; i < list.Count; i++)
            {
                var st = list[i];
                if (st is Statements.If)
                    _ifs.Push(st as Statements.If);
                else if (st is Statements.Else || st is Statements.EndIf)
                {
                    if (_ifs.Count == 0)
                        throw new ParseException("找不到对应的If语句", i);
                    var @if = _ifs.Peek();
                    if (st is Statements.Else)
                    {
                        if (@if.Else != null)
                            throw new ParseException("一个If只能对应一个Else", i);
                        var @else = st as Statements.Else;
                        @if.Else = @else;
                        @else.If = @if;
                    }
                    else
                    {
                        var endif = st as Statements.EndIf;
                        @if.EndIf = endif;
                        endif.If = @if;
                        _ifs.Pop();
                    }
                }
            }
            if (_ifs.Count > 0)
                throw new ParseException("If语句需要Endif结束", _ifs.Peek().Address);

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
