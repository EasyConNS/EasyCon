using EasyCon.Script.Binding;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EasyCon.Script.Parsing;

partial class Parser(IEnumerable<ExternalVariable> extVars)
{
    readonly Formatter _formatter = new(extVars);

    [GeneratedRegex("\r\n|\r|\n")]
    private static partial Regex LineRegex();
    [GeneratedRegex(@"(\s*#.*)$")]
    private static partial Regex CommentRegex();

    private IEnumerable<ParserArgument> ParseLines(string text)
    {
        var lines = LineRegex().Split(text.Trim());
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
            };
        }
    }

    private Statement? ParseStatement(string text)
    {
        if (text.Length == 0)
            return new EmptyStmt();
        if (text.StartsWith('_'))
        {
            return ParseConstantDecl(text);
        }
        else if (text.StartsWith('$'))
        {
            return ParseAssignment(text);
        }
        else if (text.StartsWith("import", StringComparison.OrdinalIgnoreCase))
        {
            return ParseImport(text);
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
            return new EndFuncStmt();
        else if (text.Equals("return", StringComparison.OrdinalIgnoreCase))
            return new ReturnStmt();
        else if (int.TryParse(text, out int duration))
            return new Wait(duration, true);
        else
            return ParseNamedExpression(text) ?? ParseKey(text);
    }

    public CompicationUnit ParseUnit(string text)
    {
        int address = 0;

        var _funcTables = new Dictionary<string, int>();
        var unit = new Stack<List<Statement>>();
        unit.Push([]);
        var result = unit.Peek();
        void startblock()
        {
            unit.Push([]);
            result = unit.Peek();
        }

        foreach (var item in BuiltinFunctions.GetAll())
        {
            _funcTables.Add(item.Name, item.Paramters.Length);
        }

        foreach (var args in ParseLines(text))
        {
            try
            {
                var st = ParseStatement(args.Text) ?? throw new ParseException("格式错误");
                st.Comment = args.Comment;
                // update address
                st.Address = address;

                if(st is ImportStmt)
                {
                    if(unit.SelectMany(u=>u).Any(st=>st is not ImportStmt && st is not EmptyStmt))
                    {
                        throw new ParseException("导入只能在脚本开头");
                    }
                }
                if (st is ForStmt || (st is IfStmt and not ElseIf) || st is FuncStmt)
                {
                    if (st is FuncStmt fst)
                    {
                        if (unit.Count > 1) throw new ParseException("函数必须在顶层定义");
                        if (_funcTables.ContainsKey(fst.Name))
                        {
                            throw new ParseException("重复定义的函数名", address);
                        }
                        _funcTables[fst.Name] = 0;
                    }
                    startblock();
                }
                else if (st is Next nst)
                {
                    if (result.First() is not ForStmt lastfor)
                    {
                        throw new ParseException("NEXT需要对应的For语句", address);
                    }
                    if (unit.Count == 1) throw new ParseException("多余的语句");
                    var body = unit.Pop();
                    st = new ForBlock(lastfor, [.. body.Skip(1)], nst);
                    st.Address = lastfor.Address;
                    result = unit.Peek();
                }
                else if (st is ElseIf)
                {
                    if (result.First() is not IfStmt)
                    {
                        throw new ParseException("ELIF需要对应的If语句", address);
                    }
                    if (result.OfType<Else>().Any())
                        throw new ParseException("Else语句后不能再接Elif", address);
                }
                else if (st is Else)
                {
                    if (result.First() is not IfStmt)
                    {
                        throw new ParseException("ELSE需要对应的If语句", address);
                    }
                    if (result.OfType<Else>().Any())
                        throw new ParseException("一个If只能对应一个Else", address);
                }
                else if (st is EndIf eifst)
                {
                    if (result.First() is not IfStmt ifstart)
                    {
                        throw new ParseException("ENDIF需要对应的If语句", address);
                    }
                    if (unit.Count == 1) throw new ParseException("多余的语句");
                    var body = unit.Pop();
                    st = new IfBlock(ifstart, [.. body.Skip(1)], eifst);
                    st.Address = ifstart.Address;
                    result = unit.Peek();
                }
                else if (st is EndFuncStmt efnst)
                {
                    if (result.First() is not FuncStmt funcdef)
                    {
                        throw new ParseException("ENDFUNC需要对应的Func语句", address);
                    }
                    if (unit.Count == 1) throw new ParseException("多余的语句");
                    var body = unit.Pop();
                    st = new FuncDeclBlock(funcdef, [.. body.Skip(1)], efnst);
                    st.Address = funcdef.Address;
                    result = unit.Peek();
                }

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
                throw new ParseException(e.Message, address);
            }
        }
        if (unit.Count > 1)
            throw new ParseException("语句块没有正确结束", unit.Peek().First().Address);

        return new CompicationUnit([.. result]);
    }

    public ImmutableArray<CompicationUnit> Parse(CompicationUnit prog)
    {
        var imports = prog.Members.OfType<ImportStmt>();
        if(!imports.Any()) return [prog];
        
        var result = ImmutableArray.CreateBuilder<CompicationUnit>();
        foreach(var imp in imports)
        {
            var newprog = ParseUnit(File.ReadAllText(LibPath+imp.LibPath));
            if(newprog.Members.OfType<ImportStmt>().Any()) 
                throw new ParseException("不支持嵌套引用", imp.Address);
           
            result.Add(newprog);
        }
        result.Add(new CompicationUnit([.. prog.Members.Except(imports)]));
        return result.ToImmutable();
    }
}

record ParserArgument
{
    public string Text { get; init; } = string.Empty;
    public string Comment { get; init; } = string.Empty;
}

public class ParseException(string message, int index = -1) : Exception(message)
{
    public int Index = index;
}
