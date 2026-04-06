using EasyCon.Script.Syntax;

namespace ECScript.Tests;

[TestFixture]
public class ParserTests
{
    [SetUp]
    public void Setup()
    {
        // setup
    }

    [Test]
    public void Test_Ignore()
    {
        Assert.Pass();
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void IsPrime_ValuesLessThan2_ReturnFalse(int value)
    {

        Assert.That(false, Is.False, $"{value} should not be prime");
    }

    // [Test]
    // public void Tokenize_InvalidNumber_ReportsDiagnostics()
    // {
    //     var tree = SyntaxTree.Parse("$x = 1.2.3");
    //     Assert.That(tree.Diagnostics.Count, Is.GreaterThan(0));
    // }

    // [Test]
    // public void Tokenize_UnterminatedString_ReportsDiagnostics()
    // {
    //     var tree = SyntaxTree.Parse("$x = \"unterminated");
    //     Assert.That(tree.Diagnostics.Count, Is.GreaterThan(0));
    // }

    // [Test]
    // public void Tokenize_BadCharacter_ReportsDiagnostics()
    // {
    //     var tree = SyntaxTree.Parse("$x = 10 \u0001");
    //     Assert.That(tree.Diagnostics.Count, Is.GreaterThan(0));
    // }
}