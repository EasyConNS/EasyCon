using EasyCon.Core.Capabilities;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Tests.Support;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// INT 转换语义（PC 端）：数字字符串解析（常量折叠路径 + 运行期路径）、
/// 小数截断、解析失败返回 0。MCU 端字符串转换静默返回 0（不在本测试范围）。
/// </summary>
[TestFixture]
public class IntConvertTests
{
    static string Run(CompileResult result, string[]? args = null)
    {
        var io = new RecordingIo();
        EcxVm.Run(result.Image!, EcsTestHost.Capabilities(io), new CancellationTokenSource().Token,
            args ?? [], result.NativeSymbols);
        return string.Join("|", io.Lines);
    }

    static CompileResult Compile(string script)
    {
        var result = Compilation.CompileSource(script,
            new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty, "编译失败");
        return result;
    }

    [Test]
    public void Int_OfNumericStringConstant_IsFolded()
    {
        Assert.That(Run(Compile("$a = INT(\"123\")\nPRINT $a")), Is.EqualTo("123"));
        Assert.That(Run(Compile("$b = INT(\"  -45 \")\nPRINT $b")), Is.EqualTo("-45"));
        Assert.That(Run(Compile("$c = INT(\"12a\")\nPRINT $c")), Is.EqualTo("0"));
        Assert.That(Run(Compile("$d = INT(\"\")\nPRINT $d")), Is.EqualTo("0"));
    }

    [Test]
    public void Int_OfNumericStringRuntime_Parses()
    {
        var script = "$s = ARG(0)\n$a = INT($s)\nPRINT $a";
        Assert.That(Run(Compile(script), ["123"]), Is.EqualTo("123"));
        Assert.That(Run(Compile(script), ["  -45 "]), Is.EqualTo("-45"));
        Assert.That(Run(Compile(script), ["abc"]), Is.EqualTo("0"));
    }

    [Test]
    public void Int_OfDouble_Truncates()
    {
        Assert.That(Run(Compile("$e = INT(3.9)\nPRINT $e")), Is.EqualTo("3"));
    }
}