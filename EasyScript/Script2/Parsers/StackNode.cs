namespace Compiler.Parsers;

internal class StackNode
{
    internal readonly StackNode PrevNode;
    internal readonly int StateIndex;
    internal object ReducedValue;

    public StackNode(int stateIndex, StackNode prev, object value)
    {
        StateIndex = stateIndex;
        PrevNode = prev;
        ReducedValue = value;
    }
}