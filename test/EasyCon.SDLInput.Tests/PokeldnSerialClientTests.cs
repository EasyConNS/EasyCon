using EasyDevice;
using EasyDevice.Connection;

namespace EasyCon.SDLInput.Tests;

/// <summary>
/// PokeldnSerialClient 的纯函数协议测试（报文翻译/组帧）与 NintendoSwitch 第三档回退顺序测试。
/// 全部不依赖真实串口。
/// </summary>
[TestFixture]
public class PokeldnSerialClientTests
{
    static byte[] Packed(SwitchReport report) => report.GetBytes();

    static byte PayloadChecksum(byte type, byte[] payload)
    {
        var sum = type;
        foreach (var b in payload)
            sum += b;
        return (byte)sum;
    }

    // ---- TranslateReport：中立报告 → 完整 pokeldn 帧 ----

    [Test]
    public void TranslateReport_NeutralReport_ProducesNeutralFrame()
    {
        var packet = Packed(new SwitchReport());

        Assert.Multiple(() =>
        {
            Assert.That(packet, Has.Length.EqualTo(8));
            Assert.That(packet[0] & 0x80, Is.Zero, "首字节 bit7 应为 0（数据）");
            Assert.That(packet[7] & 0x80, Is.Not.Zero, "末字节 bit7 应为 1（结束标志）");
        });

        var frame = PokeldnSerialClient.TranslateReport(packet);

        // payload = 按钮 u16 LE + HAT + LX + LY + RX + RY + vendor
        byte[] expectedPayload = [0, 0, 8, 128, 128, 128, 128, 0];
        byte[] expected =
        [
            0xA5, 0x5A,
            (byte)(expectedPayload.Length + 1), 0,  // len = type + payload = 9
            0x01,                                    // REPORT
            .. expectedPayload,
            PayloadChecksum(0x01, expectedPayload),
        ];
        Assert.That(frame, Is.EqualTo(expected));
    }

    // ---- TranslateReport：按键/摇杆字段映射 ----

    [Test]
    public void TranslateReport_MapsButtonHatSticksIntoPayload()
    {
        var report = new SwitchReport
        {
            Button = (ushort)(SwitchButton.A | SwitchButton.ZR),
            HAT = (byte)SwitchHAT.RIGHT,
            LX = 100,
        };
        var frame = PokeldnSerialClient.TranslateReport(Packed(report));

        Assert.That(frame, Is.Not.Null);
        var payload = frame![5..^1];

        Assert.Multiple(() =>
        {
            Assert.That(payload[0] | (payload[1] << 8), Is.EqualTo((ushort)(SwitchButton.A | SwitchButton.ZR)), "按钮 u16 LE");
            Assert.That(payload[2], Is.EqualTo((byte)SwitchHAT.RIGHT));
            Assert.That(payload[3], Is.EqualTo(100));
            Assert.That(payload[4], Is.EqualTo(SwitchStick.STICK_CENTER));
            Assert.That(payload[5], Is.EqualTo(SwitchStick.STICK_CENTER));
            Assert.That(payload[6], Is.EqualTo(SwitchStick.STICK_CENTER));
            Assert.That(payload[7], Is.Zero, "vendor 字节");
        });
    }

    // ---- TranslateReport：EasyCon 命令帧必须原样忽略 ----

    [Test]
    public void TranslateReport_NonReportPackets_ReturnNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PokeldnSerialClient.TranslateReport(null), Is.Null);
            Assert.That(PokeldnSerialClient.TranslateReport([0xA5, 0xA5, 0x81]), Is.Null, "握手帧");
            Assert.That(PokeldnSerialClient.TranslateReport([0xA5, 0x85]), Is.Null, "GetVersion 命令帧");
            Assert.That(PokeldnSerialClient.TranslateReport(new byte[20]), Is.Null, "Flash 数据包长度不为 8");
            Assert.That(PokeldnSerialClient.TranslateReport([0x80, 0, 0, 0, 0, 0, 0, 0x80]), Is.Null, "首字节 bit7=1 非数据包");
        });
    }

    // ---- TranslateReport：任意报告往返一致 ----

    [Test]
    public void TranslateReport_RoundTripsAllFields()
    {
        var reports = new[]
        {
            new SwitchReport(),
            new SwitchReport { Button = 0x3FFF, HAT = 7, LX = 0, LY = 255, RX = 1, RY = 254 },
            new SwitchReport { Button = (ushort)SwitchButton.CAPTURE, HAT = (byte)SwitchHAT.CENTER },
        };

        foreach (var report in reports)
        {
            var frame = PokeldnSerialClient.TranslateReport(Packed(report));
            Assert.That(frame, Is.Not.Null);

            var payload = frame![5..^1];
            Assert.Multiple(() =>
            {
                Assert.That(payload[0] | (payload[1] << 8), Is.EqualTo(report.Button));
                Assert.That(payload[2], Is.EqualTo(report.HAT));
                Assert.That(payload[3], Is.EqualTo(report.LX));
                Assert.That(payload[4], Is.EqualTo(report.LY));
                Assert.That(payload[5], Is.EqualTo(report.RX));
                Assert.That(payload[6], Is.EqualTo(report.RY));
            });
        }
    }

    // ---- BuildFrame：帧结构与校验和 ----

    [Test]
    public void BuildFrame_ProducesChecksummedFrame()
    {
        byte[] payload = [1, 2, 3];
        var frame = PokeldnSerialClient.BuildFrame(0x02, payload);

        Assert.Multiple(() =>
        {
            Assert.That(frame, Has.Length.EqualTo(6 + payload.Length));
            Assert.That(frame[0], Is.EqualTo(0xA5));
            Assert.That(frame[1], Is.EqualTo(0x5A));
            Assert.That(frame[2] | (frame[3] << 8), Is.EqualTo(1 + payload.Length), "len = type + payload");
            Assert.That(frame[4], Is.EqualTo(0x02));
            Assert.That(frame[^1], Is.EqualTo(PayloadChecksum(0x02, payload)));
            Assert.That(frame[^1], Is.EqualTo(0x08), "checksum = (2+1+2+3) & 0xFF");
        });
    }

    // ---- NintendoSwitch 第三档回退：前两档失败后按 pokeldn 协议连接 ----

    /// <summary>永不握手的假连接：模拟非 EasyCon、非 pokeldn 的串口设备（外层 1 秒超时）。</summary>
    private sealed class SilentConnection : IConnection
    {
        public override event BytesTransferedHandler BytesSent = delegate { };
        public override event BytesTransferedHandler BytesReceived = delegate { };
        public override event StatusChangedHandler StatusChanged = delegate { };

        public override Status CurrentStatus { get; protected set; } = Status.Connecting;

        public override void Connect() { /* 保持 Connecting：外层等待超时 */ }

        public override void Disconnect() { }

        public override void Write(params byte[] val) { }

        public override void ClearQueue() { }
    }

    /// <summary>CreateConnection 永不握手、CreatePokeldnConnection 立即握手的 NintendoSwitch。</summary>
    private sealed class PokeldnFallbackSwitch : NintendoSwitch
    {
        public FakeConnection PokeldnConnection { get; } = new("fake://ldn");

        public int RegularAttempts { get; private set; }

        protected override IConnection CreateConnection(string connStr, int baudrate)
        {
            RegularAttempts++;
            return new SilentConnection();
        }

        protected override IConnection CreatePokeldnConnection(string connStr) => PokeldnConnection;
    }

    [Test]
    public void TryConnect_FallsThroughToPokeldnProtocol()
    {
        var sw = new PokeldnFallbackSwitch();

        Assert.That(sw.TryConnect("fake://ldn"), Is.EqualTo(NintendoSwitch.ConnectResult.Success), "第三档 pokeldn 探测应成功");
        Assert.That(sw.RegularAttempts, Is.EqualTo(2), "EasyCon 协议应先试 115200 与 9600 两档");

        // 联机循环经由 pokeldn 连接实例对外写报告；此处连接替身为 FakeConnection，
        // 收到的仍是 7 字节 7-bit 打包报文（协议翻译发生在 PokeldnSerialClient 内部，由上面的纯函数测试覆盖）。
        var report = new SwitchReport { Button = (ushort)SwitchButton.A, HAT = (byte)SwitchHAT.RIGHT };
        sw.ApplyReport(report);
        var expected = report.GetBytes();

        var deadline = DateTime.UtcNow.AddSeconds(3);
        var found = false;
        while (DateTime.UtcNow < deadline && !found)
        {
            // 初始中立报告可能先入队，逐条取出直至匹配按键报告
            while (sw.PokeldnConnection.TryTake(out var packet, 200))
            {
                if (packet.SequenceEqual(expected))
                {
                    found = true;
                    break;
                }
            }
        }
        Assert.That(found, Is.True, "联机循环应经 pokeldn 连接写出所下发的报告");
    }
}