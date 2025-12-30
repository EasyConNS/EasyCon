using EasyCon.Script2.Ast;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace EasyScript.Statements;

abstract class Statement
{
    public int Address = -1;
    public string Comment { get; set; } = string.Empty;

    public abstract void Exec(Processor processor);

    protected abstract string _GetString();

    public string GetString()
    {
        return $"{_GetString()}{Comment}";
    }
}

class Empty(string text = "") : Statement
{
    private readonly string Text = text;

    public override void Exec(Processor _) { }

    protected override string _GetString()
    {
        return Text;
    }
}

class BlockStatement(string name, ImmutableArray<Statement> statements) :Statement
{
    public readonly string Name = name;
    public readonly ImmutableArray<Statement> Statements = statements;

    public override void Exec(Processor processor)
    {
        //throw new NotImplementedException();
    }

    protected override string _GetString()
    {
        using var writer = new StringWriter();
        using var printer = new IndentedTextWriter(writer, "  ");
        Statements.ToList().ForEach(u => u.WriteTo(printer));
        return writer.ToString().Trim();
    }
}


//internal sealed class ScriptUnit(ImmutableArray<Statement> statements)
//{
//    public ImmutableArray<Statement> Statements = statements;
//    public ImmutableDictionary<string, FuncDeclStatment> Functions = statements
//        .OfType<FuncDeclStatment>()
//        .ToImmutableDictionary(f => f.Label, f => f);

//    public bool HaveKeyAction => Statements.OfType<KeyAction>().Any() && Statements.OfType<FuncDeclStatment>().SelectMany(st => st.Statements).OfType<KeyAction>().Any();
//}

internal static class BoundNodePrinter
{
    // public static void WriteTo(this Statement node, TextWriter writer)
    // {
    //     if (writer is IndentedTextWriter iw)
    //         WriteTo(node, iw);
    //     else
    //         WriteTo(node, new IndentedTextWriter(writer));
    // }
    public static void WriteTo(this Statement node, IndentedTextWriter writer)
    {
        if (node is ElseIf || node is Else || node is EndIf || node is Next || node is EndFuncStat)
        {
            writer.Indent--;
        }

        writer.Write(node.GetString());

        if (node is ForStmt || node is FunctionStmt || node is IfStmt || node is ElseIf || node is Else)
        {
            writer.Indent++;
        }
        writer.WriteLine();
    }
}