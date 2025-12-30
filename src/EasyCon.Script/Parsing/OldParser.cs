using EasyScript.Statements;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing;

partial class Parser(Dictionary<string, ExternalVariable> extVars)
{
    readonly Formatter _formatter = new(extVars);

    [GeneratedRegex(@"(\s*#.*)$")]
    private static partial Regex CommentRegex();

    private IEnumerable<ParserArgument> ParseLines(string text)
    {
        var lines = Regex.Split(text.Trim(), "\r\n|\r|\n");
        foreach (var line in lines)
        {
            // get indent
            text = line.TrimStart();
            var _text = text;
            // get comment
            var m = CommentRegex().Match(text);
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
        else if (text.StartsWith("if", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("elif", StringComparison.OrdinalIgnoreCase))
        {
            return ParseIfelse(text);
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
        else if (text.StartsWith("break", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("continue", StringComparison.OrdinalIgnoreCase))
        {
            return ParseLoopCtrl(text);
        }
        else if (text.Equals("next", StringComparison.OrdinalIgnoreCase))
            return new Next();
        else if (text.StartsWith("func", StringComparison.OrdinalIgnoreCase))
            return ParseFuncDecl(text);
        else if (text.Equals("endfunc", StringComparison.OrdinalIgnoreCase))
            return new EndFuncStat();
        else if (text.Equals("return", StringComparison.OrdinalIgnoreCase))
            return new ReturnStat();
        else if (int.TryParse(text, out int duration))
            return new Wait(duration, true);
        else
            return ParseNamedExpression(text) ?? ParseKey(text);
    }

    public List<Statement> Parse(string text)
    {
        int address = 0;

        var _blocks = new Stack<Statement>();
        var _elifs = new Stack<BranchOp>();
        var _funcTables = new Dictionary<string, bool>();
        var unit = new Stack<List<Statement>>();
        unit.Push([]);
        var result = unit.Peek();
        void startblock()
        {
            unit.Push([]);
            result = unit.Peek();
        }

        _funcTables.Add("PRINT", false);
        _funcTables.Add("ALERT", false);
        _funcTables.Add("RAND", false);
        _funcTables.Add("TIME", false);
        _funcTables.Add("BEEP", false);

        foreach (var args in ParseLines(text))
        {
            try
            {
                var st = ParseStatement(args.Text) ?? throw new ParseException("格式错误");
                st.Comment = args.Comment;
                // update address
                st.Address = address;

                if (st is ForStmt || (st is IfStmt && st is not ElseIf) || st is FunctionStmt)
                {
                    _blocks.Push(st);
                    if (st is FunctionStmt fst)
                    {
                        if (_funcTables.ContainsKey(fst.Label))
                        {
                            throw new ParseException("重复定义的函数名", address);
                        }
                        _funcTables[fst.Label] = true;
                    }
                }
                // for/next
                if (st is LoopCtrl loopstat)
                {
                    var forcount = (from forstate in _blocks
                                    where forstate is Statements.ForStmt
                                    select forstate).Count();
                    if (loopstat.Level.Val > forcount)
                        throw new ParseException("循环层数不足", address);
                }
                if (st is Next nextstat)
                {
                    if (_blocks.Count == 0)
                        throw new ParseException("多余的语句", address);
                    var last = _blocks.Peek();
                    if (last is ForStmt lastfor)
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
                    if (last is Statements.IfStmt lastif)
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
                if (st is Else selse)
                {
                    if (_blocks.Count == 0)
                        throw new ParseException("多余的语句", address);
                    var last = _blocks.Peek();
                    if (last is IfStmt lastif)
                    {
                        if (lastif.Else != null)
                        {
                            throw new ParseException("一个If只能对应一个Else", address);
                        }
                        lastif.Else = st as BranchOp;
                        selse.If = lastif;
                    }
                    else
                    {
                        throw new ParseException("else需要配合if语句使用", address);
                    }
                }
                if (st is EndIf endifst)
                {
                    if (_blocks.Count == 0)
                        throw new ParseException("多余的语句", address);
                    var last = _blocks.Peek();
                    if (last is Statements.IfStmt lastif)
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

                //if (st is ForStmt || (st is IfStmt and not ElseIf) || st is FunctionStmt)
                //{
                //    startblock();
                //    if (st is FunctionStmt fst)
                //    {
                //        if (unit.Count > 1) throw new ParseException("函数必须在顶层定义");
                //        if (_funcTables.ContainsKey(fst.Label))
                //        {
                //            throw new ParseException("重复定义的函数名", address);
                //        }
                //        _funcTables[fst.Label] = true;
                //    }
                //}
                //else if (st is LoopCtrl loopstat)
                //{
                //    var forcount = (from forstate in _blocks
                //                    where forstate is ForStmt
                //                    select forstate).Count();
                //    if (loopstat.Level.Val > forcount)
                //        throw new ParseException("循环层数不足", address);
                //}
                //else if (st is Next)
                //{
                //    if (result.First() is not ForStmt lastfor)
                //    {
                //        throw new ParseException("NEXT需要对应的For语句", address);
                //    }
                //    if (unit.Count == 1) throw new ParseException("多余的语句");
                //    var body = unit.Pop();
                //    st = new BlockStatement("For", [.. body, st]);
                //    result = unit.Peek();
                //}
                //else if(st is ElseIf)
                //{
                //    if (result.First() is not IfStmt)
                //    {
                //        throw new ParseException("ELIF需要对应的If语句", address);
                //    }
                //    if (result.OfType<Else>().Any())
                //        throw new ParseException("Else语句后不能再接Elif", address);
                //}
                //else if(st is Else)
                //{
                //    if (result.First() is not IfStmt)
                //    {
                //        throw new ParseException("ELSE需要对应的If语句", address);
                //    }
                //    if (result.OfType<Else>().Any())
                //        throw new ParseException("一个If只能对应一个Else", address);
                //}
                //else if(st is EndIf)
                //{
                //    if (result.First() is not IfStmt)
                //    {
                //        throw new ParseException("ENDIF需要对应的If语句", address);
                //    }
                //    if (unit.Count == 1) throw new ParseException("多余的语句");
                //    var body = unit.Pop();
                //    st = new BlockStatement("If", [.. body, st]);
                //    result = unit.Peek();
                //}
                //else if (st is EndFuncStat)
                //{
                //    if (result.First() is not FunctionStmt lastfor)
                //    {
                //        throw new ParseException("ENDFUNC需要对应的Func语句", address);
                //    }
                //    if (unit.Count == 1) throw new ParseException("多余的语句");
                //    var body = unit.Pop();
                //    st = new BlockStatement("Func", [.. body, st]);
                //    result = unit.Peek();
                //}
                //result.Add(st);

                result.Add(st);
                address += 1;
            }
            catch (OverflowException)
            {
                throw new Exception("数值溢出");
            }
            catch (ParseException ex)
            {
                ex.Index = address;
                throw;
            }
            catch (Exception e)
            {
                Console.WriteLine($"{e.Message} 行：{address + 1}");
                throw new ParseException(e.Message, address);
            }
        }

        // pair Call
        result.OfType<CallStat>().ToList().ForEach(cst =>
        {
            if (!_funcTables.TryGetValue(cst.FnName, out _))
                throw new ParseException($"找不到调用的函数\"{cst.FnName}\"", cst.Address);
        });

        if (_blocks.Count != 0)
            throw new ParseException("发现未结束的语句", _blocks.Peek().Address);
        //if (unit.Count > 1)
        //    throw new ParseException("发现未结束的语句", unit.Peek().First().Address);
        // pair Call
        //result.SelectMany(st =>
        //{
        //    return st switch
        //    {
        //        BlockStatement bst => bst.Statements,
        //        _ => [st],
        //    };
        //}).OfType<CallStat>().ToList().ForEach(cst =>
        //{
        //    if (!_funcTables.TryGetValue(cst.FnName, out _))
        //        throw new ParseException($"找不到调用函数\"{cst.FnName}\"", cst.Address);
        //});

        return result;
    }
}

record ParserArgument
{
    public string Text { get; init; } = string.Empty;
    public Formatter Formatter { get; init; }
    public string Comment { get; init; } = string.Empty;
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
