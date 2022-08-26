using VBF.Compilers.Scanners;

namespace ECP.Ast;

public abstract class KeyAction : Statement
{
}

public class ButtonAction : KeyAction
{
    public ButtonAction(LexemeValue name)
    {
        Key = name;
        Duration = 50;
    }

    public LexemeValue Key { get; init; }
    public int Duration { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitButtonAction(this);
    }

    public override void Show()
    {
        Console.WriteLine($"press({Key}, {Duration})");
    }
}