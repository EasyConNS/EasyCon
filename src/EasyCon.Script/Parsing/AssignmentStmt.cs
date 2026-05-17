namespace EasyCon.Script.Syntax;

class AssignmentStmt(Token syntax, TargetExpr target, Token assignmentToken, BaseExpr value, TypeClauseSyntax? typeClause = null) : Statement(syntax)
{
    public readonly TargetExpr Target = target;
    public readonly Token AssignmentToken = assignmentToken;
    public readonly BaseExpr Expression = value;
    public readonly TypeClauseSyntax? TypeClause = typeClause;

    protected override string _GetString()
    {
        var typePart = TypeClause != null ? $": {TypeClause.TypeName.ToUpper()}" : "";
        return $"{Target.GetCodeText()}{typePart} {AssignmentToken.Value} {Expression.GetCodeText()}";
    }
}