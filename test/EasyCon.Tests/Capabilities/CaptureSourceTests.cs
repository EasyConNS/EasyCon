using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Capabilities;
using OpenCvSharp;

namespace EasyCon.Tests.Capabilities;

/// <summary>
/// 帧能力适配：委托哨兵归一、帧号（等待新帧/慢感知 everyFrames 的判据）与 ROI 裁剪。
/// </summary>
[TestFixture]
public class CaptureSourceTests
{
    [Test]
    public void DelegateCaptureSource_NormalizesFailureSentinels()
    {
        ICaptureSource noFrame = new DelegateCaptureSource((_, _, _, _) => FrameDelegateFactory.NoFrameError);
        ICaptureSource badRoi = new DelegateCaptureSource((_, _, _, _) => FrameDelegateFactory.RoiError);
        ICaptureSource ok = new DelegateCaptureSource((_, _, _, _) => "B64");

        Assert.Multiple(() =>
        {
            // 端口契约：不可用 = null（哨兵字符串不得被下游当成 Base64 图像）
            Assert.That(noFrame.CaptureFrame(-1, -1, -1, -1), Is.Null);
            Assert.That(badRoi.CaptureFrame(0, 0, 10, 10), Is.Null);
            Assert.That(ok.CaptureFrame(-1, -1, -1, -1), Is.EqualTo("B64"));
            Assert.That(noFrame.FrameIndex, Is.Null, "委托适配不提供帧号");
        });
    }

    [Test]
    public void FrameStoreCaptureSource_ExposesFrameIndexAndRoi()
    {
        var store = new FrameStore();
        ICaptureSource source = new FrameStoreCaptureSource(store);

        Assert.That(source.CaptureFrame(-1, -1, -1, -1), Is.Null, "未发布帧时为 null");
        Assert.That(source.FrameIndex, Is.EqualTo(0));

        using var mat = new Mat(40, 80, MatType.CV_8UC3, Scalar.Green);
        store.Publish(mat);

        Assert.Multiple(() =>
        {
            Assert.That(source.FrameIndex, Is.EqualTo(1), "每发布一帧帧号 +1");
            Assert.That(source.CaptureFrame(-1, -1, -1, -1), Is.Not.Null);
            Assert.That(source.CaptureFrame(0, 0, 10, 10), Is.Not.Null);
        });

        // ROI 收敛到空（越界起点）→ null，而不是抛异常
        Assert.That(source.CaptureFrame(9999, 0, 10, 10), Is.Null);

        using var second = new Mat(40, 80, MatType.CV_8UC3, Scalar.Red);
        store.Publish(second);
        Assert.That(source.FrameIndex, Is.EqualTo(2));
    }

    [Test]
    public void CaptureFrame_ZeroRoiIsNull_FullFrameRequiresSentinel()
    {
        // 423c4d4 回归病灶：把 (0,0,0,0) 当"全图"传入会被 ROI 收敛成 null。
        // 端口语义：0 尺寸=空 ROI → null；负数=整帧。工具层必须走 CaptureFullFrame()。
        var store = new FrameStore();
        ICaptureSource source = new FrameStoreCaptureSource(store);
        using var mat = new Mat(8, 8, MatType.CV_8UC3, Scalar.Green);
        store.Publish(mat);

        Assert.Multiple(() =>
        {
            Assert.That(source.CaptureFrame(0, 0, 0, 0), Is.Null, "0 尺寸 ROI 收敛为空");
            Assert.That(source.CaptureFrame(-1, -1, -1, -1), Is.Not.Null, "负数哨兵=整帧");
        });
    }

    [Test]
    public void CaptureFullFrame_RoutesThroughCaptureFrameWithSentinel()
    {
        // 语义化入口：默认实现必须以 (-1,-1,-1,-1) 落到 CaptureFrame，
        // 且 FrameStoreCaptureSource 经它拿到的是真实整帧内容
        var requested = new List<(int X, int Y, int W, int H)>();
        ICaptureSource recording = new DelegateCaptureSource((x, y, w, h) =>
        {
            requested.Add((x, y, w, h));
            return "B64";
        });

        Assert.Multiple(() =>
        {
            Assert.That(recording.CaptureFullFrame(), Is.EqualTo("B64"));
            Assert.That(requested, Is.EqualTo(new List<(int, int, int, int)> { (-1, -1, -1, -1) }),
                "语义入口落点必须是整帧哨兵");
        });

        var store = new FrameStore();
        ICaptureSource source = new FrameStoreCaptureSource(store);
        using var mat = new Mat(8, 8, MatType.CV_8UC3, Scalar.Green);
        store.Publish(mat);
        Assert.That(source.CaptureFullFrame(), Is.Not.Null);
    }
}