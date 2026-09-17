using EasyCon.Lsp;
using EasyCon.Lsp.Analysis;

namespace EasyCon.Lsp.Tests;

[TestFixture]
public class SourceHelperTests
{
    [TestCase("hello world", 0, ExpectedResult = "hello")]
    [TestCase("hello world", 4, ExpectedResult = "hello")]
    [TestCase("hello world", 5, ExpectedResult = "hello")]
    [TestCase("hello world", 6, ExpectedResult = "world")]
    [TestCase("hello world", 11, ExpectedResult = "world")]
    [TestCase("abc123 def", 3, ExpectedResult = "abc123")]
    [TestCase("foo_bar", 3, ExpectedResult = "foo_bar")]
    public string ExtractWord_Simple(string lineText, int character)
    {
        return SourceHelper.ExtractWord(lineText, character);
    }

    [Test]
    public void ExtractWord_VariablePrefix()
    {
        Assert.That(SourceHelper.ExtractWord("$count + 1", 3), Is.EqualTo("$count"));
    }

    [Test]
    public void ExtractWord_VariableAtStart()
    {
        Assert.That(SourceHelper.ExtractWord("$count", 1), Is.EqualTo("$count"));
    }

    [Test]
    public void ExtractWord_ConstantPrefix()
    {
        Assert.That(SourceHelper.ExtractWord("_MAX + 1", 3), Is.EqualTo("_MAX"));
    }

    [Test]
    public void ExtractWord_ExternPrefix()
    {
        Assert.That(SourceHelper.ExtractWord("@myFunc()", 3), Is.EqualTo("@myFunc"));
    }

    [TestCase("a", 0, ExpectedResult = "a")]
    [TestCase("a", 1, ExpectedResult = "a")]
    public string ExtractWord_EdgeCases(string lineText, int character)
    {
        return SourceHelper.ExtractWord(lineText, character);
    }

    [Test]
    public void ExtractWord_NegativePosition()
    {
        Assert.That(SourceHelper.ExtractWord("hello", -1), Is.EqualTo(""));
    }

    [Test]
    public void ExtractWord_PositionBeyondLength()
    {
        Assert.That(SourceHelper.ExtractWord("hi", 10), Is.EqualTo(""));
    }
}