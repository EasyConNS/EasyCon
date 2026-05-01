using EasyCon.Script.Syntax;
using EmmyLua.LanguageServer.Framework.Protocol.Model;

namespace EasyCon.Lsp.Analysis;

internal static class DefinitionFinder
{
    public static DocumentRange? FindDefinition(CompicationUnit? root, string? lineText, Position position)
    {
        if (lineText == null || root == null) return null;

        var word = SourceHelper.ExtractWord(lineText, position.Character);
        if (string.IsNullOrEmpty(word)) return null;

        if (word.StartsWith("$"))
            return FindVariable(root, word);
        if (word.StartsWith("_"))
            return FindConstant(root, word);
        if (word.StartsWith("@"))
            return null;
        // 先查找函数，如果没找到再查找结构体
        var funcResult = FindFunction(root, word);
        if (funcResult != null) return funcResult;
        return FindStruct(root, word);
    }

    private static DocumentRange? FindVariable(CompicationUnit root, string name)
    {
        foreach (var stmt in root.Members)
        {
            var loc = FindVariableInStatement(stmt, name);
            if (loc != null) return loc;
        }
        return null;
    }

    private static DocumentRange? FindVariableInStatement(Statement stmt, string name)
    {
        switch (stmt)
        {
            case AssignmentStmt a when a.Target is VariableExpr v && v.Tag == name:
                return StmtRange(a);
            case FuncDeclBlock fb:
                foreach (var p in fb.Declare.Paramters)
                {
                    if (p.Identifier.Tag == name)
                        return TokenRange(p.Identifier.Syntax);
                }
                foreach (var s in fb.Statements)
                {
                    var loc = FindVariableInStatement(s, name);
                    if (loc != null) return loc;
                }
                break;
            case ForBlock fb:
                if (fb.Condition is For_Full ff && ff.RegIter.Tag == name)
                    return StmtRange(fb.Condition);
                foreach (var s in fb.Statements)
                {
                    var loc = FindVariableInStatement(s, name);
                    if (loc != null) return loc;
                }
                break;
            case IfBlock ib:
                foreach (var s in ib.Statements)
                {
                    var loc = FindVariableInStatement(s, name);
                    if (loc != null) return loc;
                }
                break;
            case WhileBlock wb:
                foreach (var s in wb.Statements)
                {
                    var loc = FindVariableInStatement(s, name);
                    if (loc != null) return loc;
                }
                break;
        }
        return null;
    }

    private static DocumentRange? FindConstant(CompicationUnit root, string name)
    {
        foreach (var stmt in root.Members)
        {
            if (stmt is ConstantDeclStmt c && c.Constant.Tag == name)
                return StmtRange(c);
        }
        return null;
    }

    private static DocumentRange? FindFunction(CompicationUnit root, string name)
    {
        foreach (var stmt in root.Members)
        {
            switch (stmt)
            {
                case FuncDeclBlock fb when string.Equals(fb.Declare.Name, name, StringComparison.OrdinalIgnoreCase):
                    return TokenRange(fb.Declare.Identifier);
                case ExternFuncStmt ef when string.Equals(ef.Name, name, StringComparison.OrdinalIgnoreCase):
                    return TokenRange(ef.Identifier);
            }
        }
        return null;
    }

    private static DocumentRange? FindStruct(CompicationUnit root, string name)
    {
        foreach (var stmt in root.Members)
        {
            if (stmt is StructDeclBlock sb && string.Equals(sb.Header.Name, name, StringComparison.OrdinalIgnoreCase))
                return TokenRange(sb.Syntax);
        }
        return null;
    }

    private static DocumentRange StmtRange(Statement stmt)
    {
        var line = stmt.Line - 1;
        var col = stmt.Syntax != null ? CharOffset(stmt.Syntax) : 0;
        var len = stmt.Syntax?.Value.Length ?? 0;
        return new(new(line, col), new(line, col + len));
    }

    private static DocumentRange TokenRange(Token? token)
    {
        if (token == null) return new(new(0, 0), new(0, 0));
        var line = token.Location.StartLine;
        var col = CharOffset(token);
        return new(new(line, col), new(line, col + token.Value.Length));
    }

    private static int CharOffset(Token token)
    {
        if (token.Text == null) return 0;
        var line = token.Location.StartLine;
        if (line >= 0 && line < token.Text.Lines.Length)
            return token.Span.Start - token.Text.Lines[line].Start;
        return 0;
    }
}