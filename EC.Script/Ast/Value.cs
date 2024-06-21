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

/*
private VariableInfo ResolveVariable(LexemeValue identifier)
{
    //step1, check local parameter & variable definitions
    if (m_currentMethodParameters.Contains(identifier.Content))
    {
        return m_currentMethodParameters[identifier.Content];
    }
    else if (m_currentMethodVariables.Contains(identifier.Content))
    {
        return m_currentMethodVariables[identifier.Content];
    }
    //step2, if not static method, check fields
    if (!m_currentMethod.IsStatic)
    {
        return ResolveField(m_currentType, identifier);
    }

    AddError(c_SE_VariableDeclMissing, identifier.Span, identifier.Content);
    return null;
}
*/