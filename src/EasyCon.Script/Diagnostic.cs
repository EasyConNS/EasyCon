using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Script;

public sealed class Diagnostic
{
    private Diagnostic(bool isError, TextLocation location, string message)
    {
        IsError = isError;
        Location = location;
        Message = message;
        IsWarning = !IsError;
    }
    public bool IsError { get; }
    public bool IsWarning { get; }
    public readonly TextLocation Location;
    public readonly string Message;

    public override string ToString() => Message;

    public static Diagnostic Error(TextLocation location, string message)
    {
        return new Diagnostic(isError: true, location, message);
    }
    public static Diagnostic Warning(TextLocation location, string message)
    {
        return new Diagnostic(isError: false, location, message);
    }
}

public static class DiagnosticExtensions
{
    public static bool HasErrors(this ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.IsError);
    }

    public static bool HasErrors(this IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.IsError);
    }
}