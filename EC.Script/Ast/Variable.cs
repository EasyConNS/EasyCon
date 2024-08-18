using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Variable : Expression
{
    public Variable(LexemeValue name)
    {
        VariableRef = new VariableRef(name);
    }

    public VariableRef VariableRef { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitVariable(this);
    }
}

public class VariableRef
{
    public VariableRef(LexemeValue name)
    {
        VariableName = name;
    }

    public LexemeValue VariableName { get; init; }
    public VariableInfo VariableInfo { get; set; }
}
