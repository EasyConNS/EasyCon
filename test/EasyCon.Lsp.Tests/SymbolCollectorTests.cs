using EasyCon.Lsp;
using EasyCon.Lsp.Analysis;
using EasyCon.Script.Syntax;

namespace EasyCon.Lsp.Tests;

[TestFixture]
public class SymbolCollectorTests
{
    private static CompicationUnit ParseRoot(string source)
    {
        return SyntaxTree.Parse(source).Root;
    }

    [Test]
    public void CollectSymbols_Constant()
    {
        var root = ParseRoot("_MAX = 100");
        var symbols = SymbolCollector.CollectSymbols(root);

        Assert.That(symbols, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(symbols[0].Name, Is.EqualTo("_MAX"));
            Assert.That(symbols[0].Kind, Is.EqualTo("constant"));
        });
    }

    [Test]
    public void CollectSymbols_Variable()
    {
        var root = ParseRoot("$count = 0");
        var symbols = SymbolCollector.CollectSymbols(root);

        Assert.That(symbols, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(symbols[0].Name, Is.EqualTo("$count"));
            Assert.That(symbols[0].Kind, Is.EqualTo("variable"));
        });
    }

    [Test]
    public void CollectSymbols_Function()
    {
        var root = ParseRoot("FUNC myFunc($a, $b)\n    $c = $a + $b\nENDFUNC");
        var symbols = SymbolCollector.CollectSymbols(root);

        var func = symbols.FirstOrDefault(s => s.Kind == "function");
        Assert.That(func, Is.Not.Null);
        Assert.That(func.Name, Is.EqualTo("myFunc"));

        var parameters = symbols.Where(s => s.Kind == "parameter").ToList();
        Assert.That(parameters, Has.Count.EqualTo(2));
        Assert.That(parameters.Select(p => p.Name), Is.EquivalentTo(new[] { "$a", "$b" }));
    }

    [Test]
    public void CollectSymbols_ExternFunc()
    {
        var root = ParseRoot("EXTERN INT myExt(INT x)");
        var symbols = SymbolCollector.CollectSymbols(root);

        Assert.That(symbols, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(symbols[0].Name, Is.EqualTo("myExt"));
            Assert.That(symbols[0].Kind, Is.EqualTo("function"));
        });
    }

    [Test]
    public void CollectSymbols_IfBlock()
    {
        var root = ParseRoot("IF $x > 0\n    $y = 1\nENDIF");
        var symbols = SymbolCollector.CollectSymbols(root);

        Assert.That(symbols.Any(s => s.Name == "$y" && s.Kind == "variable"), Is.True);
    }

    [Test]
    public void CollectSymbols_ForBlock()
    {
        var root = ParseRoot("FOR $i = 0 TO 10\n    $sum = $sum + $i\nEND");
        var symbols = SymbolCollector.CollectSymbols(root);

        Assert.Multiple(() =>
        {
            Assert.That(symbols.Any(s => s.Name == "$i" && s.Kind == "variable"), Is.True);
            Assert.That(symbols.Any(s => s.Name == "$sum" && s.Kind == "variable"), Is.True);
        });
    }

    [Test]
    public void CollectSymbols_WhileBlock()
    {
        var root = ParseRoot("WHILE $x > 0\n    $x = $x - 1\nEND");
        var symbols = SymbolCollector.CollectSymbols(root);

        Assert.That(symbols.Any(s => s.Name == "$x" && s.Kind == "variable"), Is.True);
    }

    [Test]
    public void CollectSymbols_EmptySource()
    {
        var root = ParseRoot("");
        var symbols = SymbolCollector.CollectSymbols(root);

        Assert.That(symbols, Is.Empty);
    }

    [Test]
    public void CollectSymbols_MultipleStatements()
    {
        var root = ParseRoot("_A = 1\n_B = 2\n$var = _A + _B");
        var symbols = SymbolCollector.CollectSymbols(root);

        Assert.That(symbols.Count(s => s.Kind == "constant"), Is.EqualTo(2));
        Assert.That(symbols.Count(s => s.Kind == "variable"), Is.EqualTo(1));
    }
}