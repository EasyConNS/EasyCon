using System.Collections.ObjectModel;
using VBF.Compilers.Scanners;

namespace ECP.Ast;
public class IntArrayType : Expression
{
    public IntArrayType(IList<Lexeme> items)
    {
        if (items == null)
        {
            Items = new ReadOnlyCollection<Number>([]);
            return;
        }
        Items = new ReadOnlyCollection<Number>(items.Select(i=>new Number(i.Value)).ToList());
    }

    public ReadOnlyCollection<Number> Items { get; private set; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException();
    }
}