using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 行号表（JVM LineNumberTable 同型稀疏 [pc, line] 交错表）：
/// 构建期语句行 → SsaValue.Line → 编码期稀疏记录 → 运行错误经 LineAt(pc) 映射回源码行。
/// 覆盖：运行时错误行映射（顶层/函数内）、表稀疏与单调不变量、ECM 序列化往返、EcxVm.Address 语义。
/// </summary>
[TestFixture]
public class LineTableTests
{
    static CompileResult Compile(string source)
    {
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
            "编译诊断：" + string.Join("\n", result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null);
        return result;
    }

    static (int Code, int ErrorFunc, int ErrorPc) RunToFault(EcxImage image)
    {
        var host = EcsTestHost.CreateRecording();
        var code = EcxInterpreter.Run(image, host, out var errorFunc, out var errorPc);
        Assert.That(code, Is.Not.EqualTo(EcxInterpreter.OK), "脚本应触发运行时错误");
        return (code, errorFunc, errorPc);
    }

    [Test]
    public void RuntimeFault_AtTopLevel_MapsToSourceLine()
    {
        // 第 3 行除零（RAND 保持对编译器不透明；PRINT 消费防止 DCE 删除纯除法）
        var result = Compile("""
            $x = 0
            $d = RAND(1) * 0
            $y = 10 / $d
            PRINT $y
            """);

        var (code, errorFunc, errorPc) = RunToFault(result.Image!);
        var fn = result.Image!.Functions[errorFunc];
        Assert.That(fn.LineAt(errorPc), Is.EqualTo(3), "除零指令应映射回第 3 行");
    }

    [Test]
    public void RuntimeFault_InsideFunction_MapsToFunctionSourceLine()
    {
        // 第 2 行函数体内除零；调用点在第 4 行——映射应指向出错语句而非调用点
        var result = Compile("""
            FUNC boom($n) : int
            RETURN 10 / $n
            ENDFUNC
            $r = boom(0)
            PRINT $r
            """);

        var (code, errorFunc, errorPc) = RunToFault(result.Image!);
        // boom 为平凡函数会被内联进入口（名字变 <main>），但除零值保留源行：行映射仍指向第 2 行
        Assert.That(result.Image!.Functions[errorFunc].LineAt(errorPc), Is.EqualTo(2),
            "除零应映射回函数体第 2 行（内联后行号随值保留）");
    }

    [Test]
    public void EcxVm_Address_IsSourceLine_NotPc()
    {
        var result = Compile("""
            $x = 0
            $d = RAND(1) * 0
            $y = 10 / $d
            PRINT $y
            """);

        ScriptException? thrown = null;
        try
        {
            EcxVm.Run(result.Image!, EcsTestHost.Capabilities(new RecordingIo()),
                new CancellationTokenSource().Token, [], result.NativeSymbols);
        }
        catch (ScriptException ex)
        {
            thrown = ex;
        }

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.Not.Null, "应抛出 ScriptException");
            if (thrown == null)
                return;
            Assert.That(thrown.Address, Is.EqualTo(3), "Address 语义 = 源码行（1 基）");
            Assert.That(thrown.Message, Does.Contain("指令"), "消息保留 pc 供底层诊断");
        });
    }

    [Test]
    public void LineTable_Sparse_Monotonic_Invariants()
    {
        var result = Compile("""
            $i = 1
            WHILE $i <= 5
            PRINT $i
            $i += 1
            END
            PRINT "done"
            """);

        var entry = result.Image!.Functions[result.Image.Entry];
        Assert.Multiple(() =>
        {
            Assert.That(entry.LineTable, Is.Not.Empty, "入口函数应有行号登记");
            for (int i = 2; i < entry.LineTable.Count; i += 2)
                Assert.That(entry.LineTable[i], Is.GreaterThan(entry.LineTable[i - 2]),
                    "pc 必须严格递增（稀疏表不变量）");
            for (int i = 1; i < entry.LineTable.Count; i += 2)
                Assert.That(entry.LineTable[i], Is.GreaterThanOrEqualTo(1), "行号为 1 基");
        });
        Assert.That(entry.LineAt(0), Is.LessThanOrEqualTo(entry.LineTable[1]), "首 pc 前回退 0 或首行");
    }

    [Test]
    public void EcmFormat_Roundtrip_PreservesLineTable()
    {
        var result = Compile("""
            FUNC boom($n) : int
            RETURN 10 / $n
            ENDFUNC
            $r = boom(0)
            PRINT $r
            """);

        var restored = result.Artifacts.Select(a => EcmFormat.Read(EcmFormat.Write(a))).ToList();
        Assert.That(restored.Count, Is.EqualTo(result.Artifacts.Count), "模块数一致");
        for (int i = 0; i < restored.Count; i++)
        {
            Assert.That(restored[i].Functions.Count, Is.EqualTo(result.Artifacts[i].Functions.Count));
            for (int j = 0; j < restored[i].Functions.Count; j++)
            {
                var before = result.Artifacts[i].Functions[j];
                var after = restored[i].Functions[j];
                Assert.That(after.LineTable, Is.EqualTo(before.LineTable),
                    $"[{before.Module}/{before.Name}] 行号表应经序列化往返保持一致");
            }
        }

        var boom = restored.SelectMany(m => m.Functions).First(f => f.Name == "boom");
        Assert.That(boom.LineTable, Is.Not.Empty, "boom 应有行号登记");
    }
}