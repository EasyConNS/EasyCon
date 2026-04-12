using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

internal sealed partial class Parser
{
    private readonly DiagnosticBag _diagnostics = [];
    private readonly SyntaxTree _syntaxTree;
    private readonly SourceText _text;
    private readonly ImmutableArray<Token> _tokens;

    public Parser(SyntaxTree syntaxTree)
    {
        _syntaxTree = syntaxTree;
        _text = syntaxTree.Text;
        var lexer = new Lexer(_syntaxTree);
        _tokens = lexer.Tokenize();
        _diagnostics.AddRange(lexer.Diagnostics);
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    string _fullPath => _text.FileName != "" ? Path.GetFullPath(_text.FileName) : AppDomain.CurrentDomain.BaseDirectory;

    string _filePath => Path.GetDirectoryName(_fullPath) ?? "";
    const string LibPath = "lib/";

    #region Token Cursor

    // 当前行的 token 游标
    private int _start;
    private int _end;

    /// <summary>
    /// 当前行 token 分组的切片视图，直接索引访问无需计算偏移
    /// </summary>
    private ReadOnlySpan<Token> _grouptokens => _tokens.AsSpan()[_start.._end];
    private int _position = 0;

    private bool CursorEOF => _position >= _grouptokens.Length;

    private Token Peek(int offset = 0)
    {
        var index = _position + offset;
        if (index >= _grouptokens.Length)
            return _grouptokens[^1];

        return _grouptokens[index];
    }
    private Token Current => Peek();
    private Token Advance()
    {
        var now = Current;
        _position++;
        return now;
    }

    private bool Check(TokenType type) => Current?.Type == type;
    private Token Match(TokenType type, string message = "")
    {
        if (Current.Type == type)
        {
            return Advance();
        }
        _diagnostics.ReportUnexpectedToken(Current.Location, Current, type);
        return new(_text, TokenType.BadToken, "", Current.Line, Current.Span.Start);
    }

    private delegate bool matchHandler(TokenType type);

    private Token Match(matchHandler handle, string message = "")
    {
        if (handle(Current.Type))
        {
            return Advance();
        }
        _diagnostics.ReportUnexpectedToken(Current.Location, Current, TokenType.ASSIGN);
        return new(_text, TokenType.BadToken, "", Current.Line, Current.Span.Start);
    }

    private void MatchEOF()
    {
        if (!CursorEOF) _diagnostics.ReportInvalidEOF(Current.Location, Current);
    }

    /// <summary>
    /// 设置当前行范围并重置游标到行首
    /// </summary>
    private void SetRange(int start, int end)
    {
        _start = start;
        _end = end;
        _position = 0;
    }

    #endregion

    public CompicationUnit ParseProgram()
    {
        int address = 1;

        var unit = new Stack<List<Statement>>();
        unit.Push([]);
        var result = unit.Peek();
        void startblock()
        {
            unit.Push([]);
            result = unit.Peek();
        }

        foreach (var (rangeStart, rangeEnd) in GetTokenRanges())
        {
            SetRange(rangeStart, rangeEnd);

            Statement? st = null;
            if (_grouptokens.Length == 0)
                st = new EmptyStmt();
            // If there's only one token and it's a comment, create a CommentStmt
            else if (_grouptokens.Length == 1 && Current.Type == TokenType.COMMENT)
            {
                st = new EmptyStmt()
                {
                    Comment = Current.Value
                };
            }
            // If there are multiple tokens, only the last one can be a comment
            else if (_grouptokens.Length >= 1)
            {
                var lastToken = Peek(_grouptokens.Length - 1);

                // Check if the last token is a comment
                if (lastToken.Type == TokenType.COMMENT)
                {
                    // Shrink range to exclude comment token
                    _end--;
                }

                // Parse the tokens directly
                try
                {

                    st = ParseStatement();
                }catch(FormatException e)
                {
                    throw new ParseException($"表达式解析异常:{e.Message}", address);
                }

                // Set the comment if there was one
                if (lastToken.Type == TokenType.COMMENT)
                {
                    st.Comment = lastToken.Value;
                }
            }
            // Handle empty lines
            else
            {
                st = new EmptyStmt();
            }

            // update address
            st.Address = address;

            if (st is ImportStmt)
            {
                if (unit.SelectMany(u => u).Any(st => st is not ImportStmt && st is not EmptyStmt))
                {
                    throw new ParseException("导入只能在脚本开头");
                }
            }
            if (st is ForStmt || (st is IfStmt and not ElseIf) || st is FuncStmt || st is WhileStmt)
            {
                if (st is FuncStmt fst)
                {
                    if (unit.Count > 1) throw new ParseException("函数必须在顶层定义", address);
                }
                startblock();
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
            else if (st is EndBlockStmt comend)
            {
                if (unit.Count == 1) throw new ParseException("多余的结束语句");

                if (st is EndIf && result.First() is not IfStmt)
                {
                    throw new ParseException("ENDIF需要对应的If语句", address);
                }
                else if (st is Next && result.First() is not ForStmt)
                {
                    throw new ParseException("NEXT需要对应的For语句", address);
                }
                else if (st is EndFuncStmt && result.First() is not FuncStmt)
                {
                    throw new ParseException("ENDFUNC需要对应的Func语句", address);
                }
                else if (result.First() is not StartBlockStmt whilecond)
                {
                    throw new ParseException("END需要对应的语句开头", address);
                }

                var body = unit.Pop();

                st = result.First() switch
                {
                    IfStmt ifstart => new IfBlock(ifstart, [.. body.Skip(1)], comend)
                    {
                        Address = ifstart.Address
                    },
                    ForStmt forstart => new ForBlock(forstart, [.. body.Skip(1)], comend)
                    {
                        Address = forstart.Address
                    },
                    WhileStmt whilecond => new WhileBlock(whilecond, [.. body.Skip(1)], comend)
                    {
                        Address = whilecond.Address
                    },
                    FuncStmt funcdef => new FuncDeclBlock(funcdef, [.. body.Skip(1)], comend)
                    {
                        Address = funcdef.Address
                    },
                    _ => throw new ParseException("语句块格式不正确", address),
                };
                result = unit.Peek();
            }

            result.Add(st);
            address += 1;

        }
        if (unit.Count > 1)
            throw new ParseException("语句块没有正确结束", unit.Peek().First().Address);

        return new CompicationUnit([.. result]);
    }

    /// <summary>
    /// 遍历所有 token，按 NEWLINE 分组，返回每行在 _tokens 中的 (start, end) 范围
    /// </summary>
    private IEnumerable<(int start, int end)> GetTokenRanges()
    {
        int start = 0;
        int length = 0;

        for (int i = 0; i < _tokens.Length; i++)
        {
            var token = _tokens[i];
            if (token.Type == TokenType.NEWLINE)
            {
                yield return (start, start + length);
                start = i + 1;
                length = 0;
            }
            else if (token.Type == TokenType.EOF)
            {
                break;
            }
            else
            {
                if (length == 0)
                    start = i;
                length++;
            }
        }

        if (length > 0)
        {
            yield return (start, start + length);
        }
    }

    internal ImmutableArray<CompicationUnit> Flatten(CompicationUnit prog)
    {
        var imports = prog.Members.OfType<ImportStmt>();
        if (!imports.Any()) return [prog];

        var result = ImmutableArray.CreateBuilder<CompicationUnit>();
        foreach (var imp in imports)
        {
            Console.WriteLine($"正在加载库:{imp.FullFileName}");
            var newprog = SyntaxTree.Load(imp.FullFileName);
            if (newprog.Root.Members.OfType<ImportStmt>().Any())
                throw new Exception("不支持嵌套引用");

            result.Add(newprog.Root);
        }
        result.Add(new CompicationUnit([.. prog.Members.Except(imports)]));
        return result.ToImmutable();
    }
}

public static class TokExt
{
    public static string STRTrimQ(this Token tok)
    {
        if (tok.Type != TokenType.STRING) return tok.ToString();

        var val = tok.Value;
        if (val.Length >= 2 && val[0] == val[^1])
        {
            if (val[0] == '"' || val[0] == '\'')
                return val[1..^1];
        }
        return val;
    }
}

public class ParseException(string message, int index = -1) : Exception(message)
{
    public int Index = index;
}
