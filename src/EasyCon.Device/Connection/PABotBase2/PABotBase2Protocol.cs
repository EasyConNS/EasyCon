using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace EasyDevice.Connection.PABotBase2;

// Wire definitions and Pro Controller calibration: PokemonAutomation/Arduino-Source,
// Common/PABotBase2 and NintendoSwitch_PABotBase2_OemController.h. See NOTICE.txt.
internal static class PABotBase2Protocol
{
    internal const uint ProtocolVersion = 2026061800;
    internal const uint WirelessProController = 0x1180;
    internal const int ReceiveWindowBytes = 16384;
    internal const ushort HoldMilliseconds = 1000;

    internal static uint Crc32C(ReadOnlySpan<byte> bytes, uint seed)
    {
        uint crc = seed;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0x82f63b78u : 0);
        }
        return crc; // PABotBase2 does not apply the usual final XOR.
    }

    internal static byte[] Packet(byte sequence, byte opcode, ReadOnlySpan<byte> payload, uint session)
    {
        if (payload.Length > 248)
            throw new ArgumentOutOfRangeException(nameof(payload));
        byte[] packet = new byte[payload.Length + 8];
        packet[0] = 0x81;
        packet[1] = sequence;
        packet[2] = unchecked((byte)packet.Length); // 0 encodes 256 bytes.
        packet[3] = opcode;
        payload.CopyTo(packet.AsSpan(4));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(packet.Length - 4),
            Crc32C(packet.AsSpan(0, packet.Length - 4), opcode == 0x01 ? uint.MaxValue : session));
        return packet;
    }

    internal static byte[] U32(uint value)
    {
        byte[] data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        return data;
    }

    internal static byte[] Message(byte opcode, byte id, ReadOnlySpan<byte> payload = default)
    {
        byte[] data = new byte[checked((ushort)(payload.Length + 4))];
        BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)data.Length);
        data[2] = opcode;
        data[3] = id;
        payload.CopyTo(data.AsSpan(4));
        return data;
    }

    internal static void RequireCompatible(uint version, string layer)
    {
        if (version / 100 != ProtocolVersion / 100 || version < ProtocolVersion)
            throw new NotSupportedException($"PABotBase2 {layer} 协议版本 {version} 不兼容；需要 20260618xx。");
    }

    internal static byte[] Buttons(SwitchReport report)
    {
        byte[] data = new byte[10];
        Map(SwitchButton.Y, 0, 0); Map(SwitchButton.X, 0, 1);
        Map(SwitchButton.B, 0, 2); Map(SwitchButton.A, 0, 3);
        Map(SwitchButton.R, 0, 6); Map(SwitchButton.ZR, 0, 7);
        Map(SwitchButton.MINUS, 1, 0); Map(SwitchButton.PLUS, 1, 1);
        Map(SwitchButton.RCLICK, 1, 2); Map(SwitchButton.LCLICK, 1, 3);
        Map(SwitchButton.HOME, 1, 4); Map(SwitchButton.CAPTURE, 1, 5);
        Map(SwitchButton.L, 2, 6); Map(SwitchButton.ZL, 2, 7);
        data[2] |= report.HAT switch
        {
            0 => 2,
            1 => 6,
            2 => 4,
            3 => 5,
            4 => 1,
            5 => 9,
            6 => 8,
            7 => 10,
            _ => 0
        };
        Stick(data.AsSpan(3, 3), report.LX, report.LY);
        Stick(data.AsSpan(6, 3), report.RX, report.RY);
        return data;

        void Map(SwitchButton button, int index, int bit)
        {
            if ((report.Button & (ushort)button) != 0)
                data[index] |= (byte)(1 << bit);
        }
    }

    private static void Stick(Span<byte> target, byte x, byte y)
    {
        double fx = Axis(x);
        double fy = -Axis(y); // EasyCon: up = 0; the Bluetooth OEM report: up = positive.
        double magnitude = Math.Sqrt(fx * fx + fy * fy);
        if (magnitude >= 1)
        {
            double scale = 1 / Math.Max(Math.Abs(fx), Math.Abs(fy));
            fx *= scale;
            fy *= scale;
        }
        else if (magnitude > 0)
        {
            // Match the firmware's Pro Controller calibration, including its radial dead zone.
            double scale = (174 + magnitude * (1728 - 174)) / (2048 * magnitude);
            fx *= scale;
            fy *= scale;
        }
        int wx = Coordinate(fx);
        int wy = Coordinate(fy);
        target[0] = (byte)wx;
        target[1] = (byte)((wx >> 8) | (wy << 4));
        target[2] = (byte)(wy >> 4);

        static double Axis(byte value) => value < 128 ? (value - 128) / 128.0 : (value - 128) / 127.0;
        static int Coordinate(double value) => (int)(2048 + Math.Clamp(value, -1, 1) * (value > 0 ? 2047 : 2048) + 0.5);
    }
}

internal readonly record struct PABotBase2Packet(byte Sequence, byte Opcode, byte[] Payload);

internal sealed class PABotBase2PacketReader
{
    private readonly List<byte> _buffer = [];

    internal void Clear() => _buffer.Clear();

    internal void Append(ReadOnlySpan<byte> bytes)
    {
        // The serial reader feeds at most 512 bytes, and drains complete packets each time.
        if (_buffer.Count + bytes.Length > 4096)
            throw new IOException("PABotBase2 串口接收缓冲区溢出。");
        foreach (byte value in bytes)
            _buffer.Add(value);
    }

    internal bool TryRead(uint session, out PABotBase2Packet packet)
    {
        packet = default;
        while (_buffer.Count >= 4)
        {
            int length = _buffer[2] == 0 ? 256 : _buffer[2];
            if (_buffer[0] != 0x81 || length < 8)
            {
                _buffer.RemoveAt(0);
                continue;
            }
            if (_buffer.Count < length)
                return false;
            ReadOnlySpan<byte> data = CollectionsMarshal.AsSpan(_buffer)[..length];
            uint seed = data[3] == 0x01 ? uint.MaxValue : session;
            if (PABotBase2Protocol.Crc32C(data[..^4], seed) != BinaryPrimitives.ReadUInt32LittleEndian(data[^4..]))
            {
                _buffer.RemoveAt(0);
                continue;
            }
            packet = new(data[1], (byte)(data[3] & 0x7f), data[4..^4].ToArray());
            _buffer.RemoveRange(0, length);
            return true;
        }
        return false;
    }
}