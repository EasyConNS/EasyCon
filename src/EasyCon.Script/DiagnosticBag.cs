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

    public void ReportUnexpectedToken(TextLocation location, Token actual, TokenType expectedKind)
    {
        var message = $"需要<{expectedKind}>但是意外的<{actual.Value}>";
        ReportError(location, message);
        throw new Exception($"需要<{expectedKind}>但是意外的<{actual.Value}>");
    }

    public void ReportInvalidImport(TextLocation location, Token modToken)
    {
        var message = $"导入库不存在 <{modToken.Value}>";
        ReportError(location, message);
    }

    public void ReportInvalidExpressionStatement(TextLocation location)
    {
        var message = "无效的表达式语句";
        ReportError(location, message);
    }

    public void ReportInvalidKeyActionStatement(TextLocation location, Token actual)
    {
        var message = $"按键语法不正确 <{actual}>";
        ReportError(location, message);
    }
}
