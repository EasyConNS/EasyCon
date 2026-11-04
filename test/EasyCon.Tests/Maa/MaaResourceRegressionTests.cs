using EasyCon.Capture;
using EasyCon.Core.Maa;
using OpenCvSharp;
using System.Text.Json.Nodes;

namespace EasyCon.Tests.Maa;

public sealed partial class MaaVisionTests
{
    [Test]
    public void RelocatedIdenticalLabels_PublishDistinctImmutableGenerations_AndKeepCorruptionDetection()
    {
        using Mat template = Pattern();
        string source = SaveLabel(SearchMethod.CCoeffNormed, template);
        string relocated = Path.Combine(_root, "relocated");
        Directory.CreateDirectory(relocated);
        File.Copy(source, Path.Combine(relocated, "sample.IL"));
        string cache = Path.Combine(_root, "cache");
        LabelDiscovery firstSource = LegacyLabelCatalog.Scan([Path.GetDirectoryName(source)!]);
        LabelDiscovery secondSource = LegacyLabelCatalog.Scan([relocated]);
        LabelGeneration first = MaaLabelConverter.Build(firstSource, cache, LegacyCompatibilityProfile.LegacyGui).Generation!;
        string firstIndex = File.ReadAllText(Path.Combine(first.Directory, "easycon", "labels.index.json"));
        LabelGeneration second = MaaLabelConverter.Build(secondSource, cache, LegacyCompatibilityProfile.LegacyGui).Generation!;
        Assert.Multiple(() =>
        {
            Assert.That(firstSource.Selected.Single().Digest, Is.EqualTo(secondSource.Selected.Single().Digest));
            Assert.That(first.Id, Is.Not.EqualTo(second.Id));
            Assert.That(second.Labels["sample"].Source, Is.EqualTo(Path.Combine(relocated, "sample.IL")));
            Assert.That(File.ReadAllText(Path.Combine(first.Directory, "easycon", "labels.index.json")), Is.EqualTo(firstIndex));
        });
        Assert.That(MaaLabelConverter.Build(secondSource, cache, LegacyCompatibilityProfile.LegacyGui).Generation!.Id, Is.EqualTo(second.Id));
        string current = File.ReadAllText(Path.Combine(cache, "current.json"));
        File.WriteAllText(Path.Combine(second.Directory, "easycon", "labels.index.json"), "{}");
        Assert.Throws<InvalidDataException>(() => MaaLabelConverter.Build(secondSource, cache, LegacyCompatibilityProfile.LegacyGui));
        Assert.That(File.ReadAllText(Path.Combine(cache, "current.json")), Is.EqualTo(current));
    }

    [TestCase(SearchMethod.CCoeffNormed)]
    [TestCase(SearchMethod.CCorrNormed)]
    [TestCase(SearchMethod.SqDiffNormed)]
    public void LongUnicodeCachePaths_ExecuteRealNativeTemplates_WithTheSameScoresAndBoxes(SearchMethod method)
    {
        using Mat template = Pattern();
        SaveLabel(method, template);
        LabelDiscovery labels = LegacyLabelCatalog.FromParents(_root);
        LabelGeneration shortPath = ConvertLabels();
        string longCache = Path.Combine(_root, "长路径-" + new string('a', 90), new string('b', 90), "cache");
        LabelGeneration longPath = MaaLabelConverter.Build(labels, longCache, LegacyCompatibilityProfile.LegacyGui).Generation!;
        string templatePath = Path.Combine(longPath.Directory, "image", "legacy", longPath.Labels["sample"].TemplateDigest + ".png");
        Assert.That(templatePath.Length, Is.GreaterThan(300));
        using Mat frame = new(1080, 1920, MatType.CV_8UC3, Scalar.All(71));
        using (Mat target = new(frame, new Rect(30, 20, 16, 12))) template.CopyTo(target);
        byte[] png = frame.ToBytes(".png");
        using MaaVisionService shortService = new(shortPath);
        using MaaVisionService longService = new(longPath);
        LegacyRecognitionTrace expected = shortService.MatchImage("sample", png);
        LegacyRecognitionTrace actual = longService.MatchImage("sample", png);
        Assert.Multiple(() =>
        {
            Assert.That(longService.Session.ValidatedTemplates, Is.EqualTo(1));
            Assert.That(actual.NativeRaw, Is.Not.Null);
            Assert.That(actual.NativeRaw, Is.EqualTo(expected.NativeRaw));
            Assert.That(actual.LegacyFloat, Is.EqualTo(expected.LegacyFloat));
            Assert.That(actual.LegacyInteger, Is.EqualTo(expected.LegacyInteger));
            Assert.That(actual.Box, Is.EqualTo(expected.Box));
            Assert.That(actual.Route, Is.Not.EqualTo("native-no-candidate"));
        });
        TestContext.Out.WriteLine($"native template path={templatePath.Length}, method={method}, raw={actual.NativeRaw}, route={actual.Route}");
    }

    [Test]
    public void LongPathLargePipeline_UsesOwnedShortBundle_AndKeepsSourceGenerationAfterDisposal()
    {
        using Mat template = Pattern();
        for (int index = 0; index < 256; index++) SaveLabel(SearchMethod.CCoeffNormed, template, "sample" + index);
        string cache = Path.Combine(_root, "长目录-" + new string('a', 90), new string('b', 90));
        LabelGeneration generation = MaaLabelConverter.Build(LegacyLabelCatalog.FromParents(_root), cache, LegacyCompatibilityProfile.LegacyGui).Generation!;
        string sourcePipeline = Path.Combine(generation.Directory, "pipeline", "legacy.native.json");
        Assert.That(sourcePipeline.Length, Is.GreaterThan(300));
        Assert.That(new FileInfo(sourcePipeline).Length, Is.GreaterThan(65536));
        using Mat frame = new(1080, 1920, MatType.CV_8UC3, Scalar.All(71));
        using (Mat target = new(frame, new Rect(30, 20, 16, 12))) template.CopyTo(target);
        string nativeDirectory;
        using (MaaVisionService service = new(generation))
        {
            nativeDirectory = service.Session.NativeResourceDirectory;
            Assert.That(nativeDirectory, Is.Not.EqualTo(generation.Directory));
            Assert.That(Path.Combine(nativeDirectory, "pipeline", "legacy.native.json").Length, Is.LessThan(240));
            Assert.That(File.ReadAllBytes(Path.Combine(nativeDirectory, "pipeline", "legacy.native.json")), Is.EqualTo(File.ReadAllBytes(sourcePipeline)));
            Assert.That(service.Session.ValidatedTemplates, Is.EqualTo(1), "相同图片只验证一次，所有引用复用同一份原生图像");
            Assert.That(service.MatchImage("sample255", frame.ToBytes(".png")).NativeRaw, Is.Not.Null);
        }
        Assert.That(Directory.Exists(nativeDirectory), Is.False);
        Assert.That(File.Exists(sourcePipeline), Is.True);
    }

    [Test]
    public void MissingOrCorruptedTemplate_FailsSessionCreationWithoutLeakingOrReplacingCurrent()
    {
        using Mat template = Pattern();
        SaveLabel(SearchMethod.CCoeffNormed, template);
        LabelGeneration generation = ConvertLabels();
        string path = Path.Combine(generation.Directory, "image", "legacy", generation.Labels["sample"].TemplateDigest + ".png");
        string current = File.ReadAllText(Path.Combine(_root, "cache", "current.json"));
        int sessions = MaaRecognitionSession.ActiveSessions;
        File.WriteAllText(path, "damaged");
        Assert.Throws<InvalidDataException>(() => new MaaVisionService(generation));
        File.Delete(path);
        Assert.Throws<InvalidDataException>(() => new MaaVisionService(generation));
        Assert.Multiple(() =>
        {
            Assert.That(MaaRecognitionSession.ActiveSessions, Is.EqualTo(sessions));
            Assert.That(File.ReadAllText(Path.Combine(_root, "cache", "current.json")), Is.EqualTo(current));
        });
    }

    [Test]
    public void NativeResourceAndEvaluationErrors_AreNotReportedAsNoCandidate()
    {
        using Mat template = Pattern();
        SaveLabel(SearchMethod.CCoeffNormed, template);
        LabelGeneration generation = ConvertLabels();
        using MaaRecognitionSession session = new(generation.Directory, generation.Labels);
        JsonObject parameters = JsonNode.Parse(generation.Labels["sample"].NativeParameters!)!.AsObject();
        parameters["template"] = "legacy/missing.png";
        InvalidDataException? missing = Assert.ThrowsAsync<InvalidDataException>(async () => await session.RecognizeAsync(
            new("TemplateMatch", parameters.ToJsonString(), template.ToBytes(".png"), "missing", null, generation.Id), default));
        Assert.That(missing!.Message, Does.Contain("未通过原生装载验证"));
        parameters = JsonNode.Parse(generation.Labels["sample"].NativeParameters!)!.AsObject();
        parameters["roi"] = new JsonArray(0, 0, 1, 1);
        InvalidDataException? failed = Assert.ThrowsAsync<InvalidDataException>(async () => await session.RecognizeAsync(
            new("TemplateMatch", parameters.ToJsonString(), template.ToBytes(".png"), "invalid-roi", null, generation.Id), default));
        Assert.That(failed!.Message, Does.Contain("不能视为正常无候选"));
        Assert.That(session.RetainedResults, Is.Zero);
    }
}