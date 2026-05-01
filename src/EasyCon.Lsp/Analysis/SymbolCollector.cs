using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Lsp.Analysis;

internal sealed record SymbolInfo(string Name, string Kind, int Line, int Character);

internal static class SymbolCollector
{
    public static List<SymbolInfo> CollectSymbols(CompicationUnit root)
    {
        var symbols = new List<SymbolInfo>();
        foreach (var stmt in root.Members)
            CollectFromStatement(stmt, symbols);
        return symbols;
    }

    private static void CollectFromStatement(Statement stmt, List<SymbolInfo> symbols)
    {
        switch (stmt)
        {
            case ConstantDeclStmt c:
                symbols.Add(new(c.Constant.Tag, "constant", c.Line, CharOffset(c.Constant.Syntax)));
                break;
            case AssignmentStmt a when a.Target is VariableExpr v:
                symbols.Add(new(v.Tag, "variable", a.Line, CharOffset(v.Syntax)));
                break;
            case FuncDeclBlock fb:
                var f = fb.Declare;
                symbols.Add(new(f.Name, "function", f.Line, CharOffset(f.Identifier)));
                foreach (var p in f.Paramters)
                    symbols.Add(new(p.Identifier.Tag, "parameter", p.Identifier.Line, CharOffset(p.Identifier.Syntax)));
                break;
            case ExternFuncStmt ef:
                symbols.Add(new(ef.Name, "function", ef.Line, CharOffset(ef.Identifier)));
                break;
            case IfBlock ib:
                foreach (var s in ib.Statements)
                    CollectFromStatement(s, symbols);
                break;
            case ForBlock fb:
                foreach (var s in fb.Statements)
                    CollectFromStatement(s, symbols);
                if (fb.Condition is For_Full ff)
                    symbols.Add(new(ff.RegIter.Tag, "variable", ff.Line, CharOffset(ff.RegIter.Syntax)));
                break;
            case WhileBlock wb:
                foreach (var s in wb.Statements)
                    CollectFromStatement(s, symbols);
                break;
            case StructDeclBlock sb:
                symbols.Add(new(sb.Header.Name, "struct", sb.Line, CharOffset(sb.Syntax)));
                foreach (var field in sb.Fields)
                    symbols.Add(new($"${field.Name}", "field", field.Line, CharOffset(field.Syntax)));
                break;
        }
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