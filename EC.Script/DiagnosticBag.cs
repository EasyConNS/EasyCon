using EC.Script.Text;
using System.Collections;

namespace EC.Script;

internal sealed class DiagnosticBag : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Report(SourceSpan location, string message)
    {
        throw new NotImplementedException();
    }

    public void ReportInvalidNumber(SourceSpan location, string text)
    {
        var message = $"{text}";
        Report(location, message);
    }
}
