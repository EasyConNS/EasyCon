namespace EasyCon.Script.Syntax;

class FieldAssignStmt(Token syntax, ExprBase target, Token fieldToken, Token assignOp, ExprBase value) : Statement(syntax)
{
    public override StatementKind Kind => StatementKind.FieldAssign;
    public readonly ExprBase Target = target;
    public readonly Token FieldToken = fieldToken;
    public readonly string FieldName = fieldToken.Value;
    public readonly Token AssignmentToken = assignOp;
    public readonly ExprBase Expression = value;

    protected override string _GetString()
        => $"{Target.GetCodeText()}.{FieldName} {AssignmentToken.Value} {Expression.GetCodeText()}";
}