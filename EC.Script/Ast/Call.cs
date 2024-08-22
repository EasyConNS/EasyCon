using System.Collections.ObjectModel;
using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Call : Expression
{
    public Call(LexemeValue package, LexemeValue methodName, IList<Expression> argList)
    {
        Package = package;
        Method = new MethodRef(methodName);
        Arguments = new ReadOnlyCollection<Expression>(argList);
    }

    public LexemeValue Package { get; private set; }
    public MethodRef Method { get; private set; }
    public ReadOnlyCollection<Expression> Arguments { get; private set; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitCall(this);
    }
}