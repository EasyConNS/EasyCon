using EasyCon.Script.Text;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

enum StatementKind
{
    CommonAction,
    // Block statements
    ForBlock,
    WhileBlock,
    IfBlock,
    FuncDeclBlock,
    // Statement keywords
    ForStmt,
    WhileStmt,
    IfStmt,
    ElseIf,
    Else,
    EndIf,
    EndBlock,
    FuncStmt,
    EndFuncStmt,
    Next,
    Break,
    Continue,
    ReturnStmt,
    Import,
    ExternFuncDeclaration,
}

abstract class Statement(Token syntax) : AstNode(syntax)
{
    public int Address = -1;
    public string Comment { get; set; } = string.Empty;

    public virtual StatementKind Kind => StatementKind.CommonAction;

    public TextLocation Location => Syntax.Location;

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
{ }

class EndBlockStmt(Token syntax) : Statement(syntax)
{
    public override StatementKind Kind => StatementKind.EndBlock;
    protected override string _GetString() => "END";
}

sealed class ImportStmt(Token syntax, Token model, string path = "") : Statement(syntax)
{
    public override StatementKind Kind => StatementKind.Import;
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
        if (node.Kind == StatementKind.IfBlock)
        {
            WriteIfBlock((IfBlock)node, writer);
        }
        else if (node.Kind == StatementKind.ForBlock)
        {
            WriteForBlock((ForBlock)node, writer);
        }
        else if (node.Kind == StatementKind.WhileBlock)
        {
            WriteWhileBlock((WhileBlock)node, writer);
        }
        else if (node.Kind == StatementKind.FuncDeclBlock)
        {
            WriteFunctionBlock((FuncDeclBlock)node, writer);
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
        if (node.Kind == StatementKind.ElseIf || node.Kind == StatementKind.Else || node.Kind == StatementKind.EndIf || node.Kind == StatementKind.Next || node.Kind == StatementKind.EndFuncStmt)
        {
            writer.Indent--;
        }

        writer.Write(node.GetCodeText());

        if (node.Kind == StatementKind.ForStmt || node.Kind == StatementKind.FuncStmt || node.Kind == StatementKind.IfStmt || node.Kind == StatementKind.Else || node.Kind == StatementKind.WhileStmt)
        {
            writer.Indent++;
        }
        writer.WriteLine();
    }
}