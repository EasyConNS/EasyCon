using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

/// <summary>
/// Represents a constant declaration statement (_constant = value)
/// </summary>
class ConstantDeclStmt(Token syntax, VariableExpr constant, Token assignmentToken, ExprBase value) : Statement(syntax)
{
    public readonly VariableExpr Constant = constant;
    public readonly Token AssignmentToken = assignmentToken;
    public readonly ExprBase Expression = value;

    protected override string _GetString()
    {
        return $"{Constant.GetCodeText()} {AssignmentToken.Value} {Expression.GetCodeText()}";
    }
}