using EasyCon.Capture;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Maa;
using EasyDevice;
using EasyDevice.Connection;
using EasyScript;
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace EasyCon.Tests.Maa;

[TestFixture]
public sealed class MaaHostTests
{
    private static void Trace(TextWriter writer, string kind, object data)
    {
        lock (writer) writer.WriteLine("P1_EVENT " + System.Text.Json.JsonSerializer.Serialize(new
        {
            kind,
            ticks = Stopwatch.GetTimestamp(),
            frequency = Stopwatch.Frequency,
            data,
        }));
    }

    private static void Snapshot(MaaControllerSession controller, FrameHub hub, FrameLease? frame = null,
        ControllerRunLease? run = null)
        => Trace(TestContext.Out, "module-snapshot", new
        {
            controller.InputEndpoint,
            controller.InputEpoch,
            controller.ConnectionEpoch,
            controller.InputConnected,
            hub.SourceId,
            hub.SourceEpoch,
            backend = hub.Backend?.ToString() ?? "controlled-test-source",
            hub.CaptureConnected,
            hub.ConsumerCount,
            hub.HeldByUser,
            runId = run?.RunId,
            bindingInput = run?.Binding.RequiresInput,
            bindingCapture = run?.Binding.RequiresCapture,
            frame = frame?.Metadata,
        });

    private sealed class Connection : IConnection
    {
        private readonly TextWriter _trace = TestContext.Out;
        private string? _endpoint;
        public readonly ConcurrentQueue<byte[]> Writes = new();
        public readonly ConcurrentQueue<bool> WriterUsesThreadPool = new();
        public int ConnectCount;
        public override event BytesTransferedHandler BytesSent = delegate { };
        public override event StatusChangedHandler StatusChanged = delegate { };
        public override Status CurrentStatus { get; protected set; } = Status.Connecting;
        public void Select(string endpoint) { _endpoint = endpoint; Trace(_trace, "input-selected", new { endpoint }); }
        public override void Connect()
        {
            ConnectCount++;
            CurrentStatus = Status.Connected;
            Trace(_trace, "input-connect", new { endpoint = _endpoint, ConnectCount });
            StatusChanged(Status.Connected);
        }
        public override void Disconnect()
        {
            CurrentStatus = Status.Connecting;
            Trace(_trace, "input-disconnect", new { endpoint = _endpoint });
        }
        public override void ClearQueue() { }
        public override void Write(params byte[] bytes)
        {
            WriterUsesThreadPool.Enqueue(Thread.CurrentThread.IsThreadPoolThread);
            Writes.Enqueue(bytes.ToArray());
            Trace(_trace, "hid-written", new { endpoint = _endpoint, bytes = Convert.ToHexString(bytes) });
            BytesSent("fake", bytes);
        }
        public void Lose() { CurrentStatus = Status.Error; StatusChanged(Status.Error); }
    }

    private sealed class Device(Connection connection) : NintendoSwitch
    {
        protected override IConnection CreateConnection(string connStr, int baudrate)
        {
            connection.Select(connStr);
            return connection;
        }
    }

    private sealed class Source(byte blue = 11) : IFrameSource
    {
        private readonly TextWriter _trace = TestContext.Out;
        public bool IsOpened { get; set; } = true;
        public int Reads;
        public bool Disposed;
        public Mat GetMatFrame()
        {
            if (Interlocked.Increment(ref Reads) == 1) Trace(_trace, "capture-read-started", new { blue });
            Thread.Sleep(15);
            return new Mat(36, 64, MatType.CV_8UC3, new Scalar(blue, 27, 93));
        }
        public void SetProperties(int width, int height) { }
        public void Dispose() { Disposed = true; Trace(_trace, "capture-disposed", new { blue, Reads }); }
    }

    private sealed class MappedDevice(Dictionary<string, Connection> connections) : NintendoSwitch
    {
        protected override IConnection CreateConnection(string connStr, int baudrate)
        {
            Connection connection = connections[connStr];
            connection.Select(connStr);
            return connection;
        }
    }

    [Test]
    public async Task DeviceWriter_UsesDedicatedThread_AndReconnectDrainsOldLoop()
    {
        Connection connection = new();
        NintendoSwitch device = new Device(connection);
        try
        {
            for (int i = 0; i < 2; i++)
            {
                Assert.That(device.TryConnect("fake:p1-writer"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
                Assert.That(await device.ResetAndWaitForWriteAsync(TimeSpan.FromSeconds(3)), Is.True);
                Assert.That(connection.WriterUsesThreadPool, Is.Not.Empty);
                Assert.That(connection.WriterUsesThreadPool.All(usesPool => !usesPool), Is.True,
                    "长期阻塞的写循环不能等待线程池补线程，否则短按可在写出前被释放覆盖");
            }
        }
        finally { device.Disconnect(); }
    }

    [Test]
    public void LateStatusFromOldConnection_CannotCancelNewConnection()
    {
        Connection oldConnection = new();
        Connection newConnection = new();
        using MaaControllerSession controller = new(new MappedDevice(new()
        {
            ["fake:old-connection"] = oldConnection,
            ["fake:new-connection"] = newConnection,
        }));
        Assert.That(controller.TryConnect("fake:old-connection"), Is.EqualTo(ControllerConnectResult.Success));
        Assert.That(controller.TryConnect("fake:new-connection"), Is.EqualTo(ControllerConnectResult.Success));
        oldConnection.Lose();
        Assert.That(controller.InputConnected, Is.True);
        Assert.That(controller.InputEndpoint, Is.EqualTo("fake:new-connection"));
        controller.ApplyReport(new SwitchReport { Button = (ushort)SwitchButton.B });
        WaitFor(newConnection, new SwitchReport { Button = (ushort)SwitchButton.B });
    }

    [Test]
    public async Task TwoInputEndpointsAndTwoSources_AllFourCombinationsStayIndependent()
    {
        Dictionary<string, Connection> connections = new()
        {
            ["fake:p1-COM3"] = new(),
            ["fake:p1-COM5"] = new(),
        };
        using MaaControllerSession controller = new(new MappedDevice(connections));
        using FrameHub hub = new();
        foreach (string endpoint in connections.Keys)
        {
            Assert.That(controller.TryConnect(endpoint), Is.EqualTo(ControllerConnectResult.Success));
            foreach ((string name, byte blue) in new[] { ("A", (byte)12), ("B", (byte)220) })
            {
                long inputEpoch = controller.InputEpoch;
                hub.Connect(new Source(blue), name);
                using FrameLease frame = await hub.WaitForFrameAfterAsync(hub.Store.FrameCount, 0, new CancellationTokenSource(5000).Token);
                Assert.That(frame.Metadata.SourceId, Is.EqualTo(name));
                Assert.That(frame.Mat.At<Vec3b>(0, 0).Item0, Is.EqualTo(blue));
                Assert.That(controller.InputEndpoint, Is.EqualTo(endpoint));
                Assert.That(controller.InputEpoch, Is.EqualTo(inputEpoch));
                int otherCount = connections.Where(p => p.Key != endpoint).Single().Value.Writes.Count;
                controller.ApplyReport(new SwitchReport { Button = (ushort)SwitchButton.A });
                WaitFor(connections[endpoint], new SwitchReport { Button = (ushort)SwitchButton.A });
                Assert.That(connections.Where(p => p.Key != endpoint).Single().Value.Writes.Count, Is.EqualTo(otherCount));
                Snapshot(controller, hub, frame);
                controller.Reset();
            }
        }
    }

    [Test]
    [Category("MaaLifetime")]
    public async Task ThousandRuns_ReleaseNativeBindingsAndFrameLeases()
    {
        int initial = MaaControllerBinding.ActiveNativeBindings;
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        Assert.That(controller.TryConnect("fake:p1-lifetime"), Is.EqualTo(ControllerConnectResult.Success));
        List<double> elapsed = [];
        List<object> samples = [];
        List<long> privateMemory = [];
        for (int i = 0; i < 1000; i++)
        {
            Stopwatch timer = Stopwatch.StartNew();
            ControllerRunLease run = controller.BeginRun("lifetime-" + i, default);
            controller.CreatePad(DelayType.LowCPU, run).PressButtons(GamePadKey.A);
            if ((i & 1) == 0) run.Cancel();
            Assert.That(await run.StopAsync(), Is.EqualTo(CleanupStatus.Written));
            elapsed.Add(timer.Elapsed.TotalMilliseconds);
            Assert.That(MaaControllerBinding.ActiveNativeBindings, Is.EqualTo(initial));
            if (i % 100 == 0 || i == 999)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                using Process process = Process.GetCurrentProcess();
                privateMemory.Add(process.PrivateMemorySize64);
                samples.Add(new
                {
                    cycle = i + 1,
                    process.WorkingSet64,
                    process.PrivateMemorySize64,
                    threads = process.Threads.Count,
                    managedBytes = GC.GetTotalMemory(false),
                    heapBytes = GC.GetGCMemoryInfo().HeapSizeBytes,
                    nativeBindings = MaaControllerBinding.ActiveNativeBindings,
                    FrameStore.ActiveSlots,
                    FrameStore.ActiveLeases
                });
                TestContext.Progress.WriteLine($"Maa lifetime {i + 1}/1000: managed={GC.GetTotalMemory(false)}, nativeBindings={MaaControllerBinding.ActiveNativeBindings}");
            }
        }
        elapsed.Sort();
        Assert.That(privateMemory[^1] - privateMemory[2], Is.LessThan(32L * 1024 * 1024),
            "bootstrap 在预热后不应继续逐轮累积原生设备/推理资源（采样均在完整 GC 后）");
        TestContext.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            runtime = MaaEnvironment.RuntimeVersion,
            cycles = elapsed.Count,
            p50 = elapsed[499],
            p95 = elapsed[949],
            p99 = elapsed[989],
            max = elapsed[^1],
            samples,
        }));
        Assert.That(connection.Writes.Last(), Is.EqualTo(new SwitchReport().GetBytes()));
    }

    [Test]
    public async Task ReadOnlyStop_DoesNotReleaseAnotherRunsInput_AndFramesKeepUpdating()
    {
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        using FrameHub hub = new();
        hub.Connect(new Source(), "source-A");
        using FrameLease initial = await hub.WaitForFrameAfterAsync(0, 0, new CancellationTokenSource(5000).Token);
        Assert.That(controller.TryConnect("fake:p1-preview"), Is.EqualTo(ControllerConnectResult.Success));
        using ControllerRunLease run = controller.BeginRun("input", default);
        IPadInput pad = controller.CreatePad(DelayType.LowCPU, run);
        pad.PressButtons(GamePadKey.A);
        WaitFor(connection, new SwitchReport { Button = (ushort)SwitchButton.A });
        int writes = connection.Writes.Count;
        long frameId = hub.Store.FrameCount;
        using (MaaControllerBinding preview = MaaControllerBinding.CaptureOnly(hub))
        {
            _ = preview.Screenshot();
            preview.Stop();
        }
        Assert.That(connection.Writes.Count, Is.EqualTo(writes), "只读 Stop/Inactive/Dispose 不得发送输入");
        using FrameLease next = await hub.WaitForFrameAfterAsync(frameId, 0, new CancellationTokenSource(5000).Token);
        Assert.That(next.Metadata.SourceEpoch, Is.EqualTo(initial.Metadata.SourceEpoch));
        Assert.That(controller.InputConnected, Is.True);
        Snapshot(controller, hub, next, run);
    }

    [Test]
    public async Task CaptureLoss_CancelsOnlyDependentRun_AndConfirmsNeutralWithoutDisconnectingPad()
    {
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        using FrameHub hub = new();
        Source source = new();
        hub.Connect(source, "source-failure");
        using FrameLease initial = await hub.WaitForFrameAfterAsync(0, 0, new CancellationTokenSource(5000).Token);
        Assert.That(controller.TryConnect("fake:p1-capture-loss"), Is.EqualTo(ControllerConnectResult.Success));
        HostRunCoordinator coordinator = new(controller, hub);
        Task run = Task.Run(() => coordinator.Run("ecs", null, true, default, scope =>
        {
            scope.CreatePad().PressButtons(GamePadKey.A);
            scope.Token.WaitHandle.WaitOne();
            scope.Token.ThrowIfCancellationRequested();
        }, requiresCapture: true));
        WaitFor(connection, new SwitchReport { Button = (ushort)SwitchButton.A });
        source.IsOpened = false;
        Assert.ThrowsAsync<OperationCanceledException>(async () => await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(coordinator.LastResult!.ErrorCode, Is.EqualTo("CaptureLost"));
        Assert.That(coordinator.LastResult.BusinessStatus, Is.EqualTo("Failed"));
        Assert.That(coordinator.LastResult.CleanupStatus, Is.EqualTo(CleanupStatus.Written));
        Assert.That(connection.Writes.Last(), Is.EqualTo(new SwitchReport().GetBytes()));
        Assert.That(controller.InputConnected, Is.True);
        Snapshot(controller, hub, initial);
        Trace(TestContext.Out, "run-result", coordinator.LastResult);
    }

    [Test]
    public async Task InputLoss_DoesNotChangeCaptureEpoch_AndReleaseIsReportedUnconfirmed()
    {
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        using FrameHub hub = new();
        hub.Connect(new Source(), "capture-keep");
        using FrameLease initial = await hub.WaitForFrameAfterAsync(0, 0, new CancellationTokenSource(5000).Token);
        Assert.That(controller.TryConnect("fake:p1-input-loss"), Is.EqualTo(ControllerConnectResult.Success));
        HostRunCoordinator coordinator = new(controller, hub);
        Task run = Task.Run(() => coordinator.Run("ecs", null, true, default, scope =>
        {
            scope.CreatePad().PressButtons(GamePadKey.A);
            scope.Token.WaitHandle.WaitOne();
            scope.Token.ThrowIfCancellationRequested();
        }));
        WaitFor(connection, new SwitchReport { Button = (ushort)SwitchButton.A });
        connection.Lose();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await run.WaitAsync(TimeSpan.FromSeconds(5)));
        using FrameLease next = await hub.WaitForFrameAfterAsync(initial.Metadata.FrameId, 0, new CancellationTokenSource(5000).Token);
        Assert.That(next.Metadata.SourceEpoch, Is.EqualTo(initial.Metadata.SourceEpoch));
        Assert.That(coordinator.LastResult!.ErrorCode, Is.EqualTo("DeviceLost"));
        Assert.That(coordinator.LastResult.CleanupStatus, Is.EqualTo(CleanupStatus.ReleaseUnconfirmed));
        Assert.That(hub.CaptureConnected, Is.True);
        Snapshot(controller, hub, next);
        Trace(TestContext.Out, "run-result", coordinator.LastResult);
    }

    [Test]
    public async Task LongInput_DoesNotBlockProducer_AndCaptureSwitchDoesNotCancelInputOnlyRun()
    {
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        using FrameHub hub = new();
        hub.Connect(new Source(), "A");
        using FrameLease initial = await hub.WaitForFrameAfterAsync(0, 0, new CancellationTokenSource(5000).Token);
        Assert.That(controller.TryConnect("fake:p1-independent-switch"), Is.EqualTo(ControllerConnectResult.Success));
        using ControllerRunLease run = controller.BeginRun("keys-only", default);
        long inputEpoch = controller.InputEpoch;
        Task pulse = Task.Run(() => controller.CreatePad(DelayType.LowCPU, run).ClickButtons(GamePadKey.A, 30_000, run.Token));
        WaitFor(connection, new SwitchReport { Button = (ushort)SwitchButton.A });
        using FrameLease next = await hub.WaitForFrameAfterAsync(initial.Metadata.FrameId, 0, new CancellationTokenSource(5000).Token);
        Assert.That(hub.Disconnect(), Is.True);
        hub.Connect(new Source(), "B");
        using FrameLease changed = await hub.WaitForFrameAfterAsync(next.Metadata.FrameId, 0, new CancellationTokenSource(5000).Token);
        Assert.That(changed.Metadata.SourceEpoch, Is.GreaterThan(initial.Metadata.SourceEpoch));
        Assert.That(controller.InputEpoch, Is.EqualTo(inputEpoch));
        Assert.That(run.Token.IsCancellationRequested, Is.False);
        Snapshot(controller, hub, changed, run);
        run.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await pulse.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public async Task OldNativeKeyUpAndInactive_CannotChangeNewOwner()
    {
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        Assert.That(controller.TryConnect("fake:p1-stale"), Is.EqualTo(ControllerConnectResult.Success));
        ControllerRunLease old = controller.BeginRun("old", default);
        MaaControllerBinding stale = old.Binding;
        Assert.That(await old.StopAsync(), Is.EqualTo(CleanupStatus.Written));
        using ControllerRunLease current = controller.BeginRun("current", default);
        controller.CreatePad(DelayType.LowCPU, current).PressButtons(GamePadKey.A);
        WaitFor(connection, new SwitchReport { Button = (ushort)SwitchButton.A });
        int writes = connection.Writes.Count;
        Assert.That(stale.KeyForOwner(2, false), Is.False);
        Assert.That(stale.InactiveForOwner(), Is.False);
        Assert.That(connection.Writes.Count, Is.EqualTo(writes));
        Assert.That(controller.GetReport().Button, Is.EqualTo((ushort)SwitchButton.A));
    }

    [Test]
    public void ManagementCommands_AreRejectedWhileRunOwnsInput()
    {
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        Assert.That(controller.TryConnect("fake:p1-management"), Is.EqualTo(ControllerConnectResult.Success));
        using ControllerRunLease run = controller.BeginRun("owns-input", default);
        ControllerOperationException? error = Assert.Throws<ControllerOperationException>(() => controller.GetVersion());
        Assert.That(error!.ErrorCode, Is.EqualTo("Busy"));
    }

    [Test]
    public async Task InvalidMaaResource_DoesNotCloseEitherModule_AndDisposesPartialBinding()
    {
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        using FrameHub hub = new();
        Assert.That(controller.TryConnect("fake:p1-resource-failure"), Is.EqualTo(ControllerConnectResult.Success));
        hub.Connect(new Source(), "source-preserved");
        using FrameLease initial = await hub.WaitForFrameAfterAsync(0, 0, new CancellationTokenSource(5000).Token);
        int native = MaaControllerBinding.ActiveNativeBindings;
        int writes = connection.Writes.Count;
        using (MaaControllerBinding binding = new(null, null, hub,
            resourceDirectory: Path.Combine(Path.GetTempPath(), "missing-maa-resource-" + Guid.NewGuid().ToString("N"))))
        {
            Assert.Throws<InvalidDataException>(() => binding.Screenshot());
        }
        Assert.That(MaaControllerBinding.ActiveNativeBindings, Is.EqualTo(native));
        Assert.That(connection.Writes.Count, Is.EqualTo(writes));
        Assert.That(controller.InputConnected, Is.True);
        Assert.That(hub.CaptureConnected, Is.True);
        using FrameLease next = await hub.WaitForFrameAfterAsync(initial.Metadata.FrameId, 0, new CancellationTokenSource(5000).Token);
        Assert.That(next.Metadata.SourceEpoch, Is.EqualTo(initial.Metadata.SourceEpoch));
        Snapshot(controller, hub, next);
    }

    private static void WaitFor(Connection connection, SwitchReport report)
    {
        byte[] expected = report.GetBytes();
        Assert.That(SpinWait.SpinUntil(() => connection.Writes.Any(b => b.SequenceEqual(expected)), 3000), Is.True,
            "未收到实际出队 HID: " + Convert.ToHexString(expected));
    }

    [Test]
    public void InputOnly_UsesRealMaa_AndPreservesAtomicReports()
    {
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        Assert.That(controller.TryConnect("fake:p1-atomic"), Is.EqualTo(ControllerConnectResult.Success));
        SwitchReport report = new() { Button = 5, HAT = 3, LX = 17, LY = 88, RX = 201, RY = 44 };
        controller.ApplyReport(report);
        WaitFor(connection, report);
        Assert.That(connection.ConnectCount, Is.EqualTo(1));
        Assert.That(controller.LastCleanup, Is.EqualTo(CleanupStatus.Written));
    }

    [Test]
    public async Task LongPulse_CancelReleasesWritten_RejectsOtherOwners()
    {
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        Assert.That(controller.TryConnect("fake:p1-cancel"), Is.EqualTo(ControllerConnectResult.Success));
        using ControllerRunLease run = controller.BeginRun("test-run", default);
        IPadInput pad = controller.CreatePad(DelayType.LowCPU, run);
        Task pulse = Task.Run(() => pad.ClickButtons(GamePadKey.A, 30_000, run.Token));
        SwitchReport pressed = new();
        ECKeyUtil.Button(SwitchButton.A).Down(pressed);
        WaitFor(connection, pressed);
        Assert.Throws<ControllerOperationException>(() => controller.Down(ECKeyUtil.Button(SwitchButton.B)));
        Stopwatch watch = Stopwatch.StartNew();
        run.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await pulse.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.That(await run.StopAsync(), Is.EqualTo(CleanupStatus.Written));
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
        Assert.That(connection.Writes.Last(), Is.EqualTo(new SwitchReport().GetBytes()));
        Assert.Throws<ControllerOperationException>(() => pad.PressButtons(GamePadKey.A));
    }

    [Test]
    public async Task CaptureOnly_StopHasNoInputEffect_AndUsesSamePixels()
    {
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        using FrameHub hub = new();
        Source source = new();
        hub.Connect(source, "controlled-A");
        using FrameLease frame = await hub.WaitForFrameAfterAsync(0, 0, new CancellationTokenSource(5000).Token);
        using MaaControllerBinding screenshot = MaaControllerBinding.CaptureOnly(hub);
        Assert.That(screenshot.BindingReady, Is.True);
        Assert.That(screenshot.RequiresInput, Is.False);
        using Mat decoded = Cv2.ImDecode(screenshot.Screenshot(), ImreadModes.Color);
        Assert.That(decoded.Size(), Is.EqualTo(new Size(1920, 1080)));
        Assert.That(Cv2.Norm(decoded, frame.Mat), Is.Zero);
        screenshot.Stop();
        Assert.That(controller.InputConnected, Is.False);
        Assert.That(connection.ConnectCount, Is.Zero);
        Assert.That(connection.Writes, Is.Empty);
        Assert.That(hub.CaptureConnected, Is.True);
        Snapshot(controller, hub, frame);
    }

    [Test]
    public async Task SharedCaptureConsumers_StopIndividually_AndKeepUsersConnection()
    {
        Connection connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        using FrameHub hub = new();
        Source source = new();
        hub.Connect(source, "shared-source");
        using FrameLease first = await hub.WaitForFrameAfterAsync(0, 0, new CancellationTokenSource(5000).Token);
        MaaControllerBinding preview = MaaControllerBinding.CaptureOnly(hub);
        using MaaControllerBinding task = MaaControllerBinding.CaptureOnly(hub);
        Assert.That(hub.ConsumerCount, Is.EqualTo(2));
        _ = preview.Screenshot();
        _ = task.Screenshot();
        preview.Dispose();
        Assert.That(hub.ConsumerCount, Is.EqualTo(1));
        using FrameLease next = await hub.WaitForFrameAfterAsync(first.Metadata.FrameId, 0, new CancellationTokenSource(5000).Token);
        _ = task.Screenshot();
        task.Dispose();
        Assert.That(hub.ConsumerCount, Is.Zero);
        Assert.That(hub.HeldByUser, Is.True);
        Assert.That(source.Disposed, Is.False);
        Assert.That(next.Metadata.SourceEpoch, Is.EqualTo(first.Metadata.SourceEpoch));
        Assert.That(connection.ConnectCount, Is.Zero);
        Snapshot(controller, hub, next);
        Assert.That(hub.Disconnect(), Is.True);
        Assert.That(source.Disposed, Is.True);
    }

    [Test]
    public async Task SwitchingInput_WaitsForDependentRun_AndKeepsCaptureGeneration()
    {
        Connection firstConnection = new();
        Connection secondConnection = new();
        using MaaControllerSession controller = new(new MappedDevice(new()
        {
            ["fake:switch-first"] = firstConnection,
            ["fake:switch-second"] = secondConnection,
        }));
        using FrameHub hub = new();
        hub.Connect(new Source(), "unchanged-source");
        using FrameLease first = await hub.WaitForFrameAfterAsync(0, 0, new CancellationTokenSource(5000).Token);
        Assert.That(controller.TryConnect("fake:switch-first"), Is.EqualTo(ControllerConnectResult.Success));
        HostRunCoordinator coordinator = new(controller, hub);
        Task run = Task.Run(() => coordinator.Run("ecs", null, true, default, scope =>
        {
            scope.CreatePad().ClickButtons(GamePadKey.A, 30_000, scope.Token);
        }));
        WaitFor(firstConnection, new SwitchReport { Button = (ushort)SwitchButton.A });
        Assert.That(controller.TryConnect("fake:switch-second"), Is.EqualTo(ControllerConnectResult.Success));
        Assert.That(run.IsCompleted, Is.True, "切换应等旧运行释放后再连接新端点");
        Assert.ThrowsAsync<OperationCanceledException>(async () => await run);
        Assert.That(coordinator.LastResult!.ErrorCode, Is.EqualTo("DeviceLost"));
        Assert.That(firstConnection.Writes.Last(), Is.EqualTo(new SwitchReport().GetBytes()));
        Assert.That(secondConnection.Writes.All(bytes => bytes.SequenceEqual(new SwitchReport().GetBytes())), Is.True);
        using FrameLease next = await hub.WaitForFrameAfterAsync(first.Metadata.FrameId, 0, new CancellationTokenSource(5000).Token);
        Assert.That(next.Metadata.SourceEpoch, Is.EqualTo(first.Metadata.SourceEpoch));
        Snapshot(controller, hub, next);
        Trace(TestContext.Out, "run-result", coordinator.LastResult);
    }

    private sealed class FailingDevice : NintendoSwitch
    {
        public readonly List<string> Attempts = [];
        protected override IConnection CreateConnection(string connStr, int baudrate)
        {
            Attempts.Add(connStr);
            throw new IOException("故障注入：显式端点不可用");
        }
    }

    [Test]
    public async Task ExplicitInputOpenFailure_DoesNotTryOtherPorts_OrCloseCapture()
    {
        FailingDevice device = new();
        using MaaControllerSession controller = new(device);
        using FrameHub hub = new();
        hub.Connect(new Source(), "preserved-on-input-failure");
        using FrameLease first = await hub.WaitForFrameAfterAsync(0, 0, new CancellationTokenSource(5000).Token);
        Assert.That(controller.TryConnect("fake:missing-explicit"), Is.EqualTo(ControllerConnectResult.Error));
        Assert.That(device.Attempts, Is.Not.Empty);
        Assert.That(device.Attempts.All(endpoint => endpoint == "fake:missing-explicit"), Is.True);
        Assert.That(controller.InputConnected, Is.False);
        using FrameLease next = await hub.WaitForFrameAfterAsync(first.Metadata.FrameId, 0, new CancellationTokenSource(5000).Token);
        Assert.That(next.Metadata.SourceEpoch, Is.EqualTo(first.Metadata.SourceEpoch));
        Snapshot(controller, hub, next);
        Trace(TestContext.Out, "input-open-attempts", device.Attempts);
    }

    [Test]
    public void PureCompute_HasNoHardwareOrNativeRequirement()
    {
        using MaaHostSession host = new();
        int called = 0;
        host.Runs.Run("ecs", null, false, default, _ => called++);
        Assert.That(called, Is.EqualTo(1));
        Assert.That(host.Runs.LastResult!.EffectiveConfig.RequiresInput, Is.False);
        Assert.That(host.Runs.LastResult.NativeStatus, Is.EqualTo("NotRequired"));
        Assert.That(host.Controller.InputConnected, Is.False);
        Assert.That(host.Frames.CaptureConnected, Is.False);
    }
}