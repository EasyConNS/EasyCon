using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class OpAssign : Binary
{
    public OpAssign(string op, LexemeValue name, Number num) : base(op+"=", new Number(name), num)
    {
    }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitOpAssign(this);
    }
}
