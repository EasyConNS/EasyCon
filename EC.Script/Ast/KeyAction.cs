using ECScript.Syntax;

namespace ECP.Ast;

/*
internal abstract class KeyAction : Statement
{
    protected KeyAction(SyntaxNode syntax) : base(syntax)
    {
    }
}

internal sealed class ButtonAction : KeyAction
{
    public ButtonAction(LexemeValue name)
    {
        Key = name.Content.ToUpper();
        Duration = "50";
    }

    public ButtonAction(LexemeValue name, IntLiteral duration)
    {
        Key = name.Content.ToUpper();
        Duration = duration.VariableRef.VariableName.Content;
    }

    public ButtonAction(LexemeValue name, string destination)
    {
        Key = name.Content.ToUpper();
        Duration = destination.ToUpper();
    }

    public string Key { get; init; }
    public string Duration { get; init; }

    public override AstNodeType Kind => throw new NotImplementedException();

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitButtonAction(this);
    }
}

public class StickAction : ButtonAction
{
    public StickAction(LexemeValue name, LexemeValue destination) : base(name)
    {
        Destination = destination;
    }

    public StickAction(LexemeValue name, LexemeValue destination, IntLiteral duration) : base(name, duration)
    {
        Destination = destination;
    }

    public LexemeValue Destination { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitStickAction(this);
    }
}
*/