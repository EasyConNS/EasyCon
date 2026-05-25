using EasyCon.Script;
using EasyCon.Script.Syntax;

namespace EasyCon.Tests;

[TestFixture]
public class FormatPrinterTests
{
    [Test]
    public void Format_IfElifElse_IndentsBranchBodies()
    {
        var code = """
IF $x == 1
A
ELIF $x == 2
B
ELSE
PRINT OK
ENDIF
""";

        var expected = """
IF $x == 1
    A
ELIF $x == 2
    B
ELSE
    PRINT OK
ENDIF
""";

        Assert.That(Format(code), Is.EqualTo(Normalize(expected)));
    }

    [Test]
    public void Format_VariableFor_IndentsBranchBodies()
    {
        var code = """
IF @你好>95
ENDIF
for $i = 0 to 5
next
""";

        var expected = """
IF @你好 > 95
ENDIF
FOR $i = 0 TO 5
NEXT
""";

        Assert.That(Format(code), Is.EqualTo(Normalize(expected)));
    }

    [Test]
    public void Format_StringLiteral_RoundTrips()
    {
        var code = "$s = \"hello\"";
        Assert.That(Format(code), Is.EqualTo("$s = \"hello\""));
    }

    [Test]
    public void Format_StringWithEscape_RoundTrips()
    {
        var code = "$s = \"hello\\nworld\"";
        Assert.That(Format(code), Is.EqualTo("$s = \"hello\\nworld\""));
    }

    [Test]
    public void Format_StringWithEscapedQuote_RoundTrips()
    {
        var code = "$s = \"say \\\"hi\\\" here\"";
        Assert.That(Format(code), Is.EqualTo("$s = \"say \\\"hi\\\" here\""));
    }

    private static string Format(string code)
    {
        var tree = SyntaxTree.Parse(code);
        Assert.That(tree.Diagnostics.Where(d => d.IsError), Is.Empty);

        return Normalize(Compilation.Create(tree).FormatCode());
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Trim();
}