using System.Collections.ObjectModel;
using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Call : Expression
{
    public Call(Expression target, LexemeValue methodName, IList<Expression> argList)
    {
        Target = target;
        Method = new MethodRef(methodName);
        Arguments = new ReadOnlyCollection<Expression>(argList);
    }

    public Expression Target { get; private set; }
    public MethodRef Method { get; private set; }
    public ReadOnlyCollection<Expression> Arguments { get; private set; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitCall(this);
    }
}