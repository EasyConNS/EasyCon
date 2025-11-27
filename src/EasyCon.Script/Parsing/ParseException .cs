namespace EasyScript.Parsing;

public class ParseException : Exception
{
    public int Index;

    public ParseException(string message, int index = -1)
        : base(message)
    {
        Index = index;
    }
}
