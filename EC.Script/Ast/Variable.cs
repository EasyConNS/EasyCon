using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Variable : Expression
{
    public Variable(LexemeValue name)
    {
        VariableRef = new VariableRef(name);
    }

    public Variable(LexemeValue name, Expression index)
    {
        VariableRef = new VariableRef(name);
        ArrIndex = index;
    }

    public VariableRef VariableRef { get; init; }

    public Expression ArrIndex { get; init; }

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
    // public VariableInfo VariableInfo { get; set; }
}
