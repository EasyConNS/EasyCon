namespace EasyScript;

public class ExternalVariable
{
    public delegate int Getter();
    public delegate void Setter(int value);

    // name, or label, of the variable
    public readonly string Name;
    // function for reading
    public readonly Getter Get;
    // function for writing
    public readonly Setter Set;

    public ExternalVariable(string name, Getter get, Setter? set = null)
    {
        if (name == null || get == null)
            throw new ArgumentNullException();
        Name = name;
        Get = get;
        Set = set ?? _defaultSetter;
    }

    static void _defaultSetter(int value)
    { }
}
