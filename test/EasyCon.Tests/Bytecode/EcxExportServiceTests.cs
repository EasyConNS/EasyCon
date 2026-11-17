using EasyCon.Core.Hosting;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Script.Bytecode;

namespace EasyCon.Tests.Bytecode;

[TestFixture]
public sealed class EcxExportServiceTests
{
    [Test]
    public void Export_ProducesRoundTrippableContainer()
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

    [TestCase("RETURN @Probe", "识图")]
    [TestCase("RETURN FRAME()", "__CAPTURE__")]
    [TestCase("RETURN OCR(1, 2, 30, 8, \"eng\")", "__TESSERACT__")]
    [TestCase("RETURN OCR_INIT(\"eng\")", "__TESSERACT_INIT__")]
    [TestCase("RETURN OCR_CONF()", "OCR_CONF")]
    [TestCase("EXTERN FUNC GetTickCount(): INT FROM \"kernel32.dll\"\nRETURN GetTickCount()", "kernel32.dll")]
    [TestCase("AUDIO_LOAD(\"Probe\")\nRETURN AUDIO_SCORE(\"Probe\")", "AUDIO_LOAD")]
    [TestCase("RETURN FILE_EXISTS(\"host-file.txt\")", "FILE_EXISTS")]
    [TestCase("RETURN NET_LOAD(\"model.onnx\")", "NET_LOAD")]
    public void Export_RejectsReachableDesktopOperations(string source, string dependency)
    {
        EcxExportResult export = EcxExportService.Export(source + "\n", null, ["Probe"]);
        Assert.That(export.Compilation.Image, Is.Not.Null, string.Join("\n", export.Diagnostics));
        Assert.That(export.Bytes, Is.Null);
        Assert.That(export.Diagnostics.Single(d => d.Code == DiagnosticCodes.OfflineCapability).Message,
            Does.Contain(dependency));
    }

    [Test]
    public void Export_AllowsConsoleOutputAndUnusedVisionHelpers()
    {
        const string source = """
            FUNC unused(): INT
                RETURN @Probe
            ENDFUNC
            PRINT 42
            RETURN 42
            """;
        EcxExportResult export = EcxExportService.Export(source, null, ["Probe"]);
        Assert.That(export.Success, Is.True, string.Join("\n", export.Diagnostics));
        EcxImage decoded = EcsContainer.ReadImage(export.Bytes!);
        Assert.That(decoded.NeedIL, Is.False);
        Assert.That(decoded.Features, Is.EqualTo(EcsImageFeatures.File), "PRINT 使用标准输出");
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