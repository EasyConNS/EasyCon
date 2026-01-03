using System.CodeDom.Compiler;
using EasyCon.Script2.Syntax;

namespace EasyCon.Script2.Binding;

internal static class BoundNodePrinter
{
    public static void WriteTo(this BoundNode node, TextWriter writer)
    {
        if (writer is IndentedTextWriter iw)
            WriteTo(node, iw);
        else
            WriteTo(node, new IndentedTextWriter(writer));
    }

    public static void WriteTo(this BoundNode node, IndentedTextWriter writer)
    {
        // 这里应该根据节点类型调用相应的写入方法
        // 由于缺少完整的BoundNode层次结构，这里提供一个基本框架
    }

    public static string GetOperatorText(TokenType type)
    {
        return TokenFacts.GetText(type) ?? type.ToString();
    }
}