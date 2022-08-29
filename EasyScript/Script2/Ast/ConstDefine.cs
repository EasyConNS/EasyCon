namespace ECP.Ast;

public class ConstDefine : Expression
{
    public ConstDefine(string name)
    {
        ConstName = name;
    }

    public string ConstName { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitConstDefine(this);
    }

    public override void Show()
    {
        Console.WriteLine(this);
    }
}