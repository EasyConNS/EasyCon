using Avalonia;
using EasyCon.Capture;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.TagEditor;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ILogService = EasyCon.Core.Services.ILogService;

namespace EasyCon2.Avalonia.UiTests;

/// <summary>
/// TagEditorCoordinator 在视频源不可用时的行为验证：
/// - 未连接视频源：截图请求只记日志（"请先连接视频源"），不抛异常、不改动 ViewModel 状态；
/// - 已连接但拿不到帧：只记日志（"截图失败：无法获取视频帧"），同样不抛异常。
/// 守卫分支无 await、同步完成，断言无需等待异步帧流程。
/// </summary>
[TestFixture]
public class TagEditorCoordinatorTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // 同一测试进程里其他测试可能已完成初始化；
        // SetupWithoutStarting 重复调用会抛 InvalidOperationException
        if (Application.Current != null)
            return;

        TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
    }

    /// <summary>未连接视频源的假采集服务：IsConnected 可切换，AcquireLatestFrame 恒返回 null 租约。</summary>
    private sealed class FakeCaptureService : ICaptureService
    {
        public bool IsConnected { get; set; }
        public string CaptureType { get; set; } = "Fake";
        public event Action? ConnectionLost;
        public event Action? ConnectionRestored;
        public event Action? ConnectionStateChanged;
        public string[] GetAvailableSources() => Array.Empty<string>();
        public bool TryConnect(string sourceName) => false;
        public Task DisconnectAsync() => Task.CompletedTask;
        public FrameLease? AcquireLatestFrame() => null;
        public void SetCaptureProperties(int width, int height) { }
    }

    /// <summary>对话框假实现：测试中不应弹出任何对话框，弹了即失败。</summary>
    private sealed class ThrowingDialogService : IDialogService
    {
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title,
            IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedStartPath = null)
            => throw new InvalidOperationException("测试中不应打开文件对话框");
        public Task<string?> SaveFileAsync(string title, string defaultExtension,
            IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedFileName = null)
            => throw new InvalidOperationException("测试中不应打开保存对话框");
        public Task<string?> OpenFolderAsync(string title, string? suggestedStartPath = null)
            => throw new InvalidOperationException("测试中不应打开文件夹对话框");
        public Task<DialogColor?> PickColorAsync(DialogColor current)
            => throw new InvalidOperationException("测试中不应打开颜色对话框");
        public Task<string[]?> ShowScriptArgsDialogAsync()
            => throw new InvalidOperationException("测试中不应打开参数对话框");
        public Task<bool> ConfirmAsync(string title, string message)
            => throw new InvalidOperationException("测试中不应打开确认对话框");
    }

    /// <summary>记录型日志服务：把 AddLog 的消息收集下来供断言。</summary>
    private sealed class RecordingLogService : ILogService
    {
        public List<string> Messages { get; } = new();
        public event Action<string?, string?>? LogAppended;
        public void Clear() => Messages.Clear();
        public void AddLog(string message, string? color = null) => Messages.Add(message);
        public void Print(string message, bool newline = true) { }
        public void Alert(string message) { }
        public string ReadLine() => "";
        public bool TryReadLine(out string line) { line = ""; return false; }
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            Thread.Sleep(25);
        }
        Assert.That(condition(), Is.True, $"等待条件超时（{timeout.TotalSeconds}s）");
    }

    [Test]
    public void CaptureScreenshotWithoutCaptureSource_OnlyLogsAndKeepsViewModelState()
    {
        var capture = new FakeCaptureService();
        var log = new RecordingLogService();
        var coordinator = new TagEditorCoordinator(capture, new ThrowingDialogService(), log);
        coordinator.Bind(coordinator.ViewModel);

        try
        {
            // 场景 1：视频源未连接——经 ViewModel 命令 → 事件 → 协调器守卫，只记日志
            coordinator.ViewModel.CaptureScreenshotCommand.Execute(null);
            WaitUntil(() => log.Messages.Contains("请先连接视频源"), TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(log.Messages, Has.Count.EqualTo(1), "未连接时只应记一条守卫日志");
                Assert.That(coordinator.ViewModel.SourcePng, Is.Null, "未连接时不得写入截图");
                Assert.That(coordinator.IsCaptureConnected, Is.False);
            });

            // 场景 2：已连接但 AcquireLatestFrame 返回 null——同样只记日志、不抛异常
            capture.IsConnected = true;
            coordinator.ViewModel.CaptureScreenshotCommand.Execute(null);
            WaitUntil(() => log.Messages.Contains("截图失败：无法获取视频帧"), TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(log.Messages, Has.Count.EqualTo(2), "无帧时只应再记一条失败日志");
                Assert.That(coordinator.ViewModel.SourcePng, Is.Null, "无帧时不得写入截图");
            });
        }
        finally
        {
            coordinator.Dispose();
        }
    }
}
