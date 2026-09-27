using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EasyCon.Capture;
using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Avalonia.ViewModels;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace EasyCon2.Avalonia.UiTests;

/// <summary>
/// MonitorViewModel 双缓冲轮换的行为验证：
/// - 帧序列在恰好两块 WriteableBitmap 间轮换复用（不再逐帧新建 ~16MB 位图）；
/// - 分辨率变化时缓冲池按新尺寸重建；
/// - Close 后循环停止，在途/迟到帧不得再更新状态。
/// 走真实 33ms 计时器 + 轮询等待（上限宽裕，避免 CI 偶发超时）。
/// 不保存 PNG：headless 下 WriteableBitmap.Save 会让测试主机原生崩溃，见 KeyMappingWindowRenderTests。
/// </summary>
[TestFixture]
public class MonitorViewModelDoubleBufferTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // 同一测试进程里 KeyMappingWindowRenderTests 可能已完成初始化；
        // SetupWithoutStarting 重复调用会抛 InvalidOperationException
        if (Application.Current != null)
            return;

        TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
    }

    /// <summary>FrameStore 驱动的假采集服务：Publish 的 Mat 即最新帧（所有权归 FrameStore）。</summary>
    private sealed class FakeCaptureService : ICaptureService
    {
        public readonly FrameStore Store = new();
        public bool IsConnected { get; set; } = true;
        public string CaptureType { get; set; } = "Fake";
        public event Action? ConnectionLost;
        public event Action? ConnectionRestored;
        public string[] GetAvailableSources() => Array.Empty<string>();
        public bool TryConnect(string sourceName) => true;
        public Task DisconnectAsync() => Task.CompletedTask;
        public FrameLease? AcquireLatestFrame() => IsConnected ? Store.AcquireLatest() : null;
        public void SetCaptureProperties(int width, int height) { }
    }

    private static Mat CreateSolidMat(int width, int height)
    {
        return new Mat(height, width, MatType.CV_8UC3, new Scalar(30, 60, 90));
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
                return;
            Thread.Sleep(25);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.That(condition(), Is.True, $"等待条件超时（{timeout.TotalSeconds}s）");
    }

    [Test]
    public void FrameOutputRotatesBetweenTwoPooledBitmaps()
    {
        FakeCaptureService capture = new();
        capture.Store.Publish(CreateSolidMat(64, 48));
        MonitorViewModel vm = new(capture);

        HashSet<Bitmap> appliedFrames = new();
        int appliedCount = 0;
        vm.PropertyChanged += (object? sender, PropertyChangedEventArgs e) =>
        {
            if (e.PropertyName != nameof(MonitorViewModel.FrameIndex))
                return;
            lock (appliedFrames)
            {
                appliedCount++;
                if (vm.CurrentFrame != null)
                    appliedFrames.Add(vm.CurrentFrame);
            }
        };

        try
        {
            vm.StartMonitoring();
            WaitUntil(() => { lock (appliedFrames) return appliedCount >= 6; }, TimeSpan.FromSeconds(5));
        }
        finally
        {
            vm.Close();
            capture.Store.ReleaseCurrent();
        }

        lock (appliedFrames)
        {
            Assert.Multiple(() =>
            {
                Assert.That(appliedCount, Is.GreaterThanOrEqualTo(6), "30fps 循环应连续产出多帧");
                Assert.That(appliedFrames, Has.Count.EqualTo(2), "帧应在两块缓冲位图间轮换复用，而不是逐帧新建");
            });
        }
    }

    [Test]
    public void ResolutionChangeRebuildsPoolWithNewSize()
    {
        FakeCaptureService capture = new();
        capture.Store.Publish(CreateSolidMat(64, 48));
        MonitorViewModel vm = new(capture);

        try
        {
            vm.StartMonitoring();
            WaitUntil(() => vm.FrameIndex >= 2, TimeSpan.FromSeconds(5));

            capture.Store.Publish(CreateSolidMat(32, 24)); // 触发缓冲池重建
            WaitUntil(() =>
            {
                try
                {
                    Bitmap? frame = vm.CurrentFrame;
                    return frame != null && frame.PixelSize.Width == 32 && frame.PixelSize.Height == 24;
                }
                catch (ObjectDisposedException)
                {
                    // 池重建后、新尺寸首帧上屏前，CurrentFrame 仍指向已释放的旧缓冲；
                    // 视为条件未满足，等 33ms 节拍换上新池
                    return false;
                }
            }, TimeSpan.FromSeconds(5));
        }
        finally
        {
            vm.Close();
            capture.Store.ReleaseCurrent();
        }
    }

    [Test]
    public void CloseStopsLoopAndLateFramesCannotTouchState()
    {
        FakeCaptureService capture = new();
        capture.Store.Publish(CreateSolidMat(64, 48));
        MonitorViewModel vm = new(capture);

        try
        {
            vm.StartMonitoring();
            WaitUntil(() => vm.FrameIndex >= 2, TimeSpan.FromSeconds(5));

            vm.Close();
            int frameIndexAtClose = vm.FrameIndex;

            // 给在途的渲染与 Post 回调留出窗口，再确认循环确实已停
            var settleDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(300);
            while (DateTime.UtcNow < settleDeadline)
            {
                Thread.Sleep(50);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Multiple(() =>
            {
                Assert.That(vm.CurrentFrame, Is.Null);
                Assert.That(vm.FrameIndex, Is.EqualTo(frameIndexAtClose), "Close 后迟到的帧不得再更新状态");
            });
        }
        finally
        {
            vm.Close(); // 必须幂等
            capture.Store.ReleaseCurrent();
        }
    }
}