using System.Collections;
using System.Collections.Generic;

namespace Compiler.Parsers;

sealed class RepeatParserListNode<T> : IEnumerable<T>
{
    public RepeatParserListNode(T value, RepeatParserListNode<T> next)
    {
        Value = value;
        Next = next;
    }

    public RepeatParserListNode() : this(default(T), null) { }
    public T Value { get; private set; }
    public RepeatParserListNode<T> Next { get; private set; }


    public IEnumerator<T> GetEnumerator()
    {
        RepeatParserListNode<T> current = this;

        while (current.Next != null)
        {
            yield return current.Value;

            current = current.Next;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}