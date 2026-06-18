using EasyCon.Script;
using EasyCon.Script.Syntax;

namespace EasyCon.Tests;

/// <summary>
/// 验证 CompileResult.KeyAction 标志是否正确识别脚本中的按键动作。
///
/// 根因回归：KeyAction 曾仅扫描函数体【顶层】语句，遗漏嵌套在 if/while/for 块内的按键，
/// 导致 ScriptService 不创建 pad，运行时 GamePad?. 静默丢弃按键（“运行脚本按键不生效”）。
/// 修复后 KeyAction 由扁平化的 SSA 指令判定，覆盖任意嵌套深度。
/// </summary>
[TestFixture]
public class KeyActionFlagTests
{
    private static CompileResult Compile(string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(code));
        return compilation.Compile(null);
    }

    private static void AssertNoErrors(CompileResult result)
    {
        Assert.That(result.Diagnostics.HasErrors(), Is.False,
            () => "脚本应编译通过: " + string.Join("; ", result.Diagnostics));
    }

    [Test]
    public void KeyAction_True_WhenPressNestedInFor()
    {
        // 真实脚本常见形态：按键在 FOR 循环内（见 examples/循环搜图测试/main.txt）
        var result = Compile("FOR 3\nA\nNEXT");
        AssertNoErrors(result);
        Assert.That(result.KeyAction, Is.True,
            "嵌套在 FOR 内的按键必须被识别为 KeyAction，否则 ScriptService 不创建 pad");
    }

    [Test]
    public void KeyAction_True_WhenPressWithDurationNestedInFor()
    {
        var result = Compile("FOR 3\nA 200\nNEXT");
        AssertNoErrors(result);
        Assert.That(result.KeyAction, Is.True);
    }

    [Test]
    public void KeyAction_True_WhenPressNestedInIf()
    {
        var result = Compile("IF 1 > 0\nA\nENDIF");
        AssertNoErrors(result);
        Assert.That(result.KeyAction, Is.True);
    }

    [Test]
    public void KeyAction_True_WhenPressDeeplyNested()
    {
        var result = Compile("IF 1 > 0\nFOR 3\nA\nNEXT\nENDIF");
        AssertNoErrors(result);
        Assert.That(result.KeyAction, Is.True);
    }

    [Test]
    public void KeyAction_True_WhenPressTopLevel()
    {
        // 顶层按键是既有正确行为，作回归保护
        var result = Compile("A");
        AssertNoErrors(result);
        Assert.That(result.KeyAction, Is.True);
    }

    [Test]
    public void KeyAction_False_WhenNoKeyAction()
    {
        var result = Compile("$c = 0\nFOR 3\n$c = $c + 1\nNEXT");
        AssertNoErrors(result);
        Assert.That(result.KeyAction, Is.False);
    }
}