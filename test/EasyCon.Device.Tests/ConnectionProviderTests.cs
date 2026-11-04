using EasyDevice;
using EasyDevice.Connection;

namespace EasyCon.Device.Tests;

[TestFixture]
public class ConnectionProviderTests
{
    [Test]
    public void SerialProtocols_AreLazyOrderedAndStopAtFirstSuccess()
    {
        List<string> events = [];
        SerialProtocol Protocol(int baud, bool accepts) => new(baud, (port, speed) =>
        {
            events.Add($"create:{port}:{speed}");
            return new TestConnection(accepts, () => events.Add($"close:{speed}"));
        });
        NintendoSwitch device = new(
        [
            new SerialConnectionProvider([Protocol(19200, false), Protocol(38400, true), Protocol(57600, true)])
        ]);
        try
        {
            Assert.That(events, Is.Empty);
            Assert.That(device.TryConnect("custom-port"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
            Assert.That(events, Is.EqualTo(new[] { "create:custom-port:19200", "close:19200", "create:custom-port:38400" }));
        }
        finally { device.Disconnect(); }
    }

    [Test]
    public async Task CustomBridge_UsesConfiguredAddressWithoutProbingSerial()
    {
        ConnectionEndpoint? selected = null;
        NintendoSwitch device = new(
        [
            new SerialConnectionProvider([new SerialProtocol(115200, (_, _) => throw new AssertionException("Bridge must not open a serial port"))]),
            new BridgeConnectionProvider([endpoint =>
            {
                selected = endpoint;
                return new TestConnection(true);
            }])
        ]);
        try
        {
            Assert.That(device.TryConnect("TCP://EXAMPLE.TEST:31789/"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
            Assert.That(selected!.Host, Is.EqualTo("example.test"));
            Assert.That(selected.Port, Is.EqualTo(31789));
            Assert.That(selected.Address, Is.EqualTo("tcp://example.test:31789"));
            Assert.That(await device.ResetAndWaitForWriteAsync(TimeSpan.FromSeconds(2)), Is.True);
        }
        finally { device.Disconnect(); }
    }

    [Test]
    public void MissingBridgeProtocol_DoesNotFallBackToSerial()
    {
        NintendoSwitch device = new(
        [
            new SerialConnectionProvider([new SerialProtocol(115200, (_, _) => throw new AssertionException("Unexpected serial probe"))])
        ]);
        try
        {
            Assert.That(device.TryConnect("tcp://localhost:31789"), Is.EqualTo(NintendoSwitch.ConnectResult.InvalidArgument));
            Assert.That(device.LastConnectionError, Does.Contain("TcpBridge"));
        }
        finally { device.Disconnect(); }
    }

    [TestCase("TCP://EXAMPLE.TEST:31789/", "tcp://example.test:31789")]
    [TestCase("tcp://[0:0:0:0:0:0:0:1]:31789/", "tcp://[::1]:31789")]
    public void EquivalentBridgeAddresses_HaveOneOwnershipKey(string input, string expected)
    {
        ConnectionEndpoint endpoint = ConnectionEndpoint.Parse(input);
        Assert.That(endpoint.Address, Is.EqualTo(expected));
        Assert.That(endpoint.OwnershipKey, Is.EqualTo(expected));
        Assert.That(endpoint.Transport, Is.EqualTo(ConnectionTransport.TcpBridge));
    }

    [TestCase("tcp://localhost")]
    [TestCase("tcp:/localhost:1234")]
    [TestCase("tcp://localhost:0")]
    [TestCase("tcp://localhost:65536")]
    [TestCase("tcp://user@localhost:1234")]
    [TestCase("tcp://localhost:1234/path")]
    [TestCase("tcp://localhost:1234?protocol=test")]
    [TestCase("tcp://localhost:1234#name")]
    public void InvalidBridgeAddress_IsRejectedBeforeAnyIo(string endpoint)
        => Assert.Throws<ArgumentException>(() => ConnectionEndpoint.Parse(endpoint));

    [Test]
    public void SerialAddresses_AreOpaqueAndNeverInterpretFirmwarePrefixes()
    {
        ConnectionEndpoint endpoint = ConnectionEndpoint.Parse("vendor:COM3");
        Assert.That(endpoint.Transport, Is.EqualTo(ConnectionTransport.Serial));
        Assert.That(endpoint.Address, Is.EqualTo("vendor:COM3"));
        if (OperatingSystem.IsWindows())
            Assert.That(ConnectionEndpoint.Parse(@"\\.\com3").OwnershipKey, Is.EqualTo(ConnectionEndpoint.Parse("COM3").OwnershipKey));
    }

    private sealed class TestConnection(bool accepts, Action? onDisconnect = null) : IConnection
    {
        public override Status CurrentStatus { get; protected set; } = Status.Connecting;
        public override event StatusChangedHandler StatusChanged = delegate { };
        public override event BytesTransferedHandler BytesSent = delegate { };
        public override void Connect()
        {
            CurrentStatus = accepts ? Status.Connected : Status.Error;
            StatusChanged(CurrentStatus);
        }
        public override void Disconnect()
        {
            CurrentStatus = Status.Connecting;
            onDisconnect?.Invoke();
        }
        public override void Write(params byte[] val) => BytesSent("test", val);
        public override void ClearQueue() { }
    }
}