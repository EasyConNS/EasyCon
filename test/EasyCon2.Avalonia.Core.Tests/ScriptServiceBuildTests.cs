using EasyCon.Core.Script;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.Core.Tests;

public partial class ScriptServiceTests
{
    [Test]
    public async Task BuildAsync_UsesCurrentEditorSnapshotAndMatchesDistributableExport()
    {
        FakeLogService log = new();
        ScriptService service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());
        string path = CreateScriptFile("editor.ecs", "BREAK\n");
        const string editorText = "RETURN 42\n";

        Assert.That(await service.CompileAsync(editorText, path), Is.True);
        byte[] guiBytes = await service.BuildAsync(stripDebug: true);
        EcxExportResult cliEquivalent = EcxExportService.Export(editorText, path, [], stripDebug: true);

        Assert.Multiple(() =>
        {
            Assert.That(cliEquivalent.Success, Is.True, string.Join("; ", cliEquivalent.Diagnostics));
            Assert.That(guiBytes, Is.EqualTo(cliEquivalent.Bytes));
            Assert.That(log.Snapshot(), Does.Not.Contain("v1 Assemble"));
            Assert.That(log.Snapshot(), Does.Contain("ECX 导出完成: " + guiBytes.Length + " 字节"));
        });
    }

    [Test]
    public async Task BuildAsync_AfterCompileFailureDoesNotReusePreviousSnapshot()
    {
        FakeLogService log = new();
        ScriptService service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());

        Assert.That(await service.CompileAsync("RETURN 42\n", null), Is.True);
        Assert.That(await service.CompileAsync("BREAK\n", null), Is.False);

        byte[] bytes = await service.BuildAsync(stripDebug: true);

        Assert.That(bytes, Is.Empty);
        Assert.That(log.Snapshot(), Does.Contain("ECX 导出失败: 没有可导出的已编译脚本"));
    }
}