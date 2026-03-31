using EasyCon.Script2.Syntax;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace EasyCon.Script.Parsing;

abstract class Statement(Token syntax)
{
    public Token Syntax { get; } = syntax;
    public int Address = -1;
    public string Comment { get; set; } = string.Empty;

    protected abstract string _GetString();

    public string GetCodeText()
    {
        return $"{_GetString()}{Comment}";
    }
}

class EmptyStmt() : Statement(null!)
{
    protected override string _GetString() => "";
}

abstract class StartBlockStmt(Token syntax) : Statement(syntax)
{}

class EndBlockStmt(Token syntax) : Statement(syntax)
{
    protected override string _GetString() => "END";
}

sealed class ImportStmt(Token syntax, Token model, string path = "") : Statement(syntax)
{
    internal readonly Token Model = model;
    internal readonly string InitPath = path;
    internal string Lib => Model.STRTrimQ();

    public string FullFileName => Path.Combine(InitPath, Lib);
    protected override string _GetString() => $"IMPORT \"{Lib}\"";
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
        else if (node is WhileBlock whb)
        {
            WriteWhileBlock(whb, writer);
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
    private static void WriteWhileBlock(WhileBlock node, IndentedTextWriter writer)
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
        if (node is ElseIf || node is Else || node is EndBlockStmt)
        {
            writer.Indent--;
        }

        writer.Write(node.GetCodeText());

        if (node is ForStmt || node is FuncStmt || node is IfStmt || node is Else || node is WhileStmt)
        {
            writer.Indent++;
        }
        writer.WriteLine();
    }
}