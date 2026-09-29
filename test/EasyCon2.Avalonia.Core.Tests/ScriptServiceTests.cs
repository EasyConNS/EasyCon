using EasyCon.Capture;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Core.Services;
using EasyDevice;
using EasyDevice.Connection;
using EasyScript;
using System.Collections.Concurrent;
using IDeviceService = EasyCon2.Avalonia.Core.Services.IDeviceService;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// ScriptService 冒烟测试：编译失败诊断、需求门控、Stop 终止与 IsRunningChanged 事件。
/// 设备/采集/日志依赖均以 fake 就地实现，不触达真实硬件。
/// </summary>
[TestFixture]
public class ScriptServiceTests
{
    private List<string> _scriptDirectories = [];

    [SetUp]
    public void Setup()
    {
        _scriptDirectories = [];
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string directory in _scriptDirectories)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

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
        private readonly NintendoSwitch _device;
        private readonly object _lock = new();
        private readonly List<bool> _runningEvents = [];

        public bool IsConnected => _device.IsConnected();
        public bool ShowDebugInfo { get; set; }
        public int AutoConnectCalls { get; private set; }
        public int ResetCalls { get; private set; }

        public FakeDeviceService(NintendoSwitch? device = null)
        {
            _device = device ?? new NintendoSwitch();
        }

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

        public bool TryConnect(string port) => _device.TryConnect(port) == NintendoSwitch.ConnectResult.Success;

        public string? AutoConnect()
        {
            AutoConnectCalls++;
            return null;
        }

        public void Disconnect()
        {
            _device.Disconnect();
        }

        public NintendoSwitch GetDevice() => _device;

        public void Reset()
        {
            ResetCalls++;
            _device.Reset();
        }

        public bool RemoteStart() => false;

        public bool RemoteStop() => false;

        public bool Flash(byte[] asmBytes) => false;

        public int GetVersion() => -1;

        public bool UnPair() => false;
    }

    private sealed class RecordingConnection : IConnection
    {
        private readonly BlockingCollection<byte[]> _writes = new();
        private readonly string _port = "fake://script-service";

        public override event BytesTransferedHandler BytesSent = delegate { };
        public override event BytesTransferedHandler BytesReceived = delegate { };
        public override event StatusChangedHandler StatusChanged = delegate { };

        public override Status CurrentStatus { get; protected set; } = Status.Connecting;

        public bool TryTake(out byte[] bytes, int timeoutMs)
        {
            if (_writes.TryTake(out byte[]? packet, timeoutMs))
            {
                bytes = packet;
                return true;
            }
            bytes = [];
            return false;
        }

        public override void Connect()
        {
            CurrentStatus = Status.Connected;
            StatusChanged?.Invoke(Status.Connected);
        }

        public override void Disconnect()
        {
            CurrentStatus = Status.Connecting;
        }

        public override void Write(params byte[] val)
        {
            byte[] packet = val.ToArray();
            BytesSent?.Invoke(_port, packet);
            _writes.Add(packet);
        }

        public override void ClearQueue()
        {
            while (_writes.TryTake(out _))
            {
            }
        }
    }

    private sealed class TestNintendoSwitch(RecordingConnection connection) : NintendoSwitch
    {
        protected override IConnection CreateConnection(string connStr, int baudrate) => connection;
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

        public Task DisconnectAsync() => Task.CompletedTask;

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

    private string CreateScriptFile(string fileName, string contents)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ScriptService_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _scriptDirectories.Add(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, contents);
        return path;
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
    public async Task Run_FromFileWithKeyActionAndDeviceDisconnected_UsesCompiledRequirements()
    {
        var log = new FakeLogService();
        var device = new FakeDeviceService();
        var service = CreateService(log, device, new FakeCaptureService());
        string path = CreateScriptFile("keys.ecs", "FOR 200\nA\nNEXT\n");

        service.Run(path);

        await WaitUntilAsync(() => !service.IsRunning, "文件脚本在设备门控失败后应结束运行");

        List<string> messages = log.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(service.HasKeyAction, Is.True, "文件会话的按键需求必须反映在服务状态中");
            Assert.That(messages, Does.Contain("❌ 脚本包含按键操作，需要连接单片机"));
            Assert.That(messages, Does.Not.Contain("脚本运行完成"));
            Assert.That(device.ResetCalls, Is.GreaterThanOrEqualTo(1));
        });
    }

    [Test]
    public async Task Run_AfterCompilingWait_UsesFileKeyRequirements()
    {
        var log = new FakeLogService();
        var device = new FakeDeviceService();
        var service = CreateService(log, device, new FakeCaptureService());
        string path = CreateScriptFile("keys.ecs", "FOR 200\nA\nNEXT\n");
        Assert.That(await service.CompileAsync("WAIT 0", null), Is.True);
        Assert.That(service.HasKeyAction, Is.False);

        service.Run(path);
        await WaitUntilAsync(() => !service.IsRunning, "文件脚本在设备门控失败后应结束运行");

        Assert.Multiple(() =>
        {
            Assert.That(service.HasKeyAction, Is.True, "不能沿用先前 WAIT 编译的需求");
            Assert.That(log.Snapshot(), Does.Contain("❌ 脚本包含按键操作，需要连接单片机"));
        });
    }

    [Test]
    public async Task Run_AfterCompilingKeyAction_PureWaitFileDoesNotRequireDevice()
    {
        var log = new FakeLogService();
        var service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());
        string path = CreateScriptFile("wait.ecs", "WAIT 0\n");
        Assert.That(await service.CompileAsync("A", null), Is.True);
        Assert.That(service.HasKeyAction, Is.True);

        service.Run(path);
        await WaitUntilAsync(() => !service.IsRunning, "纯等待文件应执行完成");

        Assert.Multiple(() =>
        {
            Assert.That(service.HasKeyAction, Is.False, "不能沿用先前按键脚本的需求");
            Assert.That(log.Snapshot(), Does.Contain("脚本运行完成"));
            Assert.That(log.Snapshot(), Does.Not.Contain("❌ 脚本包含按键操作，需要连接单片机"));
        });
    }

    [Test]
    public async Task Run_FileCompileFailureAfterSuccessfulCompile_ClearsOldSession()
    {
        var log = new FakeLogService();
        var service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());
        string path = CreateScriptFile("broken.ecs", "BREAK\n");
        Assert.That(await service.CompileAsync("A", null), Is.True);

        service.Run(path);
        await WaitUntilAsync(() => !service.IsRunning, "编译失败的文件不应启动旧会话");

        Assert.Multiple(() =>
        {
            Assert.That(service.HasKeyAction, Is.False, "失败编译后应清空旧会话状态");
            Assert.That(log.Snapshot(), Does.Not.Contain("脚本运行完成"));
            Assert.That(log.Snapshot().Any(m => m.Contains("broken.ecs:1:") && m.Contains("BREAK")), Is.True,
                string.Join(" | ", log.Snapshot()));
        });
    }

    [Test]
    public async Task CompileAsync_WithFileName_UsesUnsavedTextAndRootLibContext()
    {
        var log = new FakeLogService();
        var service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());
        string path = CreateScriptFile("main.ecs", "BREAK\n");
        string libraryPath = Path.Combine(Path.GetDirectoryName(path)!, "lib", "answer.ecs");
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        File.WriteAllText(libraryPath, "FUNC answer():INT\n    RETURN 42\nENDFUNC\n");
        const string editorText = "$r = answer()\nPRINT $r\n";

        bool compiled = await service.CompileAsync(editorText, path);

        Assert.Multiple(() =>
        {
            Assert.That(compiled, Is.True, string.Join(" | ", log.Snapshot()));
            Assert.That(service.GetFormattedCode(), Does.Contain("answer()"));
            Assert.That(log.Snapshot(), Does.Not.Contain("行 1: BREAK"), "不应读取磁盘上的旧脚本内容");
        });

        service.RunFromContent(editorText, fileName: path);
        await WaitUntilAsync(() => !service.IsRunning, "带路径的编辑器内容应运行完成");
        Assert.That(log.Snapshot(), Does.Contain("42"), "运行时仍应能调用同目录自动库");

        string labelsDirectory = Path.Combine(Path.GetDirectoryName(path)!, "ImgLabel");
        new ImgLabel { name = "enemy", searchMethod = SearchMethod.TesserDetect }.Save(labelsDirectory);
        Assert.That(await service.CompileAsync("PRINT @enemy", path), Is.True,
            "带路径的编辑器编译应从脚本目录加载图像标签并设置 ExtVars");
    }

    [Test]
    public async Task CompileAsync_ReportsLibraryFileAndLine()
    {
        var log = new FakeLogService();
        var service = CreateService(log, new FakeDeviceService(), new FakeCaptureService());
        string path = CreateScriptFile("main.ecs", "PRINT 1\n");
        string libraryPath = Path.Combine(Path.GetDirectoryName(path)!, "lib", "00_bad.ecs");
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        File.WriteAllText(libraryPath, "$r = missing()\n");

        bool compiled = await service.CompileAsync(File.ReadAllText(path), path);

        Assert.That(compiled, Is.False);
        Assert.That(log.Snapshot().Any(m => m.Replace('\\', '/').Contains("lib/00_bad.ecs:1:")), Is.True,
            string.Join(" | ", log.Snapshot()));
    }

    [Test]
    public async Task RunFromContent_ForTwoHundredAActions_WritesPressAndReleaseHidReports()
    {
        var connection = new RecordingConnection();
        var switchDevice = new TestNintendoSwitch(connection);
        Assert.That(switchDevice.TryConnect("fake://script-service"),
            Is.EqualTo(NintendoSwitch.ConnectResult.Success));
        var log = new FakeLogService();
        var service = CreateService(log, new FakeDeviceService(switchDevice), new FakeCaptureService());
        var packets = new ConcurrentQueue<byte[]>();
        byte[] pressedA = new SwitchReport { Button = (ushort)SwitchButton.A }.GetBytes();
        byte[] released = new SwitchReport().GetBytes();
        void RecordSentPacket(string _, byte[] packet) => packets.Enqueue(packet.ToArray());
        connection.BytesSent += RecordSentPacket;
        const int pressDurationMs = 200;

        try
        {
            // 留出多个设备写循环周期，避免 CI runner 调度抖动让短按状态被合并。
            service.RunFromContent($"FOR 200\nA {pressDurationMs}\nNEXT\n");
            await WaitUntilAsync(() => !service.IsRunning, "按键循环应执行完成", timeoutMs: 120000,
                detail: () => string.Join(" | ", log.Snapshot()));
            await WaitUntilAsync(() => HasCompleteHidReportSequence(packets, pressedA, released),
                "设备写入队列应收到完整的 200 次按下和最后释放报告", timeoutMs: 5000,
                detail: () => DescribeHidReports(packets, pressedA, released));
        }
        finally
        {
            connection.BytesSent -= RecordSentPacket;
            switchDevice.Disconnect();
        }

        byte[][] hidReports = packets.ToArray();
        int lastPress = Array.FindLastIndex(hidReports, packet => packet.SequenceEqual(pressedA));
        int lastRelease = Array.FindLastIndex(hidReports, packet => packet.SequenceEqual(released));
        Assert.Multiple(() =>
        {
            Assert.That(hidReports.Count(packet => packet.SequenceEqual(pressedA)), Is.GreaterThanOrEqualTo(200),
                "必须从设备写循环收到 200 个 A 按下 HID 报告");
            Assert.That(lastRelease, Is.GreaterThan(lastPress),
                $"最后一次 A 按下后必须收到释放 HID 报告；按下数={hidReports.Count(packet => packet.SequenceEqual(pressedA))}，释放数={hidReports.Count(packet => packet.SequenceEqual(released))}");
            Assert.That(log.Snapshot(), Does.Contain("脚本运行完成"));
        });
    }

    private static bool HasCompleteHidReportSequence(ConcurrentQueue<byte[]> packets, byte[] pressedA, byte[] released)
    {
        byte[][] reports = packets.ToArray();
        int pressCount = reports.Count(packet => packet.SequenceEqual(pressedA));
        int lastPress = Array.FindLastIndex(reports, packet => packet.SequenceEqual(pressedA));
        int lastRelease = Array.FindLastIndex(reports, packet => packet.SequenceEqual(released));
        return pressCount >= 200 && lastRelease > lastPress;
    }

    private static string DescribeHidReports(ConcurrentQueue<byte[]> packets, byte[] pressedA, byte[] released)
    {
        byte[][] reports = packets.ToArray();
        int pressCount = reports.Count(packet => packet.SequenceEqual(pressedA));
        int releaseCount = reports.Count(packet => packet.SequenceEqual(released));
        int lastPress = Array.FindLastIndex(reports, packet => packet.SequenceEqual(pressedA));
        int lastRelease = Array.FindLastIndex(reports, packet => packet.SequenceEqual(released));
        return $"按下数={pressCount}，释放数={releaseCount}，最后按下索引={lastPress}，最后释放索引={lastRelease}";
    }

    [Test]
    public async Task StopWhileButtonIsHeld_WritesReleaseReportAndAllowsAnotherRun()
    {
        var connection = new RecordingConnection();
        var switchDevice = new TestNintendoSwitch(connection);
        Assert.That(switchDevice.TryConnect("fake://script-service"),
            Is.EqualTo(NintendoSwitch.ConnectResult.Success));
        var log = new FakeLogService();
        var service = CreateService(log, new FakeDeviceService(switchDevice), new FakeCaptureService());
        var packets = new ConcurrentQueue<byte[]>();
        using var stopCollector = new CancellationTokenSource();
        Task collector = Task.Run(() =>
        {
            while (!stopCollector.IsCancellationRequested)
            {
                if (connection.TryTake(out byte[] packet, 100))
                    packets.Enqueue(packet);
            }
        });

        try
        {
            service.RunFromContent("A DOWN\nWAIT 10000\nA UP\n");
            byte[] pressedA = new SwitchReport { Button = (ushort)SwitchButton.A }.GetBytes();
            await WaitUntilAsync(() => packets.Any(packet => packet.SequenceEqual(pressedA)),
                "设备写循环应发送按下报告");
            service.Stop();
            await WaitUntilAsync(() => !service.IsRunning, "停止后运行状态应复位");
            byte[] released = new SwitchReport().GetBytes();
            await WaitUntilAsync(() => packets.Any(packet => packet.SequenceEqual(released)),
                "停止时应发送释放报告", timeoutMs: 5000);

            service.RunFromContent("WAIT 0");
            await WaitUntilAsync(() => !service.IsRunning, "复位后应能启动第二次脚本");
        }
        finally
        {
            await stopCollector.CancelAsync();
            await collector;
            switchDevice.Disconnect();
        }

        Assert.That(log.Snapshot(), Does.Contain("脚本已终止"));
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

    [Test]
    public async Task RunFromContent_PureWaitScript_CompletesAndResetsIsRunning()
    {
        var log = new FakeLogService();
        var device = new FakeDeviceService();
        var service = CreateService(log, device, new FakeCaptureService());

        service.RunFromContent("WAIT 100");

        await WaitUntilAsync(() => !service.IsRunning, "脚本完成后 IsRunning 应复位", detail: () => string.Join(" | ", log.Snapshot()));
        Assert.That(log.Snapshot(), Does.Contain("脚本运行完成"));
    }

    [Test]
    public async Task RunThenImmediateStop_AlwaysResetsIsRunning()
    {
        // 回归：此前 token 传给 Task.Run，启动瞬间取消会让委托体不执行，
        // finally 丢失 → IsRunning 永久卡死
        var log = new FakeLogService();
        var device = new FakeDeviceService();
        var service = CreateService(log, device, new FakeCaptureService());

        service.RunFromContent("WAIT 10000");
        service.Stop();

        await WaitUntilAsync(() => !service.IsRunning, "启动后立即停止，IsRunning 也必须复位", detail: () => string.Join(" | ", log.Snapshot()));
        Assert.That(device.ResetCalls, Is.GreaterThanOrEqualTo(1), "停止路径必须触发设备 Reset");
    }

    [Test]
    public async Task RunFromContent_WhileAlreadyRunning_IsRejectedAndAllowsRestartAfterFinish()
    {
        var log = new FakeLogService();
        var device = new FakeDeviceService();
        var service = CreateService(log, device, new FakeCaptureService());

        service.RunFromContent("WAIT 10000");
        await WaitUntilAsync(() => service.IsRunning, "第一个脚本应进入运行状态");

        service.RunFromContent("WAIT 10000");

        Assert.That(log.Snapshot(), Does.Contain("脚本已在运行中，忽略本次启动请求"), "运行中重复启动应被拒绝");

        service.Stop();
        await WaitUntilAsync(() => !service.IsRunning, "停止后 IsRunning 应复位");

        service.RunFromContent("WAIT 50");
        await WaitUntilAsync(() => !service.IsRunning, "复位后应能再次启动并正常完成", detail: () => string.Join(" | ", log.Snapshot()));
        Assert.That(log.Snapshot(), Does.Contain("脚本运行完成"));
    }
}