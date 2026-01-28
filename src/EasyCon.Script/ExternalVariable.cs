namespace EasyCon.Script;

public class ExternalVariable(string name, ExternalVariable.Getter get)
{
    public delegate int Getter();
    public delegate void Setter(int value);

    // name, or label, of the variable
    public readonly string Name = name;
    // function for reading
    public readonly Getter Get = get;
}
