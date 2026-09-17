using EasyCon.Lsp;
using EasyCon.Lsp.Analysis;
using EasyCon.Script.Syntax;
using EmmyLua.LanguageServer.Framework.Protocol.Model;

namespace EasyCon.Lsp.Tests;

[TestFixture]
public class HoverProviderTests
{
    private static CompicationUnit ParseRoot(string source)
    {
        return SyntaxTree.Parse(source).Root;
    }

    [Test]
    public void GetHover_BuiltinFunction()
    {
        var root = ParseRoot("WAIT 100");
        var hover = HoverProvider.GetHover(root, "WAIT 100", new Position(0, 2));

        Assert.That(hover, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(hover!.Contents.Value, Does.Contain("WAIT"));
            Assert.That(hover.Contents.Value, Does.Contain("等待"));
        });
    }

    [Test]
    public void GetHover_Keyword()
    {
        var root = ParseRoot("IF $x > 0\nENDIF");
        var hover = HoverProvider.GetHover(root, "IF $x > 0", new Position(0, 1));

        Assert.That(hover, Is.Not.Null);
        Assert.That(hover!.Contents.Value, Does.Contain("关键字"));
    }

    [Test]
    public void GetHover_Constant()
    {
        var root = ParseRoot("_MAX = 100\n$var = _MAX");
        var hover = HoverProvider.GetHover(root, "$var = _MAX", new Position(0, 8));

        Assert.That(hover, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(hover!.Contents.Value, Does.Contain("constant"));
            Assert.That(hover.Contents.Value, Does.Contain("_MAX"));
        });
    }

    [Test]
    public void GetHover_Function()
    {
        var root = ParseRoot("FUNC myFunc()\nENDFUNC\nmyFunc()");
        var hover = HoverProvider.GetHover(root, "myFunc()", new Position(0, 3));

        Assert.That(hover, Is.Not.Null);
        Assert.That(hover!.Contents.Value, Does.Contain("function"));
    }

    [Test]
    public void GetHover_Variable()
    {
        var root = ParseRoot("$count = 0\n$count = $count + 1");
        var hover = HoverProvider.GetHover(root, "$count = $count + 1", new Position(0, 1));

        Assert.That(hover, Is.Not.Null);
        Assert.That(hover!.Contents.Value, Does.Contain("variable"));
    }

    [Test]
    public void GetHover_UnknownWord_ReturnsNull()
    {
        var root = ParseRoot("$x = 1");
        var hover = HoverProvider.GetHover(root, "$x = 1", new Position(0, 0));

        // "$x" is a variable, it should return hover info
        // But let's test with a word that doesn't exist
        var hover2 = HoverProvider.GetHover(root, "unknownStuff", new Position(0, 3));
        Assert.That(hover2, Is.Null);
    }

    [Test]
    public void GetHover_NullLineText_ReturnsNull()
    {
        var root = ParseRoot("$x = 1");
        var hover = HoverProvider.GetHover(root, null, new Position(0, 0));
        Assert.That(hover, Is.Null);
    }

    [Test]
    public void GetHover_NullRoot_ReturnsNull()
    {
        var hover = HoverProvider.GetHover(null, "WAIT", new Position(0, 0));
        Assert.That(hover, Is.Null);
    }

    [Test]
    public void GetHover_SpacesOnly_ReturnsNull()
    {
        var root = ParseRoot("$x = 1");
        var hover = HoverProvider.GetHover(root, "   ", new Position(0, 1));
        Assert.That(hover, Is.Null);
    }
}