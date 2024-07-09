using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class Number : Expression
{
    public Number(LexemeValue name)
    {
        VariableRef = new VariableRef(name);
    }

    public VariableRef VariableRef { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitNumber(this);
    }

    public override string ToString()
    {
        return VariableRef.VariableName.Content;
    }
}

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
}
