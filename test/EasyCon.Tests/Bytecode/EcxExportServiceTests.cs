using EasyCon.Core.Hosting;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Script.Bytecode;

namespace EasyCon.Tests.Bytecode;

[TestFixture]
public sealed class EcxExportServiceTests
{
    [Test]
    public void Export_UsesDistributableProfile_AndProducesRoundTrippableContainer()
    {
        const string source = "RETURN 42\n";
        EcxExportResult export = EcxExportService.Export(source, null, [], stripDebug: true);

        Assert.That(export.Success, Is.True, string.Join("; ", export.Diagnostics));
        Assert.That(export.Bytes, Is.Not.Null);
        Assert.That(export.Compilation.Image, Is.Not.Null);
        EcxImage roundTrip = EcsContainer.ReadImage(export.Bytes!);
        byte[] expected = EcsContainer.WriteImage(export.Compilation.Image!, stripDebug: true);

        Assert.That(export.Bytes, Is.EqualTo(expected));
        Assert.That(roundTrip.Entry, Is.EqualTo(export.Compilation.Image!.Entry));
    }

    [Test]
    public void Export_InvalidSourceHasNoBytesOrContainerDiagnosticLeak()
    {
        EcxExportResult export = EcxExportService.Export("BREAK\n", null, [], stripDebug: true);

        Assert.That(export.Success, Is.False);
        Assert.That(export.Bytes, Is.Null);
        Assert.That(export.Diagnostics.Count(d => d.IsError), Is.GreaterThan(0));
        Assert.That(export.Diagnostics.Count, Is.EqualTo(export.Diagnostics.DistinctStable().Count()));
        Assert.That(export.Diagnostics.Any(d => d.Message.Contains("v1 Assemble")), Is.False);
    }

    [Test]
    public void ProjectCompiler_ErrorGateLeavesImageNullAndDeduplicatesDiagnostics()
    {
        CompileOptions options = ScriptCompileProfiles.Interactive();
        options.UseProcessCache = false;
        var project = EasyCon.Script.Modules.ProjectCompiler.CompileProject(
            EasyCon.Script.Syntax.SyntaxTree.Parse("BREAK\n"), options);

        Assert.That(project.Success, Is.False);
        Assert.That(project.Image, Is.Null);
        Assert.That(project.Diagnostics.Count, Is.EqualTo(project.Diagnostics.DistinctStable().Count()));
    }
}