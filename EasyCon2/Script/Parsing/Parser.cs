﻿using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing
{
    class Parser
    {
        readonly Dictionary<string, int> _constants;
        readonly Dictionary<string, ExternalVariable> _extVars;
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
            _constants = constants;
            _extVars = extVars;
        }

        public List<Statement> Parse(string text)
        {
            var list = new List<Statement>();
            var lines = text.Replace("\r", "").Split('\n');
            int indentnum = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                text = lines[i];
                // get indent
                var m = Regex.Match(text, @"^(\s*)([^\s]?.*)$");
                _ = m.Groups[1].Value;
                text = m.Groups[2].Value;
                // get comment
                m = Regex.Match(text, @"(\s*#.*)$");
                string comment;
                if (m.Success)
                {
                    comment = m.Groups[1].Value;
                    text = text[..^comment.Length];
                }
                else
                {
                    comment = string.Empty;
                    text = text.Trim();
                }
                try
                {
                    // enumerate generators
                    var args = new ParserArgument
                    {
                        Text = text,
                        Formatter = new Formatter(_constants, _extVars)
                    };
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
            for (var i = 0; i < list.Count; i++)
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
            for (var i = 0; i < list.Count; i++)
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

            // pair Func & Ret
            var _funcs = new Stack<Statements.Function>();
            var _funcTables = new Dictionary<string, Statements.Function>();
            for (var i = 0; i < list.Count; i++)
            {
                var st = list[i];
                if (st is Statements.Function)
                {
                    var fst = st as Statements.Function;
                    if(_funcTables.ContainsKey(fst.Label))
                    {
                        throw new ParseException("重复定义的函数名", i);
                    }
                    _funcTables[fst.Label] = fst;
                    _funcs.Push(fst);
                }
                else if (st is Statements.ReturnStat)
                {
                    if (_funcs.Count == 0)
                        throw new ParseException("找不到对应的Func定义", i);
                    var @func = _funcs.Peek();
                    @func.Ret = (st as Statements.ReturnStat);
                    var @ret = (st as Statements.ReturnStat);
                    @ret.Label = @func.Label;
                    _funcs.Pop();
                }
            }
            if (_funcs.Count > 0)
                throw new ParseException("Func语句需要Ret结束", _funcs.Peek().Address);
            // pair call func
            for (var i = 0; i < list.Count; i++)
            {
                var st = list[i];
                if (st is Statements.CallStat)
                {
                    var cst = st as Statements.CallStat;

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
