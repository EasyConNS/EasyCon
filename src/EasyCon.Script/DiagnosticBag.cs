using EasyCon.Script.Syntax;
using System.Collections;
using EasyCon.Script.Text;

namespace EasyCon.Script;

internal sealed class DiagnosticBag : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        _diagnostics.AddRange(diagnostics);
    }

    private void ReportError(TextLocation location, string message)
    {
        var diagnostic = Diagnostic.Error(location, message);
        _diagnostics.Add(diagnostic);
    }

    public void ReportInvalidNumber(TextLocation location, string text)
    {
        var message = $"数字格式不正确: {text}";
        ReportError(location, message);
    }

    public void ReportBadCharacter(TextLocation location, char character)
    {
        var message = $"不识别的字符: '{character}'";
        ReportError(location, message);
    }

    public void ReportUnterminatedString(TextLocation location)
    {
        var message = "字符串没有结束引号";
        ReportError(location, message);
    }

    public void ReportUnexpectedToken(TextLocation location, TokenType actualKind, TokenType expectedKind)
    {
        var message = $"错误的 <{actualKind}>, 期望 <{expectedKind}>";
        ReportError(location, message);
    }

    public void ReportInvalidExpressionStatement(TextLocation location, TokenType actualKind)
    {
        var message = $"无效的表达式语句 <{actualKind}>";
        ReportError(location, message);
    }

    public void ReportInvalidKeyActionStatement(TextLocation location, TokenType actualKind)
    {
        var message = $"按键语法不正确 <{actualKind}>";
        ReportError(location, message);
    }
}
