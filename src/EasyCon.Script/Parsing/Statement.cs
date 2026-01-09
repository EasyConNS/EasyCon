using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace EasyCon.Script.Parsing;

abstract class Statement
{
    public int Address = -1;
    public string Comment { get; set; } = string.Empty;

    protected abstract string _GetString();

    public string GetCodeText()
    {
        return $"{_GetString()}{Comment}";
    }
}

class EmptyStmt : Statement
{

    protected override string _GetString() => "";
}

sealed class ImportStmt(string path) : Statement
{
    internal readonly string LibPath = path;

    protected override string _GetString() => $"IMPORT \"{LibPath}\"";
}

sealed class CompicationUnit(ImmutableArray<Statement> members)
{
    public readonly ImmutableArray<Statement> Members = members;
}

internal static class FormatPrinter
{
    public static void WriteTo(this Statement node, IndentedTextWriter writer)
    {
        if (node is IfBlock block)
        {
            WriteIfBlock(block, writer);
        }
        else if (node is ForBlock fb)
        {
            WriteForBlock(fb, writer);
        }
        else if (node is FuncDeclBlock fnb)
        {
            WriteFunctionBlock(fnb, writer);
        }
        else
        {
            WriteStatementInternal(node, writer);
        }
    }
    private static void WriteIfBlock(IfBlock node, IndentedTextWriter writer)
    {
        node.Condition.WriteTo(writer);
        foreach (var s in node.Statements)
            s.WriteTo(writer);
        node.End.WriteTo(writer);
    }
    private static void WriteForBlock(ForBlock node, IndentedTextWriter writer)
    {
        node.Condition.WriteTo(writer);
        foreach (var s in node.Statements)
            s.WriteTo(writer);
        node.End.WriteTo(writer);
    }
    private static void WriteFunctionBlock(FuncDeclBlock node, IndentedTextWriter writer)
    {
        node.Declare.WriteTo(writer);
        foreach (var s in node.Statements)
            s.WriteTo(writer);
        node.End.WriteTo(writer);
    }
    private static void WriteStatementInternal(Statement node, IndentedTextWriter writer)
    {
        if (node is ElseIf || node is Else || node is EndIf || node is Next || node is EndFuncStmt)
        {
            writer.Indent--;
        }

        writer.Write(node.GetCodeText());

        if (node is ForStmt || node is FuncStmt || node is IfStmt || node is Else)
        {
            writer.Indent++;
        }
        writer.WriteLine();
    }
}