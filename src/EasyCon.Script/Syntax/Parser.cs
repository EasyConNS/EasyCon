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
        Advance();
        return new(_text, TokenType.BadToken, "", Current.Span.Start);
    }

    private delegate bool matchHandler(TokenType type);

    private Token Match(matchHandler handle, string message = "")
    {
        if (handle(Current.Type))
        {
            return Advance();
        }
        _diagnostics.ReportUnexpectedToken(Current.Location, Current, TokenType.ASSIGN);
        return new(_text, TokenType.BadToken, "", Current.Span.Start);
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
                st = ParseStatement();

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
                    _diagnostics.ReportBadStruct(st.Syntax.Location, "导入只能在脚本开头");
                }
            }
            if (st.Kind == StatementKind.ExternFuncDeclaration)
            {
                if (unit.Count > 1)
                {
                    _diagnostics.ReportBadStruct(st.Syntax.Location, "EXTERN 声明必须在顶层");
                    st = new EmptyStmt();
                }
            }
            if (st.Kind == StatementKind.ForStmt || (st.Kind == StatementKind.IfStmt) || st.Kind == StatementKind.FuncStmt || st.Kind == StatementKind.WhileStmt)
            {
                if (st.Kind == StatementKind.FuncStmt)
                {
                    if (unit.Count > 1)
                    {
                        _diagnostics.ReportBadStruct(st.Syntax.Location, "函数必须在顶层定义");
                        // 跳过函数定义，继续解析
                        st = new EmptyStmt();
                    }
                }
                if (st is not EmptyStmt)
                    startblock();
            }
            else if (st.Kind == StatementKind.ElseIf)
            {
                if (result.First().Kind != StatementKind.IfStmt)
                {
                    _diagnostics.ReportBadStruct(st.Syntax.Location, "ELIF需要对应的If语句");
                    // 跳过 ELIF
                    st = new EmptyStmt();
                }
                else if (result.Any(s => s.Kind == StatementKind.Else))
                {
                    _diagnostics.ReportBadStruct(st.Syntax.Location, "Else语句后不能再接Elif");
                    // 跳过 ELIF
                    st = new EmptyStmt();
                }
            }
            else if (st.Kind == StatementKind.Else)
            {
                if (result.First().Kind != StatementKind.IfStmt)
                {
                    _diagnostics.ReportBadStruct(st.Syntax.Location, "ELSE需要对应的If语句");
                    // 跳过 ELSE
                    st = new EmptyStmt();
                }
                else if (result.Any(s => s.Kind == StatementKind.Else))
                {
                    _diagnostics.ReportBadStruct(st.Syntax.Location, "一个If只能对应一个Else");
                    // 跳过 ELSE
                    st = new EmptyStmt();
                }
            }
            else if (st.Kind == StatementKind.EndBlock || st.Kind == StatementKind.Next || st.Kind == StatementKind.EndIf || st.Kind == StatementKind.EndFuncStmt)
            {
                if (unit.Count == 1)
                {
                    _diagnostics.ReportBadStruct(st.Syntax.Location, "多余的结束语句");
                    // 跳过多余的结束语句
                    st = new EmptyStmt();
                }
                else
                {
                    var endStmt = st;
                    bool validEnd = true;

                    if (endStmt.Kind == StatementKind.EndIf && result.First().Kind != StatementKind.IfStmt)
                    {
                        _diagnostics.ReportBadStruct(st.Syntax.Location, "ENDIF需要对应的If语句");
                        validEnd = false;
                    }
                    else if (endStmt.Kind == StatementKind.Next && result.First().Kind != StatementKind.ForStmt)
                    {
                        _diagnostics.ReportBadStruct(st.Syntax.Location, "NEXT需要对应的For语句");
                        validEnd = false;
                    }
                    else if (endStmt.Kind == StatementKind.EndFuncStmt && result.First().Kind != StatementKind.FuncStmt)
                    {
                        _diagnostics.ReportBadStruct(st.Syntax.Location, "ENDFUNC需要对应的Func语句");
                        validEnd = false;
                    }
                    else if (result.First().Kind != StatementKind.IfStmt && result.First().Kind != StatementKind.ForStmt && result.First().Kind != StatementKind.WhileStmt && result.First().Kind != StatementKind.FuncStmt)
                    {
                        _diagnostics.ReportBadStruct(st.Syntax.Location, "END需要对应的语句开头");
                        validEnd = false;
                    }

                    if (validEnd)
                    {
                        var body = unit.Pop();

                        st = result.First().Kind switch
                        {
                            StatementKind.IfStmt => new IfBlock((IfStmt)result.First(), [.. body.Skip(1)], (EndBlockStmt)endStmt) { Address = result.First().Address },
                            StatementKind.ForStmt => new ForBlock((ForStmt)result.First(), [.. body.Skip(1)], (EndBlockStmt)endStmt) { Address = result.First().Address },
                            StatementKind.WhileStmt => new WhileBlock((WhileStmt)result.First(), [.. body.Skip(1)], (EndBlockStmt)endStmt) { Address = result.First().Address },
                            StatementKind.FuncStmt => new FuncDeclBlock((FuncStmt)result.First(), [.. body.Skip(1)], (EndBlockStmt)endStmt) { Address = result.First().Address },
                            _ => st // 保持原样
                        };
                        result = unit.Peek();
                    }
                    else
                    {
                        // 跳过结束语句
                        st = new EmptyStmt();
                    }
                }
            }

            result.Add(st);
            address += 1;

        }
        if (unit.Count > 1)
        {
            var first = unit.Peek().First();
            _diagnostics.ReportBadStruct(first.Syntax.Location, "语句块没有正确结束");
        }

        // lib 脚本后置校验：顶层只允许变量定义、常量定义和函数定义
        if (_syntaxTree.IsLib)
        {
            foreach (var st in result)
            {
                if (st is EmptyStmt or FuncDeclBlock or ConstantDeclStmt or AssignmentStmt or ExternFuncStmt)
                    continue;
                _diagnostics.ReportBadStruct(st.Syntax.Location, "库脚本只允许变量定义、常量定义和函数定义");
            }
        }

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