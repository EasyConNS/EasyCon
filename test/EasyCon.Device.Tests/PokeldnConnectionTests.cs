using EasyDevice;
using EasyDevice.Connection;
using EasyDevice.Connection.Pokeldn;
using System.Collections.Concurrent;

namespace EasyCon.Device.Tests;

[TestFixture]
public class PokeldnConnectionTests
{
    [Test]
    public void RealDeviceLoop_SendsButtonsAndSticks_AndResetReleasesEverything()
    {
        FakeTransport transport = new();
        TestSwitch device = new(new PokeldnSerialClient("test", transport));
        try
        {
            Assert.That(device.TryConnect("pokeldn:fake"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
            Assert.That(transport.TakeReport(), Is.EqualTo(Convert.FromHexString("0000088080808000")));
            device.Down(ECKeyUtil.Button(SwitchButton.A));
            Assert.That(transport.TakeReport(), Is.EqualTo(Convert.FromHexString("0400088080808000")));
            device.LeftDirection(DirectionKey.Up, true);
            Assert.That(transport.TakeReport(), Is.EqualTo(Convert.FromHexString("0400088000808000")));
            device.Reset();
            Assert.That(transport.TakeReport(), Is.EqualTo(Convert.FromHexString("0000088080808000")));
            device.LeftDirection(DirectionKey.Right, true);
            Assert.That(transport.TakeReport(), Is.EqualTo(Convert.FromHexString("000008FF80808000")));
        }
        finally
        {
            device.Disconnect();
        }
        Assert.That(transport.Stops, Is.GreaterThanOrEqualTo(3));
        Assert.That(transport.TakeReport(), Is.EqualTo(Convert.FromHexString("0000088080808000")));
        Assert.That(transport.Disconnected, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ResetConfirmation_WaitsForTransportAck_AndUsesPackedReportEvent(bool serialEntry)
    {
        FakeTransport transport = new();
        IConnection connection = serialEntry ? new PokeldnSerialClient("test", transport) : new PokeldnConnection("test", transport);
        TestSwitch device = new(connection);
        try
        {
            Assert.That(device.TryConnect("pokeldn:fake"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
            transport.TakeReport();
            transport.BlockWrites = true;
            Task<bool> confirmed = device.ResetAndWaitForWriteAsync(TimeSpan.FromSeconds(3));
            Assert.That(transport.WriteEntered.Wait(2000), Is.True);
            Assert.That(confirmed.IsCompleted, Is.False, "Enqueuing a neutral report must not claim it was written");
            transport.AllowWrite.Set();
            Assert.That(await confirmed, Is.True);
            Assert.That(transport.TakeReport(), Is.EqualTo(Convert.FromHexString("0000088080808000")));
        }
        finally
        {
            transport.AllowWrite.Set();
            device.Disconnect();
        }
    }

    [Test]
    public void LegacyCommands_AreRejectedWithoutWritingToPad()
    {
        FakeTransport transport = new();
        TestSwitch device = new(new PokeldnConnection("test", transport));
        try
        {
            Assert.That(device.TryConnect("pokeldn:fake"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
            transport.TakeReport();
            Assert.Multiple(() =>
            {
                Assert.That(device.SupportsLegacyCommands, Is.False);
                Assert.That(device.FirmwareVersion, Is.EqualTo("1.3.0"));
                Assert.That(device.ConsoleConnected, Is.True);
                Assert.That(device.GetVersion(), Is.EqualTo(-1));
                Assert.That(device.Flash([]), Is.False);
                Assert.That(device.SaveAmiibo(0, []), Is.False);
                Assert.That(device.ChangeAmiiboIndex(0), Is.False);
                Assert.That(device.RemoteStart(), Is.False);
                Assert.That(device.RemoteStop(), Is.False);
                Assert.That(device.UnPair(), Is.False);
                Assert.That(device.TriggerLED(), Is.False);
                Assert.That(device.ChangeControllerColor(new byte[12]), Is.False);
                Assert.That(device.ChangeControllerMode(0), Is.False);
                Assert.That(transport.Reports.Count, Is.Zero);
            });
        }
        finally { device.Disconnect(); }
    }

    [Test]
    public void IdleDisconnect_IsDetectedByStatusPolling()
    {
        FakeTransport transport = new();
        TestSwitch device = new(new PokeldnConnection("test", transport));
        try
        {
            Assert.That(device.TryConnect("pokeldn:fake"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
            transport.FailStatus = true;
            Assert.That(SpinWait.SpinUntil(() => !device.IsConnected(), 3000), Is.True);
            Assert.That(device.LastConnectionError, Does.Contain("unplugged"));
        }
        finally { device.Disconnect(); }
    }

    [Test]
    public void AutoDetection_KeepsLegacyBaudsBeforePokeldn_ExplicitSkipsLegacy()
    {
        ProbeSwitch legacy = new(115200);
        ProbeSwitch pad = new(921600);
        ProbeSwitch explicitPad = new(921600);
        try
        {
            Assert.That(legacy.TryConnect("fake"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
            Assert.That(legacy.Bauds, Is.EqualTo(new[] { 115200 }));
            Assert.That(pad.TryConnect("fake"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
            Assert.That(pad.Bauds, Is.EqualTo(new[] { 115200, 9600, 921600 }));
            Assert.That(explicitPad.TryConnect("pokeldn:fake"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
            Assert.That(explicitPad.Bauds, Is.EqualTo(new[] { 921600 }));
        }
        finally
        {
            legacy.Disconnect();
            pad.Disconnect();
            explicitPad.Disconnect();
        }
    }

    [Test]
    public void ResetDuringSlowWrite_DiscardsQueuedReportsWithoutBlockingCaller()
    {
        FakeTransport transport = new();
        PokeldnConnection connection = new("test", transport);
        connection.Connect();
        try
        {
            Assert.That(SpinWait.SpinUntil(() => connection.CurrentStatus == Status.Connected, 2000), Is.True);
            transport.TakeReport();
            transport.BlockWrites = true;
            connection.WriteReport(new SwitchReport { Button = (ushort)SwitchButton.A });
            Assert.That(transport.WriteEntered.Wait(2000), Is.True);
            connection.WriteReport(new SwitchReport { Button = (ushort)SwitchButton.B });
            Task reset = Task.Run(connection.ClearQueue);
            Assert.That(reset.Wait(500), Is.True, "Reset must not wait for a BLE write");
            connection.WriteReport(new SwitchReport());
            transport.BlockWrites = false;
            transport.AllowWrite.Set();
            Assert.That(transport.TakeReport(), Is.EqualTo(Convert.FromHexString("0400088080808000")));
            Assert.That(transport.TakeReport(), Is.EqualTo(Convert.FromHexString("0000088080808000")));
            Assert.That(transport.Reports.Count, Is.Zero, "Queued B must have been discarded");
            Assert.That(transport.Stops, Is.GreaterThanOrEqualTo(2));
        }
        finally
        {
            transport.AllowWrite.Set();
            connection.Disconnect();
        }
    }

    [Test]
    public void DisconnectWhileHoldingA_ReleasesBeforeClosing_AndIsIdempotent()
    {
        FakeTransport transport = new();
        TestSwitch device = new(new PokeldnConnection("test", transport));
        Assert.That(device.TryConnect("pokeldn:fake"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
        transport.TakeReport();
        device.Down(ECKeyUtil.Button(SwitchButton.A));
        transport.TakeReport();
        device.Disconnect();
        device.Disconnect();
        Assert.That(transport.TakeReport(), Is.EqualTo(Convert.FromHexString("0000088080808000")));
        Assert.That(transport.Disconnected, Is.True);
    }

    [Test]
    public void LegacyOutput_RemainsSevenBitPacked()
    {
        ProbeConnection connection = new(true);
        TestSwitch device = new(connection);
        try
        {
            Assert.That(device.TryConnect("fake"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
            SwitchReport report = new() { Button = (ushort)SwitchButton.HOME, HAT = 7, LX = 0, LY = 255, RX = 42, RY = 128 };
            device.ApplyReport(report);
            Assert.That(connection.Writes.TryTake(out byte[]? actual, 2000), Is.True);
            Assert.That(actual, Is.EqualTo(report.GetBytes()));
        }
        finally { device.Disconnect(); }
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("pokeldn:")]
    [TestCase("pokeldn://typo")]
    public void InvalidEndpoint_DoesNotAttemptHardware(string endpoint)
    {
        NintendoSwitch device = new();
        Assert.That(device.TryConnect(endpoint), Is.EqualTo(NintendoSwitch.ConnectResult.InvalidArgument));
    }

    private sealed class TestSwitch(IConnection connection) : NintendoSwitch
    {
        protected override IConnection CreateConnection(string connStr, int baudrate) => connection;
        protected override IConnection CreatePokeldnConnection(string connStr) => connection;
    }

    private sealed class ProbeSwitch(int acceptedBaud) : NintendoSwitch
    {
        public List<int> Bauds { get; } = [];
        protected override IConnection CreatePokeldnConnection(string connStr) => CreateConnection(connStr, PokeldnSerialClient.DefaultBaudRate);
        protected override IConnection CreateConnection(string connStr, int baudrate)
        {
            Bauds.Add(baudrate);
            return new ProbeConnection(baudrate == acceptedBaud);
        }
    }

    private sealed class ProbeConnection(bool success) : IConnection
    {
        public BlockingCollection<byte[]> Writes { get; } = new();
        public override event StatusChangedHandler StatusChanged = delegate { };
        public override Status CurrentStatus { get; protected set; }
        public override void Connect()
        {
            CurrentStatus = success ? Status.Connected : Status.Error;
            StatusChanged(CurrentStatus);
        }
        public override void Disconnect() => CurrentStatus = Status.Connecting;
        public override void Write(params byte[] val) => Writes.Add(val);
        public override void ClearQueue() { }
    }

    private sealed class FakeTransport : IPokeldnTransport
    {
        public BlockingCollection<byte[]> Reports { get; } = new();
        public int Stops { get; private set; }
        public bool Disconnected { get; private set; }
        public volatile bool FailStatus;
        public volatile bool BlockWrites;
        public ManualResetEventSlim WriteEntered { get; } = new(false);
        public ManualResetEventSlim AllowWrite { get; } = new(false);
        public int ConnectTimeoutMilliseconds => 2000;
        public PokeldnStatus Connect(CancellationToken token) => new(true, true, "1.3.0");
        public PokeldnStatus GetStatus(CancellationToken token)
            => FailStatus ? throw new IOException("unplugged") : new(true, false, "1.3.0");
        public void SendReport(byte[] report, CancellationToken token)
        {
            if (BlockWrites)
            {
                WriteEntered.Set();
                AllowWrite.Wait(token);
            }
            Reports.Add(report);
        }
        public void Stop(CancellationToken token) => Stops++;
        public void Disconnect(CancellationToken token) => Disconnected = true;
        public void Dispose() { }
        public byte[] TakeReport()
        {
            Assert.That(Reports.TryTake(out byte[]? report, 2000), Is.True, "No report reached the transport");
            return report!;
        }
    }
}