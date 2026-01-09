using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

internal sealed class Lowerer
{
    public static BoundBlockStatement Flatten(BoundStmt statement)
    {
        var builder = ImmutableArray.CreateBuilder<BoundStmt>();
        var stack = new Stack<BoundStmt>();
        stack.Push(statement);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (current is BoundBlockStatement block)
            {
                foreach (var s in block.Statements.Reverse())
                    stack.Push(s);
            }
            else
            {
                builder.Add(current);
            }
        }
        return new BoundBlockStatement(statement.Syntax, builder.ToImmutable());
    }
}