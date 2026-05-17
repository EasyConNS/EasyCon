using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

/// <summary>
/// Represents a constant declaration statement (_constant = value)
/// </summary>
class ConstantDeclStmt(Token syntax, ConstVarExpr constant, Token assignmentToken, BaseExpr value) : Statement(syntax)
{
    public readonly ConstVarExpr Constant = constant;
    public readonly Token AssignmentToken = assignmentToken;
    public readonly BaseExpr Expression = value;

    protected override string _GetString()
    {
        return $"{Constant.GetCodeText()} {AssignmentToken.Value} {Expression.GetCodeText()}";
    }
}