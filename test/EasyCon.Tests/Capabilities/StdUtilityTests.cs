using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Runner;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;
using System.Collections.Immutable;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// std 实用工具层（std 定 API/语义，打洞加速或低级原语组合）：
/// JQ/ENCODE 打洞转发语义、READFILE/WRITEFILE/APPENDFILE 文件组合语义。
/// </summary>
[TestFixture]
public class StdUtilityTests
{
    static RecordingIo Run(string source)
    {
        var engine = new EasyScriptEngine();
        var session = engine.FromSource(source,
            new ScriptHostOptions
            {
                Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false },
            });
        Assert.That(session.Info.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
            "编译诊断：" + string.Join("; ", session.Info.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var io = new RecordingIo();
        session.Run(new CancellationTokenSource().Token,
            new CapabilitySet { Console = new ConsoleIoAdapter(io) });
        return io;
    }

    // ---- JQ（std API → __JQ__ 打洞；查询语义不回归） ----

    [Test]
    public void Jq_ExtractsField_Index_AndMissing()
    {
        var io = Run("""
            $a = JQ("{\"name\":\"john\"}", ".name")
            PRINT $a
            $b = JQ("{\"tags\":[\"a\",\"b\"]}", ".tags[1]")
            PRINT $b
            $c = JQ("{\"age\":30}", ".age")
            PRINT $c
            $d = JQ("{\"age\":30}", ".nope")
            PRINT $d
            """);
        Assert.That(io.Lines, Is.EqualTo(new[] { "john", "b", "30", "" }));
    }

    // ---- ENCODE（std API → __ENCODE__ 打洞；仅接受 byte 数组——byte[] 的脚本侧来源待宿主原语提供） ----

    [Test]
    public void Encode_RejectsNonByteArray()
    {
        // int[] 实参在绑定期拒绝（严格 byte[] 语义，用户决策 2026-10）
        var engine = new EasyScriptEngine();
        var session = engine.FromSource("""
            $b = APPEND(APPEND([], 228), 184)
            $s = ENCODE($b)
            PRINT $s
            """, new ScriptHostOptions { Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false } });
        Assert.That(string.Join(";", session.Info.Diagnostics.Where(d => d.IsError).Select(d => d.Message)),
            Does.Contain("ENCODE"), "int[] 实参 → ENCODE 应绑定期拒绝（严格 byte[] 语义）");
    }

    // ---- READFILE / WRITEFILE / APPENDFILE（std 组合低级句柄原语） ----

    [Test]
    public void FileRoundTrip_WriteReadAppend()
    {
        // 路径将插入 ECS 字符串，避免 Windows 反斜杠被解释成转义。
        var path = Path.Combine(Path.GetTempPath(), $"ecs-std-util-{Guid.NewGuid():N}.txt").Replace('\\', '/');
        try
        {
            var io = Run($"""
                WRITEFILE "{path}", "line1"
                APPENDFILE "{path}", "line2"
                $s = READFILE("{path}")
                PRINT $s
                $l = LEN($s)
                PRINT $l
                """);
            Assert.That(io.Lines[^2], Is.EqualTo("line1line2"));
            Assert.That(io.Lines[^1], Is.EqualTo("10"));
            Assert.That(File.ReadAllText(path), Is.EqualTo("line1line2"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ReadFile_MissingPath_ReturnsEmpty()
    {
        var io = Run("""
            $s = READFILE("Z:/definitely/not/here.txt")
            PRINT $s
            $l = LEN($s)
            PRINT $l
            """);
        Assert.That(io.Lines, Is.EqualTo(new[] { "", "0" }));
    }
}