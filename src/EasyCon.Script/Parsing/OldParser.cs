using EasyCon.Script2.Ast;
using EasyCon.Script2.Syntax;
using EasyScript.Statements;
using System.Reflection;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing;

partial class Parser(Dictionary<string, int> constants, Dictionary<string, ExternalVariable> extVars)
{
    readonly Formatter _formatter = new(constants, extVars);

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
        else if (text.StartsWith('$'))
        {
            return ParseAssignment(text);
        }
        else if (text.StartsWith("if", StringComparison.OrdinalIgnoreCase))
        {
            return ParseIfelse(text);
        }
        else if (text.StartsWith("elif", StringComparison.OrdinalIgnoreCase))
        {
            return ParseIfelse(text, true);
        }
        else if (text.Equals("else", StringComparison.OrdinalIgnoreCase))
        {
            return new Else();
        }
        else if (text.Equals("endif", StringComparison.OrdinalIgnoreCase))
        {
            return new EndIf();
        }
        else if (text.StartsWith("for", StringComparison.OrdinalIgnoreCase))
        {
            return ParseFor(text);
        }
        else if (text.StartsWith("break", StringComparison.OrdinalIgnoreCase) || text.StartsWith("continue", StringComparison.OrdinalIgnoreCase))
        {
            return ParseLoopCtrl(text);
        }
        else if (text.Equals("next", StringComparison.OrdinalIgnoreCase))
        {
            return new Next();
        }
        else if (text.Equals("return", StringComparison.OrdinalIgnoreCase))
        {
            return new ReturnStat();
        }
        else if (text.Equals("endfunc", StringComparison.OrdinalIgnoreCase))
        {
            return new EndFuncStat();
        }
        else if (int.TryParse(text, out int duration))
            return new Wait(duration, true);
        else
        {
            return ParseNamedExpression(text) ?? ParseKey(text) ?? ParseDebug(text);
        }
    }

    public List<Statement> Parse(string text)
    {
        int indentnum = 0;
        int linnum = 0;
        int address = 0;
        var list = new List<Statement>();

        var _blocks = new Stack<Statement>();
        var _elifs = new Stack<BranchOp>();
        var _funcTables = new Dictionary<string, FunctionStmt>();
#if false
        try
        {
            var syntaxTree = SyntaxTree.Parse(text);

            if (syntaxTree.Diagnostics.Length > 0)
            {
                foreach (var diagnostic in syntaxTree.Diagnostics)
                {
                    System.Diagnostics.Debug.WriteLine(diagnostic);
                }
            }
            else
            {
                var visitor = new StatementGen(_formatter);
                visitor.VisitProgram(syntaxTree.Root);
            }
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine(e.Message); }
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

                    if (st is For || (st is If && st is not ElseIf) || st is FunctionStmt)
                    {
                        _blocks.Push(st);
                        if (st is FunctionStmt fst)
                        {
                            if (_funcTables.ContainsKey(fst.Label))
                            {
                                throw new ParseException("重复定义的函数名", address);
                            }
                            _funcTables[fst.Label] = fst;
                        }
                    }
                    // for/next
                    if (st is LoopControl loopstat)
                    {
                        var forcount = (from forstate in _blocks
                                        where forstate is Statements.For
                                        select forstate).Count();
                        if (loopstat.Level.Val > forcount)
                            throw new ParseException("循环层数不足", address);
                    }
                    if (st is Next nextstat)
                    {
                        if (_blocks.Count == 0)
                            throw new ParseException("多余的语句", address);
                        var last = _blocks.Peek();
                        if (last is For lastfor)
                        {
                            _blocks.Pop();
                            nextstat.For = lastfor;
                            lastfor.Next = nextstat;
                        }
                        else
                        {
                            throw new ParseException("NEXT需要对应的For语句", address);
                        }
                    }
                    // if/elif/elif/else/endif
                    if (st is ElseIf selif)
                    {
                        if (_blocks.Count == 0)
                            throw new ParseException("多余的语句", address);
                        var last = _blocks.Peek();
                        if (last is Statements.If lastif)
                        {
                            if (lastif.Else != null)
                            {
                                throw new ParseException("多余的Elseif语句", address);
                            }
                            _blocks.Pop();
                            _blocks.Push(selif);
                            lastif.Else = st as Statements.BranchOp;
                            selif.If = lastif;
                            _elifs.Push(lastif);
                        }
                        else
                        {
                            throw new ParseException("elif需要配合if语句使用", address);
                        }
                    }
                    if (st is Statements.Else selse)
                    {
                        if (_blocks.Count == 0)
                            throw new ParseException("多余的语句", address);
                        var last = _blocks.Peek();
                        if (last is Statements.If lastif)
                        {
                            if (lastif.Else != null)
                            {
                                throw new ParseException("一个If只能对应一个Else", address);
                            }
                            lastif.Else = st as Statements.BranchOp;
                            selse.If = lastif;
                        }
                        else
                        {
                            throw new ParseException("else需要配合if语句使用", address);
                        }
                    }
                    if (st is Statements.EndIf endifst)
                    {
                        if (_blocks.Count == 0)
                            throw new ParseException("多余的语句", address);
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
                            throw new ParseException("endif需要对应的If语句", address);
                        }
                    }
                    // function
                    if (st is EndFuncStat rst)
                    {
                        if (_blocks.Count == 0)
                            throw new ParseException("多余的语句", address);
                        var last = _blocks.Peek();
                        if (last is FunctionStmt lastfunc)
                        {
                            _blocks.Pop();
                            lastfunc.Ret = rst;
                            rst.Label = lastfunc.Label;
                        }
                        else
                        {
                            throw new ParseException("Endfunc需要对应的Func语句", address);
                        }
                    }



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
                Console.WriteLine($"{ex.Message}: 行{ex.Index + 1}");
                ex.Index = linnum;
                throw;
            }
            linnum++;
        }

        if (_blocks.Count != 0)
            throw new ParseException("发现未结束的语句", _blocks.Peek().Address);

        return list;
    }
}
