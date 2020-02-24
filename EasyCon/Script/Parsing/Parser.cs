using EasyCon.Script.Parsing.Statements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyCon.Script.Parsing
{
    class Parser
    {
        readonly Dictionary<string, int> _constants;
        List<IStatementParser> _parsers = new List<IStatementParser>();

        public Parser(Dictionary<string, int> constants)
        {
            _constants = constants;

            // constant declaration
            _parsers.Add(new StatementParser(args =>
            {
                var m = Regex.Match(args.Text, $@"^{Formats.Constant}\s*=\s*(\d+)$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    _constants[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
                    return new Empty($"{m.Groups[1].Value} = {m.Groups[2].Value}");
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

        Statement ParseLing(string text)
        {
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
                args.Formatter = new Formats.Formatter(_constants);
                foreach (var parser in _parsers)
                {
                    var cmd = parser.Parse(args);
                    if (cmd != null)
                    {
                        cmd.Indent = indent;
                        cmd.Comment = comment;
                        return cmd;
                    }
                }
            }
            catch (OverflowException)
            {
                throw new ParseException("数值溢出");
            }
            throw new ParseException("格式错误");
        }

        public List<Statement> Parse(string text)
        {
            List<Statement> list = new List<Statement>();
            var lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                try
                {
                    var cmd = ParseLing(lines[i]);
                    if (cmd != null)
                        list.Add(cmd);
                }
                catch (ParseException ex)
                {
                    ex.Index = i;
                    throw;
                }
            }

            // update address
            for (int i = 0; i < list.Count; i++)
                list[i].Address = i;

            // pair For & Next
            Stack<For> _fors = new Stack<For>();
            for (int i = 0; i < list.Count; i++)
            {
                var st = list[i];
                if (st is For)
                    _fors.Push(st as For);
                else if (st is Next)
                {
                    if (_fors.Count == 0)
                        throw new ParseException("找不到对应的For语句", i);
                    var @for = _fors.Pop();
                    var next = st as Next;
                    next.For = @for;
                    @for.Next = next;
                }
                else if (st is LoopControl)
                {
                    if ((st as LoopControl).Level.Val > _fors.Count)
                        throw new ParseException("循环层数不足", i);
                }
            }
            if (_fors.Count > 0)
                throw new ParseException("For语句需要Next结束", _fors.Peek().Address);

            // pair If & Else & Endif
            Stack<If> _ifs = new Stack<If>();
            for (int i = 0; i < list.Count; i++)
            {
                var st = list[i];
                if (st is If)
                    _ifs.Push(st as If);
                else if (st is Else || st is EndIf)
                {
                    if (_ifs.Count == 0)
                        throw new ParseException("找不到对应的If语句", i);
                    var @if = _ifs.Peek();
                    if (st is Else)
                    {
                        if (@if.Else != null)
                            throw new ParseException("一个If只能对应一个Else", i);
                        var @else = st as Else;
                        @if.Else = @else;
                        @else.If = @if;
                    }
                    else
                    {
                        var endif = st as EndIf;
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
