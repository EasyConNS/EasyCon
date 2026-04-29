using EasyCon.Lsp;
using EasyCon.Script.Text;
using EmmyLua.LanguageServer.Framework.Protocol.Model;

namespace EasyCon.Lsp.Tests;

[TestFixture]
public class DocumentManagerTests
{
    private static readonly DocumentUri TestUri = new(new Uri("file:///test.ecs"));

    [Test]
    public void OpenDocument_ParsesSource()
    {
        var dm = new DocumentManager();
        dm.OpenDocument(TestUri, "$x = 1");

        var tree = dm.GetSyntaxTree(TestUri);
        Assert.That(tree, Is.Not.Null);
        Assert.That(tree!.Diagnostics.IsEmpty, Is.True);
    }

    [Test]
    public void OpenDocument_StoresSource()
    {
        var dm = new DocumentManager();
        dm.OpenDocument(TestUri, "$x = 1");

        Assert.That(dm.GetSource(TestUri), Is.EqualTo("$x = 1"));
    }

    [Test]
    public void OpenDocument_ReturnsRoot()
    {
        var dm = new DocumentManager();
        dm.OpenDocument(TestUri, "_MAX = 100");

        var root = dm.GetRoot(TestUri);
        Assert.That(root, Is.Not.Null);
    }

    [Test]
    public void UpdateDocument_ReplacesSource()
    {
        var dm = new DocumentManager();
        dm.OpenDocument(TestUri, "$x = 1");
        dm.UpdateDocument(TestUri, "$x = 2");

        Assert.That(dm.GetSource(TestUri), Is.EqualTo("$x = 2"));
    }

    [Test]
    public void UpdateDocument_Reparses()
    {
        var dm = new DocumentManager();
        dm.OpenDocument(TestUri, "$x = 1");
        dm.UpdateDocument(TestUri, "IF $x\nENDIF");

        var root = dm.GetRoot(TestUri);
        Assert.That(root, Is.Not.Null);
    }

    [Test]
    public void CloseDocument_RemovesDocument()
    {
        var dm = new DocumentManager();
        dm.OpenDocument(TestUri, "$x = 1");
        dm.CloseDocument(TestUri);

        Assert.Multiple(() =>
        {
            Assert.That(dm.GetSource(TestUri), Is.Null);
            Assert.That(dm.GetSyntaxTree(TestUri), Is.Null);
            Assert.That(dm.GetRoot(TestUri), Is.Null);
        });
    }

    [Test]
    public void GetSource_Unopened_ReturnsNull()
    {
        var dm = new DocumentManager();
        Assert.That(dm.GetSource(TestUri), Is.Null);
    }

    [Test]
    public void GetLineText_ReturnsCorrectLine()
    {
        var dm = new DocumentManager();
        dm.OpenDocument(TestUri, "line1\nline2\nline3");

        Assert.Multiple(() =>
        {
            Assert.That(dm.GetLineText(TestUri, 0), Is.EqualTo("line1"));
            Assert.That(dm.GetLineText(TestUri, 1), Is.EqualTo("line2"));
            Assert.That(dm.GetLineText(TestUri, 2), Is.EqualTo("line3"));
        });
    }

    [Test]
    public void GetLineText_InvalidLine_ReturnsNull()
    {
        var dm = new DocumentManager();
        dm.OpenDocument(TestUri, "only one line");

        Assert.Multiple(() =>
        {
            Assert.That(dm.GetLineText(TestUri, -1), Is.Null);
            Assert.That(dm.GetLineText(TestUri, 5), Is.Null);
        });
    }

    [Test]
    public void GetSourceText_ReturnsText()
    {
        var dm = new DocumentManager();
        dm.OpenDocument(TestUri, "$x = 1");

        var sourceText = dm.GetSourceText(TestUri);
        Assert.That(sourceText, Is.Not.Null);
    }

    [Test]
    public void OpenDocument_SyntaxError_DiagnosticsAvailable()
    {
        var dm = new DocumentManager();
        dm.OpenDocument(TestUri, "IF without endif garbage");

        var tree = dm.GetSyntaxTree(TestUri);
        Assert.That(tree, Is.Not.Null);
        // Syntax errors are expected here
        Assert.That(tree!.Diagnostics.IsEmpty, Is.False);
    }

    [Test]
    public void MultipleDocuments_Independent()
    {
        var dm = new DocumentManager();
        var uri1 = new DocumentUri(new Uri("file:///a.ecs"));
        var uri2 = new DocumentUri(new Uri("file:///b.ecs"));

        dm.OpenDocument(uri1, "$a = 1");
        dm.OpenDocument(uri2, "$b = 2");

        Assert.Multiple(() =>
        {
            Assert.That(dm.GetSource(uri1), Is.EqualTo("$a = 1"));
            Assert.That(dm.GetSource(uri2), Is.EqualTo("$b = 2"));
        });

        dm.CloseDocument(uri1);
        Assert.Multiple(() =>
        {
            Assert.That(dm.GetSource(uri1), Is.Null);
            Assert.That(dm.GetSource(uri2), Is.EqualTo("$b = 2"));
        });
    }
}