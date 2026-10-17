using EasyCon.Core.Maa;
using System.Text.RegularExpressions;

namespace EasyCon.Tests.Maa;

[TestFixture]
public sealed class MaaBoundaryTests
{
    [Test]
    public void RealRuntime_DiagnosticLoadsResourceAndExplicitImageWithoutHardware()
    {
        MaaRuntimeCheck check = MaaHostDiagnostics.CheckRuntime();
        Assert.That(check.Runtime, Is.EqualTo("v5.14.3"));
        Assert.That(check.ResourceLoaded && check.TaskerInitialized && check.PixelRoundTrip, Is.True);
        Assert.That(check.RecognitionStatus, Is.EqualTo("Succeeded"));
    }

    [Test]
    public void ProductionBoundary_NoRawDeviceEscapes_AndModulesDoNotReferenceEachOther()
    {
        DirectoryInfo? root = new(TestContext.CurrentContext.TestDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "EasyCon2.slnx"))) root = root.Parent;
        Assert.That(root, Is.Not.Null);
        string source = Path.Combine(root!.FullName, "src");
        foreach (string project in new[] { "EasyCon2.Avalonia", "EasyCon2.CLI", "EasyCon.SDLInput" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(source, project), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
            {
                Assert.That(Regex.IsMatch(File.ReadAllText(file), @"\bNintendoSwitch\b"), Is.False, file);
            }
        }
        string pad = File.ReadAllText(Path.Combine(source, "EasyCon.Core", "Input", "PadSession.cs"));
        Assert.That(pad, Does.Not.Contain("EasyCon.Capture").And.Not.Contain("IFrameHub").And.Not.Contain("MaaFramework.Binding"));
        string frames = File.ReadAllText(Path.Combine(source, "EasyCon.Core", "Maa", "FrameHub.cs"));
        Assert.That(frames, Does.Not.Contain("PadSession").And.Not.Contain("NintendoSwitch").And.Not.Contain("IControllerSession"));
        foreach (string leaf in new[] { "EasyCon.Device", "EasyCon.Capture", "EasyCon.Script" })
        {
            string project = File.ReadAllText(Path.Combine(source, leaf, leaf + ".csproj"));
            Assert.That(project, Does.Not.Contain("Maa.Framework").And.Not.Contain("EasyCon.Core"));
        }
    }
}