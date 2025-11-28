using EC.Script.Text;

namespace EC.Script;

public sealed class Diagnostic
{
    private Diagnostic(bool isError, TextLocation location, string message)
    {
        Location = location;
        Message = message;
    }

    public TextLocation Location { get; init; }
    public string Message { get; init; }

    public override string ToString() => Message;

    public static Diagnostic Error(TextLocation location, string message)
    {
        return new Diagnostic(isError: true, location, message);
    }
}
