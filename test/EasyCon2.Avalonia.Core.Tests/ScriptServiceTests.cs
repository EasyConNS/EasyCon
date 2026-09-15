using EasyCon.Capture;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Core.Services;
using EasyDevice;
using EasyScript;
using IDeviceService = EasyCon2.Avalonia.Core.Services.IDeviceService;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// ScriptService 冒烟测试：编译失败诊断、需求门控、Stop 终止与 IsRunningChanged 事件。
/// 设备/采集/日志依赖均以 fake 就地实现，不触达真实硬件。
/// </summary>
[TestFixture]
public class ScriptServiceTests
{
    // ── fakes ────────────────────────────────

    private sealed class FakeLogService : ILogService
    {
        private readonly object _lock = new();
        private readonly List<string> _messages = [];

        public event Action<string?, string?>? LogAppended
        {
            add { }
            remove { }
        }

        public void AddLog(string message, string? color = null)
        {
            lock (_lock)
            {
                _messages.Add(message);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _messages.Clear();
            }
        }

        public List<string> Snapshot()
        {
            lock (_lock)
            {
                return [.. _messages];
            }
        }

        public void Print(string message, bool newline = true) => AddLog(message);

        public void Alert(string message) => AddLog(message);

        public string ReadLine() => "";

        public bool TryReadLine(out string line)
        {
            line = "";
            return false;
        }
    }

    private sealed class FakeDeviceService : IDeviceService
    {
        private readonly object _lock = new();
        private readonly List<bool> _runningEvents = [];

        public bool IsConnected => false;
        public bool ShowDebugInfo { get; set; }
        public int AutoConnectCalls { get; private set; }
        public int ResetCalls { get; private set; }

        public event Action? ConnectionLost
        {
            add { }
            remove { }
        }

        public void RecordRunningEvent(bool running)
        {
            lock (_lock)
            {
                _runningEvents.Add(running);
            }
        }

        public List<bool> SnapshotRunningEvents()
        {
            lock (_lock)
            {
                return [.. _runningEvents];
            }
        }

        public string[] GetAvailablePorts() => [];

        public bool TryConnect(string port) => false;

        public string? AutoConnect()
        {
            AutoConnectCalls++;
            return null;
        }

        public void Disconnect()
        {
        }

        public NintendoSwitch GetDevice() => new NintendoSwitch();

        public void Reset() => ResetCalls++;

        public bool RemoteStart() => false;

        public bool RemoteStop() => false;

        public bool Flash(byte[] asmBytes) => false;

        public int GetVersion() => -1;

        public bool UnPair() => false;
    }

    private sealed class FakeCaptureService : ICaptureService
    {
        private readonly object _lock = new();
        private readonly List<(int Width, int Height)> _setPropertiesCalls = [];

        public bool IsConnected => false;
        public string CaptureType { get; set; } = "ANY";

        public event Action? ConnectionLost
        {
            add { }
            remove { }
        }

        public event Action? ConnectionRestored
        {
            add { }
            remove { }
        }

        public List<(int Width, int Height)> SnapshotSetPropertiesCalls()
        {
            lock (_lock)
            {
                return [.. _setPropertiesCalls];
            }
        }

        public string[] GetAvailableSources() => [];

        public bool TryConnect(string sourceName) => false;

        public void Disconnect()
        {
        }

        public FrameLease? AcquireLatestFrame() => null;

        public void SetCaptureProperties(int width, int height)
        {
            lock (_lock)
            {
                _setPropertiesCalls.Add((width, height));
            }
        }
    }

    private static ScriptService CreateService(FakeLogService log, FakeDeviceService device, FakeCaptureService capture)
    {
        return new ScriptService(device, capture, log);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string timeoutMessage, int timeoutMs = 15000, Func<string>? detail = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
        var detailText = detail != null ? $"，实际日志: {detail()}" : "";
        Assert.That(condition(), Is.True, timeoutMessage + detailText);
    }

    // ── 测试 ────────────────────────────────

    [Test]
    public async Task CompileAsync_WhenScriptHasError_ReturnsFalseAndLogsLineAndMessage()
    {
        var log = new FakeLogService();
        var service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());

        var ok = await service.CompileAsync("BREAK", null);

        var messages = log.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False, "含错误的脚本应编译失败");
            Assert.That(messages, Does.Contain("开始编译..."));
            Assert.That(messages.Any(m => m.StartsWith("行 ") && m.Contains(": ")), Is.True,
                $"日志应包含行号与错误消息，实际: {string.Join(" | ", messages)}");
            Assert.That(messages.Any(m => m.Contains("BREAK")), Is.True,
                $"错误消息应包含出错内容，实际: {string.Join(" | ", messages)}");
        });
    }

    [Test]
    public async Task RunFromContent_WhenDeviceNotConnected_LogsBlockReasonAndDoesNotRun()
    {
        var log = new FakeLogService();
        var device = new FakeDeviceService();
        var capture = new FakeCaptureService();
        var service = CreateService(log, device, capture);

        service.RunFromContent("A");

        await WaitUntilAsync(() => !service.IsRunning, "脚本应在门控失败后结束运行");

        var messages = log.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(messages, Does.Contain("❌ 脚本包含按键操作，需要连接单片机"));
            Assert.That(messages, Does.Not.Contain("脚本运行完成"), "需求未满足时脚本不应执行");
            Assert.That(device.AutoConnectCalls, Is.Zero, "门控失败时不应尝试自动连接");
            Assert.That(capture.SnapshotSetPropertiesCalls(), Is.Empty, "门控失败时不应设置采集参数");
        });
    }

    [Test]
    public async Task Stop_CancelsRunningScript_AndRaisesIsRunningChangedFalse()
    {
        var log = new FakeLogService();
        var device = new FakeDeviceService();
        var service = CreateService(log, device, new FakeCaptureService());
        service.IsRunningChanged += running => device.RecordRunningEvent(running);

        service.RunFromContent("""
            $count = 0
            WHILE $count < 100
            WAIT 100
            $count += 1
            END
            """);

        Assert.That(service.IsRunning, Is.True, "RunFromContent 返回后应处于运行中");
        await WaitUntilAsync(() => log.Snapshot().Contains("编译完成"), "后台编译应已完成", detail: () => string.Join(" | ", log.Snapshot()));
        Assert.That(device.SnapshotRunningEvents(), Does.Contain(true), "启动时应触发 IsRunningChanged(true)");

        service.Stop();

        await WaitUntilAsync(() => !service.IsRunning, "Stop 后脚本应终止");
        var messages = log.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(device.SnapshotRunningEvents(), Does.Contain(false), "终止时应触发 IsRunningChanged(false)");
            Assert.That(messages, Does.Contain("脚本已终止"));
            Assert.That(device.ResetCalls, Is.GreaterThanOrEqualTo(1), "终止时应释放按键");
        });
    }
}
