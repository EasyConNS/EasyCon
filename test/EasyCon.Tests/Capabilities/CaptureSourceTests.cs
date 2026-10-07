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
}