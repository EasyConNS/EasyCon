using EC.Script.Text;
using EC.Script.Syntax;
using System.Collections;

namespace EC.Script;

internal sealed class DiagnosticBag : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        _diagnostics.AddRange(diagnostics);
    }

    private void Report(TextLocation location, string message)
    {
        var diagnostic = Diagnostic.Error(location, message);
        _diagnostics.Add(diagnostic);
    }

    public void ReportInvalidNumber(TextLocation location, string text)
    {
        var message = $"数字格式不正确: {text}";
        Report(location, message);
    }

    public void ReportBadCharacter(TextLocation location, char character)
    {
        var message = $"Bad character input: '{character}'.";
        Report(location, message);
    }

    public void ReportUnterminatedString(TextLocation location)
    {
        var message = "字符串没有结束引号";
        Report(location, message);
    }

    public void ReportUnexpectedToken(TextLocation location, TokenType actualKind, TokenType expectedKind)
    {
        var message = $"错误的 <{actualKind}>, 期望 <{expectedKind}>.";
        Report(location, message);
    }

    public void ReportInvalidExpressionStatement(TextLocation location, TokenType actualKind)
    {
        var message = $"Only assignment and call expressions can be used as a statement.";
        Report(location, message);
    }
}
