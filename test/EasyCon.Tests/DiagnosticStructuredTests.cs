using EasyCon.Script;
using EasyCon.Script.Text;

namespace EasyCon.Tests;

/// <summary>
/// M1 诊断结构化（SCRIPT_MODERNIZATION_PLAN.md）：错误码分配、严重级统一、JSON 输出快照。
/// </summary>
[TestFixture]
public class DiagnosticStructuredTests
{
    static CompileResult Compile(string source)
        => Compilation.CompileSource(source, new CompileOptions
        {
            ExtVars = System.Collections.Immutable.ImmutableHashSet<string>.Empty,
            UseDiskCache = false,
            UseProcessCache = false,
        });

    [Test]
    public void Diagnostics_CarryCodes_AndLocations()
    {
        var result = Compile("$x = @nope\n$s = \"ab\"\n$x2 = $s[\"i\"]\n");
        var errors = result.Diagnostics.Where(d => d.IsError).ToList();
        Assert.That(errors, Is.Not.Empty);
        Assert.That(errors.All(d => d.Code != null), "主路径诊断应全部带错误码");
        Assert.That(errors.All(d => d.Severity == DiagnosticSeverity.Error));
        Assert.That(errors.Any(d => d.Code == DiagnosticCodes.ImageLabelNotFound));
        var withTypes = errors.FirstOrDefault(d => d.Code == DiagnosticCodes.CannotConvert);
        Assert.That(withTypes, Is.Not.Null, "字符串下标应报 CannotConvert");
        Assert.That(withTypes!.Location.StartLine, Is.EqualTo(2), "诊断定位到第三行");
    }

    [Test]
    public void JsonFormat_StructuredSnapshot()
    {
        var result = Compile("$s = \"ab\"\n$x = $s[\"i\"]\n");
        var json = DiagnosticFormat.ToJson(result.Diagnostics);
        Assert.That(json, Does.Contain("\"code\": \"ECX0201\""), "CannotConvert 码");
        Assert.That(json, Does.Contain("\"severity\": \"error\""));
        Assert.That(json, Does.Contain("\"line\": 1"));
        Assert.That(json, Does.Contain("\"character\":"));
        TestContext.Out.WriteLine(json);
    }

    [Test]
    public void Notes_CanAttach()
    {
        var loc = new TextLocation(SourceText.From("$x = 1", "t.ecs"), new SourceSpan(0, 1));
        var diag = Diagnostic.Error(loc, "主诊断", DiagnosticCodes.VariableNotFound)
            .WithNotes(new DiagnosticNote(loc, "另见"));
        Assert.That(diag.Notes.Length, Is.EqualTo(1));
        Assert.That(diag.Notes[0].Message, Is.EqualTo("另见"));
    }
}
