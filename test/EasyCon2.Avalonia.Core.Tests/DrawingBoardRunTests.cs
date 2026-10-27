using EasyCon.Core.Maa;
using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Models;
using static EasyCon2.Avalonia.Core.Tests.DrawingBoardTestKit;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>画板 ViewModel 的绘制部分：开始/停止/关闭、错误与断开、进度与剩余时间、橡皮擦模式。</summary>
[TestFixture]
[NonParallelizable]
public class DrawingBoardRunTests : DrawingBoardTestBase
{
    [Test]
    public async Task EraserMode_HoldsBAndErasesOnlyBlankPointsInRegion()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "1";
        f.Vm.SelectRegion = true;
        f.Vm.SetRegion(0, 0, 3, 1);
        f.Vm.EraserMode = true;

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        // 区域内第 0 行 0～2 列：第 1 列是墨点要保留，第 0、2 列擦成空白
        Assert.That(f.Pad.Events, Is.EqualTo(new[]
        {
            "press B", "release B",
            "click RIGHT", "click RIGHT", "press B", "release B",
            LeaseRelease,
        }));
    }

    [Test]
    public async Task EraserMode_IsFasterForMostlyBlackPictures()
    {
        byte[] rgba = new byte[320 * 120 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        SetPixel(rgba, 60, 160, 255, 255, 255, 255);
        Fixture f = new(new PixelImage(320, 120, rgba));
        await f.Vm.LoadPictureFileAsync("x.png");
        double pen = Minutes(f.Vm.EstimateText);

        f.Vm.EraserMode = true;

        Assert.That(Minutes(f.Vm.EstimateText), Is.LessThan(pen / 10), "几乎全黑的图只需擦一个点");
    }

    [Test]
    public async Task EraserMode_DoesNotChangeThePreview()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        PixelImage? preview = f.Vm.Preview;

        f.Vm.EraserMode = true;

        Assert.That(f.Vm.Preview, Is.SameAs(preview), "预览显示最终效果，两种模式相同");
    }

    [Test]
    public void EstimateRemaining_UsesPlanFirstThenActualPace()
    {
        Assert.That(DrawingBoardViewModel.EstimateRemaining(0, 10_000, TimeSpan.Zero), Is.EqualTo(TimeSpan.FromSeconds(10)));
        Assert.That(DrawingBoardViewModel.EstimateRemaining(2_000, 10_000, TimeSpan.FromSeconds(3)), Is.EqualTo(TimeSpan.FromSeconds(8)));
        // 实际用时是计划的 2 倍：剩余 8 s 计划按 2 倍校正
        Assert.That(DrawingBoardViewModel.EstimateRemaining(5_000, 13_000, TimeSpan.FromSeconds(10)), Is.EqualTo(TimeSpan.FromSeconds(16)));
        Assert.That(DrawingBoardViewModel.EstimateRemaining(10_000, 10_000, TimeSpan.FromSeconds(30)), Is.EqualTo(TimeSpan.Zero));
    }

    [TestCase(65, "01:05")]
    [TestCase(3599, "59:59")]
    [TestCase(3725, "1:02:05")]
    [TestCase(36000, "10:00:00")]
    public void FormatClock_ShowsHoursOnlyWhenNeeded(int seconds, string expected)
    {
        Assert.That(DrawingBoardViewModel.FormatClock(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }

    [Test]
    public async Task Stop_WaitsForDrawToExitThenReenablesStart()
    {
        Fixture f = new(Canvas((0, 100)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "10000";
        TaskCompletionSource firstKey = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pad.WaitForDuration = true;
        f.Pad.OnKey = () => firstKey.TrySetResult();

        await f.Vm.StartCommand.ExecuteAsync(null);
        await firstKey.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Vm.IsStartEnabled, Is.False);
        Assert.That(f.Vm.ProgressText, Does.StartWith("0% · 第 0/1 段 · 已用 00:00 · 剩余约 "));
        await f.Vm.StopCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Vm.ProgressText, Does.StartWith("已停止 · 完成 "));

        Assert.That(f.Vm.DrawTask!.IsCompleted, Is.True);
        Assert.That(f.Vm.IsStartEnabled, Is.True);
        Assert.That(f.Dialogs.Messages, Is.Empty, "用户停止不应弹错误");
        int count = f.Pad.Events.Count;
        await Task.Delay(100);
        Assert.That(f.Pad.Events, Has.Count.EqualTo(count), "停止后不应再有按键");
        Assert.That(f.Pad.Events.Last(), Is.EqualTo(LeaseRelease));
    }

    [Test]
    public async Task Start_Disconnected_Warns()
    {
        Fixture f = new(Canvas());
        f.Runner.Connected = false;

        await f.Vm.StartCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "串口未连接" }));
        Assert.That(f.Vm.DrawTask, Is.Null);
    }

    [Test]
    public async Task Start_WithoutImage_WarnsWithoutStarting()
    {
        Fixture f = new(Canvas());

        await f.Vm.StartCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "未加载图片" }));
        Assert.That(f.Vm.DrawTask, Is.Null);
        Assert.That(f.Vm.IsStartEnabled, Is.True);
    }

    [Test]
    public async Task Start_DrawsAndReenablesStart()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "1";

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        Assert.That(f.Vm.Preview, Is.Not.Null);
        Assert.That(f.Pad.Events, Does.Contain("press A"));
        Assert.That(f.Pad.Events.Last(), Is.EqualTo(LeaseRelease));
        Assert.That(f.Vm.IsStartEnabled, Is.True);
        Assert.That(f.Vm.ProgressValue, Is.EqualTo(100));
        Assert.That(f.Vm.ProgressText, Does.StartWith("已完成 · 用时 "));
    }

    [Test]
    public async Task Dispose_StopsRunningDrawAndReleasesKeys()
    {
        Fixture f = new(Canvas((0, 100)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "10000";
        TaskCompletionSource firstKey = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pad.WaitForDuration = true;
        f.Pad.OnKey = () => firstKey.TrySetResult();

        await f.Vm.StartCommand.ExecuteAsync(null);
        await firstKey.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Vm.Dispose();
        await f.Vm.DrawTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(f.Pad.Events.Last(), Is.EqualTo(LeaseRelease));
    }

    [Test]
    public async Task Start_DeviceBusy_ReportsAndReenablesStart()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Runner.Failure = new ControllerOperationException("Busy", "busy: 设备已有运行或正在收尾");

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        Assert.That(f.Vm.IsStartEnabled, Is.True);
        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "画图中断: 手柄正被脚本或其他任务占用，请先停止后再画" }));
        Assert.That(f.Pad.Events, Is.Empty);
        Assert.That(f.Vm.ProgressText, Is.EqualTo("已中断 · 完成 0%"));
    }

    [Test]
    public async Task Start_DeviceLostMidDraw_ReportsDisconnect()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "1";
        f.Pad.OnKey = () => throw new OperationCanceledException();

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        Assert.That(f.Vm.IsStartEnabled, Is.True);
        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "画图中断: 手柄连接已断开" }));
        Assert.That(f.Vm.ProgressText, Does.StartWith("已中断 · 完成 "));
        Assert.That(f.Pad.Events.Last(), Is.EqualTo(LeaseRelease));
    }

    [Test]
    public async Task Pause_ThenContinue_TogglesStateAndTexts()
    {
        Fixture f = new(Canvas((0, 100)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "10000";
        TaskCompletionSource firstKey = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pad.WaitForDuration = true;
        f.Pad.OnKey = () => firstKey.TrySetResult();
        Assert.That(f.Vm.TogglePauseCommand.CanExecute(null), Is.False, "没在画时不能暂停");

        await f.Vm.StartCommand.ExecuteAsync(null);
        await firstKey.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Vm.TogglePauseCommand.CanExecute(null), Is.True);
        Assert.That(f.Vm.PauseButtonText, Is.EqualTo("暂停"));

        f.Vm.TogglePauseCommand.Execute(null);
        Assert.That(f.Vm.IsPaused, Is.True);
        Assert.That(f.Vm.PauseButtonText, Is.EqualTo("继续"));
        Assert.That(f.Vm.ProgressText, Does.StartWith("已暂停 · 0% · 第 0/1 段 · 已用 "));

        f.Vm.TogglePauseCommand.Execute(null);
        Assert.That(f.Vm.IsPaused, Is.False);
        Assert.That(f.Vm.PauseButtonText, Is.EqualTo("暂停"));

        await f.Vm.StopCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Vm.IsDrawing, Is.False);
        Assert.That(f.Vm.TogglePauseCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task Stop_WhilePaused_EndsTheDraw()
    {
        Fixture f = new(Canvas((0, 100)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "1";

        // 开始后立刻暂停：第一次按键前就停住
        await f.Vm.StartCommand.ExecuteAsync(null);
        f.Vm.TogglePauseCommand.Execute(null);
        await Task.Delay(200);
        Assert.That(f.Pad.Events, Is.Empty, "暂停期间不发键");

        await f.Vm.StopCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(f.Vm.DrawTask!.IsCompleted, Is.True);
        Assert.That(f.Vm.IsPaused, Is.False);
        Assert.That(f.Vm.IsStartEnabled, Is.True);
        Assert.That(f.Pad.Events.Last(), Is.EqualTo(LeaseRelease));
    }

    [Test]
    public async Task Finished_PreviewShowsDrawnInkAndTheCursor()
    {
        Fixture f = new(Canvas((0, 1), (0, 50)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "1";

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        PixelImage display = f.Vm.DisplayImage!;
        Assert.That(display.GetPixel(1, 0).ToArgb(), Is.EqualTo(System.Drawing.Color.Black.ToArgb()), "已画的墨点");
        Assert.That(display.GetPixel(50, 0).R, Is.EqualTo(PreviewOverlay.Cursor.R), "光标停在最后一段");
        Assert.That(f.Vm.ProgressText, Does.StartWith("已完成"));
        Assert.That(f.Vm.Preview, Is.Not.SameAs(display), "保存图片用的预览不含进度叠加");

        f.Vm.ThresholdPercent = 40;
        Assert.That(f.Vm.DisplayImage, Is.SameAs(f.Vm.Preview), "改了图片参数就恢复显示预览");
    }

    [Test]
    public async Task Progress_CountsCompletedSegments()
    {
        Fixture f = new(Canvas((0, 1), (0, 50), (3, 7)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "1";
        TaskCompletionSource secondSegment = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int presses = 0;
        f.Pad.OnKey = () =>
        {
            if (f.Pad.Events.Last() == "press A" && ++presses == 2)
            {
                f.Vm.TogglePauseCommand.Execute(null);
                secondSegment.TrySetResult();
            }
        };

        await f.Vm.StartCommand.ExecuteAsync(null);
        await secondSegment.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(f.Vm.ProgressText, Does.Contain("第 1/3 段"), "第一段已画完，第二段刚落笔");
        await f.Vm.StopCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Progress_OutsideTheRegion_ShowsTheUntouchedCanvasFaintly(bool eraser)
    {
        // 第 0 行第 1 列与第 100 列各有一个墨点；只框选左边 0～49 列
        Fixture f = new(Canvas((0, 1), (0, 100)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "1";
        f.Vm.EraserMode = eraser;
        f.Vm.SelectRegion = true;
        f.Vm.SetRegion(0, 0, 50, 120);

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        // 区域外不处理：画笔模式的墨点、橡皮擦模式本该擦的点都只淡淡显示，不算已画也不算待画
        (byte R, byte G, byte B) Rgb(int col, int row)
        {
            System.Drawing.Color c = f.Vm.DisplayImage!.GetPixel(col, row);
            return (c.R, c.G, c.B);
        }
        if (eraser)
        {
            Assert.That(Rgb(100, 60), Is.EqualTo(PreviewOverlay.OutsideErase), "区域外本该擦的空白处：仍是黑，略亮");
            Assert.That(Rgb(100, 0), Is.EqualTo(((byte)0, (byte)0, (byte)0)), "区域外的墨点：保持涂黑");
        }
        else
        {
            Assert.That(Rgb(100, 0), Is.EqualTo(PreviewOverlay.OutsideInk), "区域外的墨点没画，但淡淡保留图片内容");
            Assert.That(Rgb(100, 60), Is.EqualTo(((byte)255, (byte)255, (byte)255)));
        }
    }

    [Test]
    public async Task AfterStopping_ReselectingTheRegionShowsTheWholePictureAgain()
    {
        Fixture f = new(Canvas((0, 1), (50, 200)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "10000";
        f.Pad.WaitForDuration = true;
        TaskCompletionSource firstKey = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pad.OnKey = () => firstKey.TrySetResult();
        f.Vm.SelectRegion = true;
        f.Vm.SetRegion(0, 0, 50, 120);

        await f.Vm.StartCommand.ExecuteAsync(null);
        await firstKey.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Vm.StopCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Vm.DisplayImage!.GetPixel(200, 50).R, Is.EqualTo(PreviewOverlay.OutsideInk.R), "停止后仍显示进度图，区域外淡化");

        f.Vm.OnPicturePressed(400, 100, 822, 282);
        f.Vm.OnPictureDragged(800, 250, 822, 282);
        f.Vm.OnPictureReleased();

        Assert.That(f.Vm.RegionText, Does.StartWith("绘制区域：第 155–311 列"));
        Assert.That(f.Vm.DisplayImage!.GetPixel(200, 50).ToArgb(), Is.EqualTo(System.Drawing.Color.Black.ToArgb()),
            "重新框选时换回完整预览，区域外的图片内容照常显示");
    }
}