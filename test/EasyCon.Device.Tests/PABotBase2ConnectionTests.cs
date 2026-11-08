using EasyDevice;
using EasyDevice.Connection;
using EasyDevice.Connection.PABotBase2;
using System.Collections.Concurrent;

namespace EasyCon.Device.Tests;

[TestFixture]
public class PABotBase2ConnectionTests
{
    [Test]
    public async Task QueuedPressReleaseAndHold_ArePreservedAndStopReleasesInput()
    {
        FakePABotBase2Port port = new();
        PABotBase2SerialClient connection = new("test-pico", port);
        ConcurrentQueue<byte[]> confirmations = new();
        connection.BytesSent += (_, bytes) => confirmations.Enqueue(bytes);
        try
        {
            connection.Connect();
            await WaitFor(() => connection.CurrentStatus == Status.Connected, connection);
            SwitchReport report = new() { Button = (ushort)SwitchButton.A };
            byte[] pressed = report.GetBytes();
            connection.WriteReport(report);
            report.Reset();
            connection.WriteReport(report);
            await WaitFor(() => confirmations.Count >= 2, connection);
            Assert.That(confirmations.ToArray()[..2], Is.EqualTo(new[] { pressed, report.GetBytes() }));
            byte[][] commands = port.Messages.Where(m => m[2] == 0x97).ToArray();
            Assert.That(commands[1][6], Is.EqualTo(8));
            Assert.That(commands[2][6], Is.Zero);

            connection.WriteReport(new SwitchReport { Button = (ushort)SwitchButton.HOME });
            await WaitFor(() => port.Messages.Count(m => m[2] == 0x97 && m[7] == 16) >= 3, connection);
            int cancelCount = port.Messages.Count(m => m[2] == 0x41);
            for (int count = 0; count < 40; count++)
                connection.WriteReport(new SwitchReport { Button = (ushort)SwitchButton.B });
            connection.ClearQueue();
            await WaitFor(() => port.Messages.Count(m => m[2] == 0x41) > cancelCount &&
                port.Messages.Last(m => m[2] == 0x97)[7] == 0, connection);
            await Task.Delay(100);
            byte[][] stopped = port.Messages.ToArray();
            int cancelIndex = Array.FindLastIndex(stopped, m => m[2] == 0x41);
            Assert.That(stopped[(cancelIndex + 1)..].Where(m => m[2] == 0x97).All(m => m[6] == 0 && m[7] == 0), Is.True,
                "Stopped input must not be followed by an old queued press.");
            Assert.That(connection.CurrentStatus, Is.EqualTo(Status.Connected), connection.LastError);
        }
        finally { connection.Disconnect(); }
        Assert.That(port.Disposed, Is.True);
        Assert.That(port.Messages.Last(m => m[2] == 0x97)[6..], Is.EqualTo(new byte[] { 0, 0, 0, 0, 8, 128, 0, 8, 128, 0 }));
    }

    [Test]
    public async Task UnpairedConsole_DoesNotPreventSerialConnection_AndGenericResetIsConfirmed()
    {
        FakePABotBase2Port port = new() { ConsoleStatus = 0 };
        PABotBase2SerialClient connection = new("COM-test", port);
        NintendoSwitch device = new([new SerialConnectionProvider([new SerialProtocol(921600, (_, _) => connection)])]);
        try
        {
            Assert.That(device.TryConnect("COM-test"), Is.EqualTo(NintendoSwitch.ConnectResult.Success), connection.LastError);
            Assert.That(device.ConsoleConnected, Is.False);
            Assert.That(device.SupportsLegacyCommands, Is.False);
            Assert.That(await device.ResetAndWaitForWriteAsync(TimeSpan.FromSeconds(3)), Is.True);
            Assert.Throws<NotSupportedException>(() => connection.Write(1, 2, 3));
        }
        finally { device.Disconnect(); }
        Assert.That(port.Disposed, Is.True);
    }

    [Test]
    public async Task MissingAck_FailsBoundedlyAndClosesPort()
    {
        FakePABotBase2Port port = new();
        PABotBase2SerialClient connection = new("COM-test", port);
        try
        {
            connection.Connect();
            await WaitFor(() => connection.CurrentStatus == Status.Connected, connection);
            port.WithholdStreamAcks = true;
            connection.WriteReport(new SwitchReport { Button = 4 });
            await WaitFor(() => connection.CurrentStatus == Status.Error, connection, 5000);
            Assert.That(connection.LastError, Does.Contain("超时"));
            Assert.That(port.Disposed, Is.True);
            Assert.That(port.Writes.Count(p => p[3] == 1), Is.GreaterThanOrEqualTo(2),
                "Cleanup must reset an ambiguous stream before sending release commands.");
        }
        finally { connection.Disconnect(); }
    }

    [Test]
    public async Task DisconnectDuringProbe_StopsRetriesAndReleasesPort()
    {
        FakePABotBase2Port port = new() { Silent = true };
        PABotBase2SerialClient connection = new("COM-test", port);
        connection.Connect();
        await WaitFor(() => !port.Writes.IsEmpty, connection);
        connection.Disconnect();
        Assert.Multiple(() =>
        {
            Assert.That(port.Disposed, Is.True);
            Assert.That(connection.CurrentStatus, Is.EqualTo(Status.Connecting));
            Assert.That(connection.LastError, Is.Null);
        });
    }

    [Test]
    public async Task QueueOverflow_IsAnExplicitFailure_AndDisposesTheConnection()
    {
        FakePABotBase2Port port = new();
        PABotBase2SerialClient connection = new("COM-test", port);
        try
        {
            connection.Connect();
            await WaitFor(() => connection.CurrentStatus == Status.Connected, connection);
            for (int count = 0; count < 300; count++)
                connection.WriteReport(new SwitchReport { Button = (ushort)(count % 2 == 0 ? 4 : 0) });
            await WaitFor(() => connection.CurrentStatus == Status.Error, connection);
            Assert.That(connection.LastError, Does.Contain("队列已满"));
            Assert.That(port.Disposed, Is.True);
        }
        finally { connection.Disconnect(); }
    }

    private static async Task WaitFor(Func<bool> condition, PABotBase2SerialClient connection, int milliseconds = 3000)
    {
        long deadline = Environment.TickCount64 + milliseconds;
        while (!condition() && Environment.TickCount64 < deadline)
            await Task.Delay(10);
        Assert.That(condition(), Is.True, connection.LastError ?? "Timed out waiting for serial activity.");
    }
}