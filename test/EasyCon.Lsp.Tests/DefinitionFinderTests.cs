using EasyCon.Lsp;
using EasyCon.Lsp.Analysis;
using EasyCon.Script.Syntax;
using EmmyLua.LanguageServer.Framework.Protocol.Model;

namespace EasyCon.Lsp.Tests;

[TestFixture]
public class DefinitionFinderTests
{
    private static CompicationUnit ParseRoot(string source)
    {
        return SyntaxTree.Parse(source).Root;
    }

    [Test]
    public void FindDefinition_Variable()
    {
        var root = ParseRoot("$count = 0\nIF $x > 0\n    $count = $count + 1\nENDIF");
        var def = DefinitionFinder.FindDefinition(root, "$count = $count + 1", new Position(0, 12));

        Assert.That(def, Is.Not.Null);
        // Should point to line 0 where $count is first assigned
        Assert.That(def!.Value.Start.Line, Is.EqualTo(0));
    }

    [Test]
    public void FindDefinition_Constant()
    {
        var root = ParseRoot("_MAX = 100\n$var = _MAX");
        var def = DefinitionFinder.FindDefinition(root, "$var = _MAX", new Position(0, 7));

        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Value.Start.Line, Is.EqualTo(0));
    }

    [Test]
    public void FindDefinition_Function()
    {
        var root = ParseRoot("FUNC myFunc($a)\n    RETURN $a\nENDFUNC\n$x = myFunc(1)");
        var def = DefinitionFinder.FindDefinition(root, "$x = myFunc(1)", new Position(0, 5));

        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Value.Start.Line, Is.EqualTo(0));
    }

    [Test]
    public void FindDefinition_ExternFunction()
    {
        var root = ParseRoot("EXTERN INT myExt(INT x)\n$r = myExt(5)");
        var def = DefinitionFinder.FindDefinition(root, "$r = myExt(5)", new Position(0, 5));

        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Value.Start.Line, Is.EqualTo(0));
    }

    [Test]
    public void FindDefinition_Function_CaseInsensitive()
    {
        var root = ParseRoot("FUNC MyFunc()\nENDFUNC\nMyFunc()");
        var def = DefinitionFinder.FindDefinition(root, "MyFunc()", new Position(0, 3));

        Assert.That(def, Is.Not.Null);
    }

    [Test]
    public void FindDefinition_ExternPrefix_ReturnsNull()
    {
        var root = ParseRoot("@something()");
        var def = DefinitionFinder.FindDefinition(root, "@something()", new Position(0, 3));

        Assert.That(def, Is.Null);
    }

    [Test]
    public void FindDefinition_ParameterInFunction()
    {
        var root = ParseRoot("FUNC myFunc($a)\n    $x = $a\nENDFUNC");
        var def = DefinitionFinder.FindDefinition(root, "$x = $a", new Position(0, 6));

        Assert.That(def, Is.Not.Null);
    }

    [Test]
    public void FindDefinition_ForLoopIterator()
    {
        var root = ParseRoot("FOR $i = 0 TO 10\n    $x = $i\nEND");
        var def = DefinitionFinder.FindDefinition(root, "$x = $i", new Position(0, 6));

        Assert.That(def, Is.Not.Null);
    }

    [Test]
    public void FindDefinition_NullRoot_ReturnsNull()
    {
        var def = DefinitionFinder.FindDefinition(null, "$x", new Position(0, 1));
        Assert.That(def, Is.Null);
    }

    [Test]
    public void FindDefinition_NullLineText_ReturnsNull()
    {
        var root = ParseRoot("$x = 1");
        var def = DefinitionFinder.FindDefinition(root, null, new Position(0, 1));
        Assert.That(def, Is.Null);
    }

    [Test]
    public void FindDefinition_UnknownVariable_ReturnsNull()
    {
        var root = ParseRoot("$x = 1");
        var def = DefinitionFinder.FindDefinition(root, "$unknown", new Position(0, 3));
        Assert.That(def, Is.Null);
    }

    [Test]
    public void FindDefinition_UnknownFunction_ReturnsNull()
    {
        var root = ParseRoot("FUNC foo()\nENDFUNC");
        var def = DefinitionFinder.FindDefinition(root, "bar()", new Position(0, 2));
        Assert.That(def, Is.Null);
    }
}