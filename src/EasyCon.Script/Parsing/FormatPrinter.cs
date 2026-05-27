using EasyCon.Script.Text;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

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
        else if (node.Kind == StatementKind.UntilBlock)
        {
            WriteUntilBlock((UntilBlock)node, writer);
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
    private static void WriteUntilBlock(UntilBlock node, IndentedTextWriter writer)
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
        if (ClosesCurrentIndent(node.Kind))
        {
            writer.Indent--;
        }

        writer.Write(node.GetCodeText());

        if (OpensChildIndent(node.Kind))
        {
            writer.Indent++;
        }
        writer.WriteLine();
    }

    private static bool ClosesCurrentIndent(StatementKind kind) =>
        kind is StatementKind.EndBlock
            or StatementKind.ElseIf
            or StatementKind.Else
            or StatementKind.EndIf
            or StatementKind.Next
            or StatementKind.EndFuncStmt;

    private static bool OpensChildIndent(StatementKind kind) =>
        kind is StatementKind.ForStmt
            or StatementKind.FuncDecl
            or StatementKind.IfStmt
            or StatementKind.ElseIf
            or StatementKind.Else
            or StatementKind.WhileStmt
            or StatementKind.UntilStmt;
}