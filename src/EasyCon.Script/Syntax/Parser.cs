using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

internal sealed partial class Parser
{
    private Formatter _formatter { get; init; }
    private readonly DiagnosticBag _diagnostics = [];
    private readonly SyntaxTree _syntaxTree;
    private readonly SourceText _text;
    private readonly ImmutableArray<Token> _tokens;

    public Parser(SyntaxTree syntaxTree)
    {
        _formatter = new();
        _syntaxTree = syntaxTree;
        _text = syntaxTree.Text;
        var lexer = new Lexer(_syntaxTree);
        _tokens = lexer.Tokenize();
        _diagnostics.AddRange(lexer.Diagnostics);
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    string _filePath => Path.GetDirectoryName(Path.GetFullPath(_text.FileName)) ?? "";
    const string LibPath = "lib/";

    public CompicationUnit ParseProgram()
    {
        return ParseUnit();
    }

    private static IEnumerable<ImmutableArray<Token>> GroupTokensByNewline(ImmutableArray<Token> allTokens)
    {
        var currentGroup = new List<Token>();
        
        foreach (var token in allTokens)
        {
            if (token.Type == TokenType.NEWLINE)
            {
                yield return [.. currentGroup];
                currentGroup.Clear();
            }
            else if (token.Type == TokenType.EOF) break;
            else
            {
                currentGroup.Add(token);
            }
        }
        
        // Add the last group if it's not empty
        if (currentGroup.Count > 0)
        {
            yield return [.. currentGroup];
        }
    }

    private CompicationUnit ParseUnit()
    {
        int address = 0;

        var unit = new Stack<List<Statement>>();
        unit.Push([]);
        var result = unit.Peek();
        void startblock()
        {
            unit.Push([]);
            result = unit.Peek();
        }

        // Group tokens by NEWLINE
        var tokenGroups = GroupTokensByNewline(_tokens);
        
        // Process each group of tokens
        foreach (var tokens in tokenGroups)
        {
            try
            {
                Statement? st = null;
                
                // If there's only one token and it's a comment, create a CommentStmt
                if (tokens.Length == 1 && tokens[0].Type == TokenType.COMMENT)
                {
                    st = new EmptyStmt()
                    {
                        Comment = tokens[0].Value
                    };
                }
                // If there are multiple tokens, only the last one can be a comment
                else if (tokens.Length >= 1)
                {
                    var toks = tokens;
                    var lastToken = tokens[^1];

                    // Check if the last token is a comment
                    if (lastToken.Type == TokenType.COMMENT)
                    {
                        // Remove the comment token for parsing the statement
                        toks = toks[..^1];
                    }
                    
                    // Parse the tokens directly
                    st = ParseStatement(toks) ?? throw new ParseException("格式错误");
                    
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
                        if (unit.Count > 1) throw new ParseException("函数必须在顶层定义");
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
                throw new ParseException("不支持嵌套引用", imp.Address);

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
        if(tok.Type != TokenType.STRING) return tok.ToString();

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
