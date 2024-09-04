namespace EC.Script.Syntax;

public abstract class ExpressionSyntax : SyntaxNode
{
}

public abstract class ConditionSyntax : ExpressionSyntax
{
}

public abstract class StatementSyntax : ExpressionSyntax
{
}
