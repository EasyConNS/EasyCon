using EasyCon.Core.Maa;
using EasyCon2.Avalonia.DrawingBoard;
using EasyDevice;
using EasyDevice.Connection;
using EasyScript;
using System.Collections.Concurrent;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>画板经 <see cref="DeviceDrawingRunner"/> 走与脚本运行相同的独占租约（真实控制器会话 + 假串口）。</summary>
[TestFixture]
public class DrawingRunnerTests
{
    private static readonly byte[] Neutral = new SwitchReport().GetBytes();
    private static readonly byte[] PressedA = new SwitchReport { Button = (ushort)SwitchButton.A }.GetBytes();

    [Test]
    public void KeepAwake_AcquireAndRelease_CallsThePlatformApiWithoutThrowing()
    {
        // macOS / Windows 上真实调用系统接口（Objective-C 运行时 / kernel32），签名写错会在这里崩溃或抛异常
        for (int i = 0; i < 3; i++)
        {
            using KeepAwake awake = KeepAwake.Acquire();
        }
    }

    [Test]
    public async Task Run_OwnsInputExclusively_AndReleasesAfterwards()
    {
        using Harness h = new();
        bool manualAcceptedDuringRun = true;
        ControllerOperationException? manualError = null;

        h.Runner.Run((pad, _) =>
        {
            pad.PressButtons(GamePadKey.A);
            manualAcceptedDuringRun = h.Controller.CanAcceptManualInput;
            manualError = Assert.Throws<ControllerOperationException>(() => h.Controller.Down(ECKeyUtil.Button(SwitchButton.B)));
            // 设备写循环约 30 ms 一帧、只发最新报告；像画图落笔一样按住一段时间
            Thread.Sleep(200);
        }, keepAwake: false, CancellationToken.None);

        Assert.That(manualAcceptedDuringRun, Is.False, "画图期间应拒绝虚拟手柄/键盘等手动输入");
        Assert.That(manualError!.ErrorCode, Is.EqualTo("Busy"));
        Assert.That(h.Controller.CanAcceptManualInput, Is.True, "画图结束后应归还手动输入");
        await h.WaitForAsync(packets => packets.Any(p => p.SequenceEqual(PressedA)), "租约内的按键应写到设备");
        await h.WaitForAsync(packets => packets.Last().SequenceEqual(Neutral), "结束时应写出中性报告");
    }

    [Test]
    public void Run_WhileScriptOwnsInput_ThrowsBusyWithoutDrawing()
    {
        using Harness h = new();
        using ControllerRunLease script = h.Controller.BeginRun("script", default);
        bool drew = false;

        ControllerOperationException? error = Assert.Throws<ControllerOperationException>(
            () => h.Runner.Run((_, _) => drew = true, keepAwake: false, CancellationToken.None));

        Assert.That(error!.ErrorCode, Is.EqualTo("Busy"));
        Assert.That(drew, Is.False);
    }

    [Test]
    public async Task Run_Cancelled_ReleasesHeldButton()
    {
        using Harness h = new();
        using CancellationTokenSource cts = new();
        TaskCompletionSource pressed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task run = Task.Run(() => h.Runner.Run((pad, token) =>
        {
            pad.PressButtons(GamePadKey.A);
            pressed.SetResult();
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
        }, keepAwake: false, cts.Token));
        await pressed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        Assert.That(async () => await run.WaitAsync(TimeSpan.FromSeconds(5)), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(h.Controller.CanAcceptManualInput, Is.True);
        await h.WaitForAsync(packets => packets.Last().SequenceEqual(Neutral), "取消时 A 仍按着，租约结束必须写出中性报告");
    }

    private sealed class Harness : IDisposable
    {
        private readonly RecordingConnection _connection = new();
        private readonly ConcurrentQueue<byte[]> _packets = new();

        public Harness()
        {
            _connection.BytesSent += (_, packet) => _packets.Enqueue(packet.ToArray());
            Controller = new MaaControllerSession(new TestNintendoSwitch(_connection));
            Assert.That(Controller.TryConnect("fake://drawing-board"), Is.EqualTo(ControllerConnectResult.Success));
            Runner = new DeviceDrawingRunner(Controller);
        }

        public MaaControllerSession Controller { get; }
        public DeviceDrawingRunner Runner { get; }

        public async Task WaitForAsync(Func<byte[][], bool> condition, string message)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                byte[][] snapshot = _packets.ToArray();
                if (snapshot.Length > 0 && condition(snapshot))
                    return;
                await Task.Delay(20);
            }
            Assert.Fail(message);
        }

        public void Dispose() => Controller.Dispose();
    }

    private sealed class RecordingConnection : IConnection
    {
        private const string Port = "fake://drawing-board";

        public override event BytesTransferedHandler BytesSent = delegate { };
        public override event BytesTransferedHandler BytesReceived = delegate { };
        public override event StatusChangedHandler StatusChanged = delegate { };

        public override Status CurrentStatus { get; protected set; } = Status.Connecting;

        public override void Connect()
        {
            CurrentStatus = Status.Connected;
            StatusChanged?.Invoke(Status.Connected);
        }

        public override void Disconnect() => CurrentStatus = Status.Connecting;

        public override void Write(params byte[] val) => BytesSent?.Invoke(Port, val.ToArray());

        public override void ClearQueue()
        {
        }
    }

    private sealed class TestNintendoSwitch(RecordingConnection connection) : NintendoSwitch
    {
        protected override IConnection CreateConnection(string connStr, int baudrate) => connection;
    }
}