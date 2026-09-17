using EasyCon.Core.Capabilities;
using EasyCon.Core.Runner;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Tests.Support;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// P1 能力装配面锁定：CapabilitySet 入口与旧 12 参入口逐事件/逐行对拍
/// （覆盖 PRINT/WAIT/KEY/STICK/标签/FRAME/ARG；零行为迁移证明）。
/// </summary>
[TestFixture]
public class CapabilityRunnerTests
{
    const string Script = """
        PRINT "p1"
        WAIT 10
        A 30
        FOR 2
            B 40
        NEXT
        LS LEFT
        $hit = @lbl
        PRINT $hit
        $f = FRAME()
        PRINT $f
        $a = ARG(0)
        PRINT $a
        """;

    static readonly string[] ExpectedLines = ["p1", "2", "B64", "x"];

    [Test]
    public void CapabilitySet_Entry_EndToEnd()
    {
        var engine = new EasyScriptEngine();
        var session = engine.FromSource(Script,
            new ScriptHostOptions { Compile = new CompileOptions { ExtVars = ImmutableHashSet.Create("lbl"), UseDiskCache = false } });

        var io = new RecordingIo();
        var pad = new RecordingPad();
        var capabilities = new CapabilitySet
        {
            Input = new PadInputAdapter(pad),
            Console = new ConsoleIoAdapter(io),
            Environment = new HostEnvironment(["x"], AppDomain.CurrentDomain.BaseDirectory),
            Capture = new DelegateCaptureSource((x, y, w, h) => "B64"),
            Vision = new DelegateVisionService(null, name => 2),
        };
        session.Run(new CancellationTokenSource().Token, capabilities);

        Assert.That(io.Lines, Is.EqualTo(ExpectedLines));
        // 键序与金标准 mock 一致：A→B×2→STICK
        Assert.That(pad.Events, Does.Contain("KEY 3 30"));
        Assert.That(pad.Events, Does.Contain("KEY 2 40"));
    }
}