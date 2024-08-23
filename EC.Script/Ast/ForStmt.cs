using ECScript.Syntax;

namespace ECP.Ast;

internal sealed class ForStmt : Statement
{
    public ForStmt(SyntaxNode node, Variable variable, Expression lowerBound, Expression upperBound, Statement body, SyntaxToken breakLabel, SyntaxToken continueLabel) : base(node)
    {
        LoopCount = variable;
    }

    public Variable LoopCount { get; init; }

    public override AstNodeType Kind => AstNodeType.ForStatement;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException();
    }
}