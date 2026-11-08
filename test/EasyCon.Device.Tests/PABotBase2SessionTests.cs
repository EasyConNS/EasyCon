using EasyDevice;
using EasyDevice.Connection.PABotBase2;

namespace EasyCon.Device.Tests;

[TestFixture]
public class PABotBase2SessionTests
{
    [TestCase(false, false)]
    [TestCase(true, true)]
    public void Connect_ReassemblesRepliesAndPreservesPairingWhenSelectingPro(bool reorder, bool duplicate)
    {
        using FakePABotBase2Port port = new() { Controller = 0, ReverseReplies = reorder, DuplicateReplies = duplicate };
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        PABotBase2Session session = new(port);
        session.Connect(timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(session.Ready, Is.True);
            Assert.That(session.FirmwareVersion, Is.EqualTo("2026101800"));
            Assert.That(session.ConsoleConnected, Is.True);
            Assert.That(port.Messages.Any(m => m[2] == 0x33), Is.True);
            Assert.That(port.Messages.Any(m => m[2] == 0x34), Is.False, "Reset-to-controller erases pairing.");
            Assert.That(port.Messages.Last(m => m[2] == 0x97), Is.EqualTo(new byte[] { 16, 0, 0x97, 1, 0xe8, 3, 0, 0, 0, 0, 8, 128, 0, 8, 128, 0 }));
        });
        port.ConsoleStatus = 0;
        session.PollStatus(timeout.Token);
        Assert.That(session.ConsoleConnected, Is.False, "Pairing is independent of the PC serial connection.");
        Assert.That(port.Acknowledgements, Is.Not.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LostOrMismatchedAck_RetriesWithoutExecutingACommandTwice(bool wrongSequence)
    {
        using FakePABotBase2Port port = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        PABotBase2Session session = new(port);
        session.Connect(timeout.Token);
        port.DropAckOnce = !wrongSequence;
        port.WrongAckOnce = wrongSequence;
        session.SendReport(PABotBase2Protocol.Buttons(new SwitchReport { Button = (ushort)SwitchButton.A }), timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(port.Writes.Any(p => p[3] == 0x92), Is.True);
            Assert.That(port.Messages.Count(m => m[2] == 0x97 && m[6] == 8), Is.EqualTo(1));
        });
    }

    [Test]
    public void CorruptReply_IsSkipped_AndZeroCreditIsRefreshedWithAnEmptyPacket()
    {
        using FakePABotBase2Port port = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        PABotBase2Session session = new(port);
        session.Connect(timeout.Token);
        port.CorruptReplyOnce = true;
        port.NextCredit = 0;
        session.PollStatus(timeout.Token);
        session.SendReport(PABotBase2Protocol.Buttons(new SwitchReport { Button = 4 }), timeout.Token);
        Assert.That(port.Writes.Any(p => p[3] == 0x12 && p.Length == 10), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void IncompatibleProtocol_FailsBeforeSendingControllerInput(bool reliableLayer)
    {
        using FakePABotBase2Port port = new();
        if (reliableLayer) port.RscVersion = 2025010100;
        else port.MlcVersion = 2025010100;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        PABotBase2Session session = new(port);
        Assert.Throws<NotSupportedException>(() => session.Connect(timeout.Token));
        Assert.That(port.Messages.Any(m => m[2] == 0x97), Is.False);
    }

    [Test]
    public void UnsupportedController_FailsBeforeChangingMode()
    {
        using FakePABotBase2Port port = new() { SupportedController = 0x1100 };
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        PABotBase2Session session = new(port);
        Assert.Throws<NotSupportedException>(() => session.Connect(timeout.Token));
        Assert.That(port.Messages.Any(m => m[2] is 0x33 or 0x97), Is.False);
    }

    [Test]
    public void RepeatedInputAndStatus_SurvivePacketRequestAndStreamOffsetWraparound()
    {
        using FakePABotBase2Port port = new() { PacketSize = 256, ReadChunk = 512, ReplyChunk = 246 };
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        PABotBase2Session session = new(port);
        session.Connect(timeout.Token);
        byte[] buttons = PABotBase2Protocol.Buttons(new SwitchReport { Button = 4 });
        for (int count = 0; count < 4200; count++)
        {
            session.SendReport(buttons, timeout.Token);
            session.PollStatus(timeout.Token);
        }
        Assert.Multiple(() =>
        {
            Assert.That(port.Messages.Count(m => m[2] == 0x97), Is.EqualTo(4201));
            Assert.That(session.ConsoleConnected, Is.True);
            Assert.That(port.Writes.All(p => p.Length <= 128), Is.True);
        });
    }

    [Test]
    public void CurrentCommandRejected_IsReportedAsAnError()
    {
        using FakePABotBase2Port port = new() { ReadChunk = 512, ReplyChunk = 246 };
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        PABotBase2Session session = new(port);
        session.Connect(timeout.Token);
        port.RejectCommands = true;
        IOException? error = Assert.Throws<IOException>(() =>
        {
            session.SendReport(PABotBase2Protocol.Buttons(new SwitchReport { Button = 4 }), timeout.Token);
            for (int count = 0; count < 5; count++) session.Pump(timeout.Token);
        });
        Assert.That(error!.Message, Does.Contain("拒绝"));
    }
}