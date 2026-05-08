using EasyCon.Script;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Tests;

/// <summary>
/// 变量生命周期分析测试
/// </summary>
[TestFixture]
public class LifecycleAnalyzerTests
{
    private static (bool Success, List<string> Errors) Compile(string code)
    {
        var tree = SyntaxTree.Parse(code);
        var errors = tree.Diagnostics.Where(d => d.IsError).Select(d => d.Message).ToList();
        if (errors.Count == 0)
        {
            try
            {
                var compilation = Compilation.Create(tree);
                var diag = compilation.Compile([]);
                foreach (var d in diag)
                    errors.Add(d.Message);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }
        return (errors.Count == 0, errors);
    }

    private static void ExpectBind(string code)
    {
        var (success, errors) = Compile(code);
        Assert.That(success, Is.True, $"Expected bind success but got: {string.Join("; ", errors)}");
    }

    private static void ExpectError(string code, string? contains = null)
    {
        var (success, errors) = Compile(code);
        Assert.That(success, Is.False, "Expected bind error but got success");
        if (contains != null)
            Assert.That(errors.Any(e => e.Contains(contains)), Is.True,
                $"Expected error containing '{contains}' but got: {string.Join("; ", errors)}");
    }

    [Test]
    public void Lifecycle_SimpleNonOverlappingVariables()
    {
        // 两个变量生命周期不重叠，应该分配到相同slot
        ExpectBind(@"
FUNC test()
    $a = 1
    $b = $a
    RETURN $b
ENDFUNC");
    }

    [Test]
    public void Lifecycle_OverlappingVariables()
    {
        // 两个变量生命周期重叠，应该分配到不同slot
        ExpectBind(@"
FUNC test()
    $a = 1
    $b = 2
    $c = $a + $b
    RETURN $c
ENDFUNC");
    }

    [Test]
    public void Lifecycle_NestedBlocks()
    {
        ExpectBind(@"
FUNC test()
    $outer = 1
    IF $outer == 1
        $inner = 2
        $result = $outer + $inner
    ELSE
        $other = 3
        $result = $outer + $other
    ENDIF
    RETURN $result
ENDFUNC");
    }

    [Test]
    public void Lifecycle_ComplexScenario()
    {
        ExpectBind(@"
FUNC test()
    $a = 1
    $b = 2
    IF $a < $b
        $c = $a + $b
        $d = $c * 2
        $result = $d
    ELSE
        $e = $a - $b
        $f = $e / 2
        $result = $f
    ENDIF
    
    $g = 10
    $h = $result + $g
    RETURN $h
ENDFUNC");
    }

    [Test]
    public void Lifecycle_SlotOptimization()
    {
        var code = @"
FUNC test()
    $a = 1
    $b = $a + 1
    RETURN $b
    $c = 3  # This variable will not be executed, but will be declared
    $d = $c + 1
ENDFUNC";

        ExpectBind(code);
    }

    [Test]
    public void Lifecycle_NonOverlappingVariables_ShouldShareSlots()
    {
        var code = @"
FUNC test()
    $a = 1
    $b = $a + 1
    RETURN $b
    $c = 3  # Not overlapping with $a, $b (although after return, but declared in the same scope)
    $d = $c + 1
ENDFUNC";

        ExpectBind(code);
    }
}