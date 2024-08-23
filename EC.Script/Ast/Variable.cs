using ECScript.Syntax;

namespace ECP.Ast;

internal sealed class Variable : Expression
{
    public Variable(SyntaxNode node, VariableRef variable) : base(node)
    {
        VariableRef = variable;
    }

    public VariableRef VariableRef { get; init; }

    public override AstNodeType Kind => AstNodeType.VariableExpression;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException();
    }
}

public class VariableRef
{
    public VariableRef(string name)
    {
        VariableName = name;
    }

    public string VariableName { get; init; }
    public bool IsReadOnly { get; init; }
    // public VariableInfo VariableInfo { get; set; }
}
