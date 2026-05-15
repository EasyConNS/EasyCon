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

    private static string Format(string code)
    {
        var tree = SyntaxTree.Parse(code);
        Assert.That(tree.Diagnostics.Where(d => d.IsError), Is.Empty);

        return Normalize(Compilation.Create(tree).FormatCode());
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Trim();
}
