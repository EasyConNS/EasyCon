using EasyCon.Script2.Ast;
using EasyCon.Script2.Symbols;

namespace EasyCon.Script2.Binding;

internal abstract class BoundExpression(ASTNode syntax) : BoundNode(syntax)
{
    public abstract TypeSymbol Type { get; }
    public virtual BoundConstant? ConstantValue => null;
}
