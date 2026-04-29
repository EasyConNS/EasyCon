using EasyCon.Lsp;
using EasyCon.Lsp.Analysis;
using EasyCon.Script.Syntax;
using EmmyLua.LanguageServer.Framework.Protocol.Message.Completion;
using EmmyLua.LanguageServer.Framework.Protocol.Model;

namespace EasyCon.Lsp.Tests;

[TestFixture]
public class CompletionProviderTests
{
    private static CompicationUnit ParseRoot(string source)
    {
        return SyntaxTree.Parse(source).Root;
    }

    [Test]
    public void GetCompletions_IncludesKeywords()
    {
        var completions = CompletionProvider.GetCompletions(null);

        var labels = completions.Items.Select(i => i.Label).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(labels, Does.Contain("IF"));
            Assert.That(labels, Does.Contain("FUNC"));
            Assert.That(labels, Does.Contain("WHILE"));
            Assert.That(labels, Does.Contain("FOR"));
            Assert.That(labels, Does.Contain("RETURN"));
        });
    }

    [Test]
    public void GetCompletions_IncludesBuiltinFunctions()
    {
        var completions = CompletionProvider.GetCompletions(null);

        var labels = completions.Items.Select(i => i.Label).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(labels, Does.Contain("WAIT"));
            Assert.That(labels, Does.Contain("PRINT"));
            Assert.That(labels, Does.Contain("ALERT"));
            Assert.That(labels, Does.Contain("RAND"));
            Assert.That(labels, Does.Contain("TIME"));
        });

        var waitItem = completions.Items.First(i => i.Label == "WAIT");
        Assert.Multiple(() =>
        {
            Assert.That(waitItem.Kind, Is.EqualTo(CompletionItemKind.Function));
            Assert.That(waitItem.Detail, Is.Not.Null);
        });
    }

    [Test]
    public void GetCompletions_IncludesFfiTypes()
    {
        var completions = CompletionProvider.GetCompletions(null);

        var labels = completions.Items.Select(i => i.Label).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(labels, Does.Contain("INT"));
            Assert.That(labels, Does.Contain("BOOL"));
            Assert.That(labels, Does.Contain("STRING"));
            Assert.That(labels, Does.Contain("VOID"));
        });
    }

    [Test]
    public void GetCompletions_IncludesGamepadKeys()
    {
        var completions = CompletionProvider.GetCompletions(null);

        var labels = completions.Items.Select(i => i.Label).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(labels, Does.Contain("A"));
            Assert.That(labels, Does.Contain("B"));
            Assert.That(labels, Does.Contain("X"));
            Assert.That(labels, Does.Contain("Y"));
            Assert.That(labels, Does.Contain("ZL"));
            Assert.That(labels, Does.Contain("ZR"));
            Assert.That(labels, Does.Contain("PLUS"));
            Assert.That(labels, Does.Contain("MINUS"));
        });
    }

    [Test]
    public void GetCompletions_IncludesStickKeys()
    {
        var completions = CompletionProvider.GetCompletions(null);

        var labels = completions.Items.Select(i => i.Label).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(labels, Does.Contain("LS"));
            Assert.That(labels, Does.Contain("RS"));
        });
    }

    [Test]
    public void GetCompletions_IncludesDirections()
    {
        var completions = CompletionProvider.GetCompletions(null);

        var labels = completions.Items.Select(i => i.Label).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(labels, Does.Contain("UP"));
            Assert.That(labels, Does.Contain("DOWN"));
            Assert.That(labels, Does.Contain("LEFT"));
            Assert.That(labels, Does.Contain("RIGHT"));
        });
    }

    [Test]
    public void GetCompletions_IncludesAstSymbols()
    {
        var root = ParseRoot("_MAX = 100\nFUNC myFunc($a)\nENDFUNC\n$var = 1");
        var completions = CompletionProvider.GetCompletions(root);

        var labels = completions.Items.Select(i => i.Label).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(labels, Does.Contain("_MAX"));
            Assert.That(labels, Does.Contain("myFunc"));
            Assert.That(labels, Does.Contain("$a"));
            Assert.That(labels, Does.Contain("$var"));
        });
    }

    [Test]
    public void GetCompletions_NullRoot_OnlyStaticItems()
    {
        var completions = CompletionProvider.GetCompletions(null);

        var expectedCount = Constants.Keywords.Length
                          + Constants.BuiltinFunctions.Length
                          + Constants.FfiTypes.Length
                          + Constants.GamepadKeys.Length
                          + Constants.StickKeys.Length
                          + Constants.Directions.Length
                          + Constants.KeyMods.Length;
        Assert.That(completions.Items, Has.Count.EqualTo(expectedCount));
    }

    [Test]
    public void GetCompletions_IsNotIncomplete()
    {
        var completions = CompletionProvider.GetCompletions(null);
        Assert.That(completions.IsIncomplete, Is.False);
    }
}