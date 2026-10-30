using EasyCon.Core.Maa;
using EasyDevice;
using EasyDevice.Connection;

namespace EasyCon.Tests.Maa;

[TestFixture]
public class PokeldnHostTests
{
    [Test]
    public void ControllerSession_ExposesFirmwareCapabilities_AndConfirmsInitialRelease()
    {
        Device device = new();
        using MaaControllerSession controller = new(device);
        Assert.That(controller.TryConnect("pokeldn:fake:metadata"), Is.EqualTo(ControllerConnectResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(controller.InputConnected, Is.True);
            Assert.That(controller.SupportsLegacyCommands, Is.False);
            Assert.That(controller.FirmwareName, Is.EqualTo("pokeldn-pad"));
            Assert.That(controller.FirmwareVersion, Is.EqualTo("1.3.0"));
            Assert.That(controller.ConsoleConnected, Is.True);
            Assert.That(controller.LastCleanup, Is.EqualTo(CleanupStatus.Written));
            Assert.That(controller.GetVersion(), Is.EqualTo(-1));
        });
    }

    [TestCase("fake:pokeldn-owned", "pokeldn:fake:pokeldn-owned")]
    [TestCase(ECDevice.PokeldnServiceName, ECDevice.PokeldnServiceEndpoint)]
    [TestCase("POKELDN://SERVICE", ECDevice.PokeldnServiceName)]
    public void EndpointAliases_CannotBypassExclusiveOwnership(string firstEndpoint, string secondEndpoint)
    {
        using MaaControllerSession first = new(new Device());
        Device secondDevice = new();
        using MaaControllerSession second = new(secondDevice);
        Assert.That(first.TryConnect(firstEndpoint), Is.EqualTo(ControllerConnectResult.Success));
        Assert.That(second.TryConnect(secondEndpoint), Is.EqualTo(ControllerConnectResult.Busy));
        Assert.That(secondDevice.OpenAttempts, Is.Zero, "Alias must be rejected before opening a second device connection");
        first.Disconnect();
        Assert.That(second.TryConnect(secondEndpoint), Is.EqualTo(ControllerConnectResult.Success));
    }

    private sealed class Device : NintendoSwitch
    {
        public int OpenAttempts { get; private set; }
        protected override IConnection CreateConnection(string connStr, int baudrate)
        {
            OpenAttempts++;
            return new Connection();
        }
        protected override IConnection CreatePokeldnConnection(string connStr) => CreateConnection(connStr, 921600);
    }

    private sealed class Connection : IConnection
    {
        public override bool SupportsLegacyCommands => false;
        public override string FirmwareName => "pokeldn-pad";
        public override string FirmwareVersion => "1.3.0";
        public override bool? ConsoleConnected => true;
        public override Status CurrentStatus { get; protected set; } = Status.Connecting;
        public override event StatusChangedHandler StatusChanged = delegate { };
        public override event BytesTransferedHandler BytesSent = delegate { };
        public override void Connect()
        {
            CurrentStatus = Status.Connected;
            StatusChanged(CurrentStatus);
        }
        public override void Write(params byte[] val) => BytesSent("fake", val);
        public override void ClearQueue() { }
        public override void Disconnect() => CurrentStatus = Status.Connecting;
    }
}