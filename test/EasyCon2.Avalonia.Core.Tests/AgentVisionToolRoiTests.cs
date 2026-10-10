using EasyCon.Core.Capabilities;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Tools;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// 取帧类工具的 ROI 语义表征化测试。
/// 根因背景：423c4d4 把取帧实现从 GetCurrentFrameBase64 换成 ICaptureSource.CaptureFrame 时
/// 用 (0,0,0,0) 表达"全图"，而端口契约是 0=空 ROI→null、负数=整帧——get_frame/ocr_frame
/// 自该提交起必然失败。本组测试钉死：整帧必须经语义化 CaptureFullFrame()，
/// 显式 ROI 原样透传，无帧/无源时返回可读错误。
/// </summary>
[TestFixture]
public class AgentVisionToolRoiTests
{
    /// <summary>记录 ROI 请求的假采集源（CaptureFullFrame 默认实现会落到 CaptureFrame）。</summary>
    private sealed class RecordingCaptureSource : ICaptureSource
    {
        public (int X, int Y, int W, int H)? LastRequest;
        public int CallCount;
        public string? NextFrame = "ZmFrZQ==";

        public long? FrameIndex => 1;

        public string? CaptureFrame(int x, int y, int width, int height)
        {
            CallCount++;
            LastRequest = (x, y, width, height);
            return NextFrame;
        }
    }

    private sealed class FakeOcr : IOcrService
    {
        public string Backend => "fake";
        public int LastConfidence => 88;
        public bool Init(OcrConfig cfg) => true;
        public string Recognize(ImageRef image, OcrQuery query) => "HELLO";
        public void Dispose() { }
    }

    private static Dictionary<string, JsonElement> Args(string json)
    {
        var dict = new Dictionary<string, JsonElement>();
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.EnumerateObject())
            dict[p.Name] = p.Value.Clone();
        return dict;
    }

    [Test]
    public async Task GetFrameTool_UsesFullFrameSemantic()
    {
        var source = new RecordingCaptureSource();
        var tool = new GetFrameTool(() => source);

        var result = await tool.ExecuteAsync(Args("{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Success), result.Content);
            Assert.That(result.AttachedMessage, Is.Not.Null, "get_frame 必须附带图片消息");
            Assert.That(source.LastRequest, Is.EqualTo((-1, -1, -1, -1)), "整帧必须走负数哨兵/语义入口");
        });
    }

    [Test]
    public async Task CaptureFrameTool_UsesFullFrameSemantic()
    {
        var source = new RecordingCaptureSource();
        var tool = new CaptureFrameTool(() => source);

        var result = await tool.ExecuteAsync(Args("{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Success), result.Content);
            Assert.That(result.AttachedMessage, Is.Not.Null);
            Assert.That(source.LastRequest, Is.EqualTo((-1, -1, -1, -1)));
        });
    }

    [Test]
    public async Task OcrFrameTool_DefaultsToFullFrame()
    {
        var source = new RecordingCaptureSource();
        var tool = new OcrFrameTool(() => new FakeOcr(), () => source);

        var result = await tool.ExecuteAsync(Args("{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Success), result.Content);
            Assert.That(result.Content, Does.Contain("HELLO"));
            Assert.That(source.LastRequest, Is.EqualTo((-1, -1, -1, -1)), "缺省参数=全图识别");
        });
    }

    [Test]
    public async Task OcrFrameTool_ExplicitRoiPassesThrough()
    {
        var source = new RecordingCaptureSource();
        var tool = new OcrFrameTool(() => new FakeOcr(), () => source);

        await tool.ExecuteAsync(Args("""{"x": 10, "y": 20, "width": 100, "height": 50}"""));

        Assert.That(source.LastRequest, Is.EqualTo((10, 20, 100, 50)), "显式 ROI 原样透传");
    }

    [Test]
    public async Task OcrFrameTool_PartialRoiMeansFullFrame()
    {
        var source = new RecordingCaptureSource();
        var tool = new OcrFrameTool(() => new FakeOcr(), () => source);

        await tool.ExecuteAsync(Args("""{"width": 100}"""));

        Assert.That(source.LastRequest, Is.EqualTo((-1, -1, -1, -1)), "任一维度缺省即全图（工具语义）");
    }

    [Test]
    public void GetFrameTool_ReportsReadableErrorWhenSourceMissing()
    {
        var tool = new GetFrameTool(() => null);

        var result = tool.ExecuteAsync(Args("{}")).Result;

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Error));
            Assert.That(result.Content, Does.Contain("视频源未连接"));
        });
    }

    [Test]
    public void OcrFrameTool_ReportsReadableErrorWhenSourceMissing()
    {
        var tool = new OcrFrameTool(() => new FakeOcr(), () => null);

        var result = tool.ExecuteAsync(Args("{}")).Result;

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Error));
            Assert.That(result.Content, Does.Contain("视频源未连接"));
        });
    }

    [Test]
    public async Task CaptureFrameTool_ReportsReadableErrorWhenNoFrame()
    {
        var source = new RecordingCaptureSource { NextFrame = null };
        var tool = new CaptureFrameTool(() => source);

        var result = await tool.ExecuteAsync(Args("{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Error));
            Assert.That(result.Content, Does.Contain("视频源未连接或无画面"));
            Assert.That(source.CallCount, Is.GreaterThan(1), "首帧等待重试生效");
        });
    }
}