using EasyDevice;
using EasyDevice.Connection.PABotBase2;
using System.Text;

namespace EasyCon.Device.Tests;

[TestFixture]
public class PABotBase2ProtocolTests
{
    [Test]
    public void Crc32C_UsesCastagnoliWithoutFinalXor()
        => Assert.That(PABotBase2Protocol.Crc32C(Encoding.ASCII.GetBytes("123456789"), uint.MaxValue), Is.EqualTo(0x1cf96d7cu));

    [Test]
    public void Packets_MatchVectorsProducedByTheOfficialCrcImplementation()
    {
        // Generated with Common/CRC32/pabb_CRC32_Basic.c from the upstream commit in NOTICE.txt.
        Assert.That(PABotBase2Protocol.Packet(0, 1, [0x78, 0x56, 0x34, 0x12], 0),
            Is.EqualTo(Convert.FromHexString("81000C0178563412C6B07C1E")));
        Assert.That(PABotBase2Protocol.Packet(5, 0x12, [0, 0, 4, 0, 0x20, 1], 0x12345678),
            Is.EqualTo(Convert.FromHexString("81050E12000004002001F8273F4B")));
    }

    [Test]
    public void PacketReader_HandlesNoiseCorruptCrcSplitsAndMaximumLength()
    {
        PABotBase2PacketReader reader = new();
        byte[] packet = PABotBase2Protocol.Packet(253, 0x92, new byte[248], 1234);
        byte[] corrupt = packet.ToArray();
        corrupt[^1] ^= 1;
        reader.Append([1, 2, 0x81, 0, 3, 0]);
        reader.Append(corrupt);
        reader.Append(packet.AsSpan(0, 13));
        Assert.That(reader.TryRead(1234, out _), Is.False);
        reader.Append(packet.AsSpan(13));
        Assert.That(reader.TryRead(1234, out PABotBase2Packet parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(packet[2], Is.Zero);
            Assert.That(parsed.Sequence, Is.EqualTo(253));
            Assert.That(parsed.Opcode, Is.EqualTo(0x12));
            Assert.That(parsed.Payload, Has.Length.EqualTo(248));
            Assert.That(reader.TryRead(1234, out _), Is.False);
        });
    }

    [Test]
    public void Reset_UsesFixedSeed_AndStaleSessionPacketsAreIgnored()
    {
        byte[] reset = PABotBase2Protocol.Packet(0, 1, [0x78, 0x56, 0x34, 0x12], 42);
        Assert.That(reset, Is.EqualTo(PABotBase2Protocol.Packet(0, 1, [0x78, 0x56, 0x34, 0x12], 1)));
        PABotBase2PacketReader reader = new();
        reader.Append(PABotBase2Protocol.Packet(1, 0x52, [0, 0, 0, 0], 123));
        Assert.That(reader.TryRead(124, out _), Is.False);
    }

    [TestCase(SwitchButton.Y, 0, 1)]
    [TestCase(SwitchButton.X, 0, 2)]
    [TestCase(SwitchButton.B, 0, 4)]
    [TestCase(SwitchButton.A, 0, 8)]
    [TestCase(SwitchButton.R, 0, 64)]
    [TestCase(SwitchButton.ZR, 0, 128)]
    [TestCase(SwitchButton.MINUS, 1, 1)]
    [TestCase(SwitchButton.PLUS, 1, 2)]
    [TestCase(SwitchButton.RCLICK, 1, 4)]
    [TestCase(SwitchButton.LCLICK, 1, 8)]
    [TestCase(SwitchButton.HOME, 1, 16)]
    [TestCase(SwitchButton.CAPTURE, 1, 32)]
    [TestCase(SwitchButton.L, 2, 64)]
    [TestCase(SwitchButton.ZL, 2, 128)]
    public void Buttons_MapToOemBits(SwitchButton button, int index, int expected)
    {
        byte[] bytes = PABotBase2Protocol.Buttons(new SwitchReport { Button = (ushort)button });
        byte[] buttons = new byte[3];
        buttons[index] = (byte)expected;
        Assert.That(bytes[..3], Is.EqualTo(buttons));
        Assert.That(bytes[3..], Is.EqualTo(new byte[] { 0, 8, 128, 0, 8, 128, 0 }));
    }

    [TestCase(0, 2)]
    [TestCase(1, 6)]
    [TestCase(2, 4)]
    [TestCase(3, 5)]
    [TestCase(4, 1)]
    [TestCase(5, 9)]
    [TestCase(6, 8)]
    [TestCase(7, 10)]
    [TestCase(8, 0)]
    public void Hat_MapsCardinalsDiagonalsAndNeutral(byte hat, byte expected)
        => Assert.That(PABotBase2Protocol.Buttons(new SwitchReport { HAT = hat })[2], Is.EqualTo(expected));

    [Test]
    public void Sticks_UseTwelveBitCoordinates_CorrectYDirection_AndCenteredNeutral()
    {
        byte[] neutral = PABotBase2Protocol.Buttons(new SwitchReport());
        byte[] extremes = PABotBase2Protocol.Buttons(new SwitchReport { LX = 0, LY = 0, RX = 255, RY = 255 });
        Assert.Multiple(() =>
        {
            Assert.That(neutral, Is.EqualTo(new byte[] { 0, 0, 0, 0, 8, 128, 0, 8, 128, 0 }));
            Assert.That(extremes[3..9], Is.EqualTo(new byte[] { 0, 240, 255, 255, 15, 0 }));
        });
        byte[] small = PABotBase2Protocol.Buttons(new SwitchReport { LX = 129 });
        int x = small[3] | ((small[4] & 15) << 8);
        Assert.That(x, Is.InRange(2220, 2240), "Small input must compensate the Pro Controller dead zone.");
    }
}