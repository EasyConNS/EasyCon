using VBF.Compilers.Scanners;

namespace ECP.Ast;

public abstract class KeyAction : Statement
{
}

public class ButtonAction : KeyAction
{
    public ButtonAction(LexemeValue name)
    {
        Key = name.Content.ToUpper();
        Duration = "50";
    }

    public ButtonAction(LexemeValue name, Number duration)
    {
        Key = name.Content.ToUpper();
        Duration = duration != null ? duration.VariableRef.Content : "50";
    }

    public ButtonAction(LexemeValue name, string destination)
    {
        Key = name.Content.ToUpper();
        Duration = destination ?? "50";
    }

    public string Key { get; init; }
    public string Duration { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitButtonAction(this);
    }
}

public class StickAction : ButtonAction
{
    public StickAction(LexemeValue name, string destination) : base(name)
    {
        Destination = destination;
    }

    public string Destination { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitStickAction(this);
    }
}