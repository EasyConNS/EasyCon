using EC.Script.Text;

namespace EC.Script;

public sealed class Diagnostic
{
    private Diagnostic(bool isError, SourceSpan location, string message)
    {
        Location = location;
        Message = message;
    }

    public SourceSpan Location { get; init; }
    public string Message { get; init; }

    public override string ToString() => Message;
}
