using EasyDevice;
using EasyDevice.Connection.Pokeldn;
using System.Text;

namespace EasyCon.Device.Tests;

[TestFixture]
public class PokeldnProtocolTests
{
    [Test]
    public void NeutralAndA_MatchFirmwareGoldenFrames()
    {
        Assert.That(PokeldnProtocol.Frame(PokeldnProtocol.StatusRequest, []),
            Is.EqualTo(Convert.FromHexString("A55A01000202")));
        Assert.That(PokeldnProtocol.Frame(PokeldnProtocol.Command, [PokeldnProtocol.Stop]),
            Is.EqualTo(Convert.FromHexString("A55A0200031316")));
        SwitchReport report = new();
        Assert.That(PokeldnProtocol.EncodeReport(report), Is.EqualTo(Convert.FromHexString("0000088080808000")));
        report.Button = (ushort)SwitchButton.A;
        Assert.That(PokeldnProtocol.Frame(PokeldnProtocol.Report, PokeldnProtocol.EncodeReport(report)),
            Is.EqualTo(Convert.FromHexString("A55A09000104000880808080000D")));
    }

    [TestCase(SwitchButton.Y, 0)]
    [TestCase(SwitchButton.B, 1)]
    [TestCase(SwitchButton.A, 2)]
    [TestCase(SwitchButton.X, 3)]
    [TestCase(SwitchButton.L, 4)]
    [TestCase(SwitchButton.R, 5)]
    [TestCase(SwitchButton.ZL, 6)]
    [TestCase(SwitchButton.ZR, 7)]
    [TestCase(SwitchButton.MINUS, 8)]
    [TestCase(SwitchButton.PLUS, 9)]
    [TestCase(SwitchButton.LCLICK, 10)]
    [TestCase(SwitchButton.RCLICK, 11)]
    [TestCase(SwitchButton.HOME, 12)]
    [TestCase(SwitchButton.CAPTURE, 13)]
    public void EveryButton_UsesTheFirmwareBit(SwitchButton button, int bit)
    {
        byte[] report = PokeldnProtocol.EncodeReport(new SwitchReport { Button = (ushort)button });
        Assert.That(report[0] | (report[1] << 8), Is.EqualTo(1 << bit));
    }

    [TestCase((byte)0)]
    [TestCase((byte)1)]
    [TestCase((byte)2)]
    [TestCase((byte)3)]
    [TestCase((byte)4)]
    [TestCase((byte)5)]
    [TestCase((byte)6)]
    [TestCase((byte)7)]
    [TestCase((byte)8)]
    public void HatAndFullRangeSticks_AreNotSevenBitPacked(byte hat)
    {
        SwitchReport report = new() { Button = 0x3FFF, HAT = hat, LX = 0, LY = 255, RX = 127, RY = 128 };
        Assert.That(PokeldnProtocol.EncodeReport(report), Is.EqualTo(new byte[] { 255, 63, hat, 0, 255, 127, 128, 0 }));
    }

    [Test]
    public void Reader_HandlesEverySplitAndMultipleFrames()
    {
        byte[] packet = Convert.FromHexString("A55A0200810081");
        for (int split = 0; split <= packet.Length; split++)
        {
            PokeldnFrameReader reader = new();
            List<PokeldnFrame> frames = [.. reader.Feed(packet[..split], split)];
            frames.AddRange(reader.Feed(packet[split..], packet.Length - split));
            Assert.That(frames, Has.Count.EqualTo(1), $"split {split}");
            Assert.That(frames[0].Kind, Is.EqualTo(0x81));
            Assert.That(frames[0].Payload, Is.EqualTo(new byte[] { 0 }));
        }
        byte[] combined = [.. packet, .. packet];
        Assert.That(new PokeldnFrameReader().Feed(combined, combined.Length).Count(), Is.EqualTo(2));
    }

    [Test]
    public void Reader_RecoversFromBootNoiseBadLengthsAndChecksum()
    {
        byte[] input = [.. Encoding.ASCII.GetBytes("rst:0x1 boot"), 0xA5,
            0xA5, 0x5A, 0, 0, 0xA5, 0x5A, 0xFF, 0x7F,
            .. Convert.FromHexString("A55A0200810080A55A0200810081")];
        List<PokeldnFrame> frames = [.. new PokeldnFrameReader().Feed(input, input.Length)];
        Assert.That(frames, Has.Count.EqualTo(1));
        Assert.That(frames[0].Payload, Is.EqualTo(new byte[] { 0 }));
    }

    [Test]
    public void Status_DecodesVersionWithoutBootDiagnostics()
    {
        byte[] payload = [1, 10, 0, 0, 0, 1, 2, 0, 0, 0, 3, 0, 4, 0,
            .. Encoding.ASCII.GetBytes("1.3.0"), 0, 1, 5, 2, 0];
        Assert.That(PokeldnProtocol.ParseStatus(payload), Is.EqualTo(new PokeldnStatus(true, true, "1.3.0")));
        Assert.That(PokeldnProtocol.ParseStatus([0, 0, 0, 0, 0]), Is.EqualTo(new PokeldnStatus(false, false, "")));
        Assert.Throws<IOException>(() => PokeldnProtocol.ParseStatus([0, 0]));
        Assert.Throws<IOException>(() => PokeldnProtocol.ParseStatus([2, 0, 0, 0, 0]));
    }
}