using System.CodeDom.Compiler;
using EasyScript.Statements;

namespace EasyScript.Parsing;

abstract class Statement
{
    public int Address = -1;
    public string Comment { get; set; }

    public abstract void Exec(Processor processor);

    public abstract void Assemble(Assembly.Assembler assembler);

    public string GetString()
    {
        return $"{_GetString()} {Comment}";
    }

    protected abstract string _GetString();

    protected static class ErrorMessage
    {
        public const string NotSupported = "脚本中存在仅支持联机模式的语句";
        public const string NotImplemented = "类型未定义，这可能是一个bug，请汇报给作者";
    }
}

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
        if(node is ElseIf || node is Else || node is EndIf || node is Next || node is EndFuncStat)
        {
            writer.Indent--;
        }

        writer.Write(node.GetString());

        if (node is For || node is FunctionStmt || node is If || node is ElseIf || node is Else)
        {
            writer.Indent++;
        }

        writer.WriteLine();
    }
}