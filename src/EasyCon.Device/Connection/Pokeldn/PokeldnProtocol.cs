using System.Buffers.Binary;
using System.Text;

namespace EasyDevice.Connection.Pokeldn;

/// <summary>pokeldn-pad 1.3.0 的 UART 帧和共享 HID 报文（不是 pokeldn-radio 协议）。</summary>
internal static class PokeldnProtocol
{
    internal const int BaudRate = 921600;
    internal const int MaxBodyLength = 600;
    internal const byte Report = 0x01;
    internal const byte StatusRequest = 0x02;
    internal const byte Command = 0x03;
    internal const byte Result = 0x81;
    internal const byte StatusReply = 0x82;
    internal const byte Stop = 0x13;

    internal static byte[] EncodeReport(SwitchReport report) =>
    [
        (byte)report.Button, (byte)(report.Button >> 8), report.HAT,
        report.LX, report.LY, report.RX, report.RY, 0
    ];

    internal static byte[] Frame(byte kind, ReadOnlySpan<byte> payload)
    {
        if (payload.Length >= MaxBodyLength)
            throw new ArgumentOutOfRangeException(nameof(payload));
        byte[] frame = new byte[payload.Length + 6];
        frame[0] = 0xA5;
        frame[1] = 0x5A;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)(payload.Length + 1));
        frame[4] = kind;
        payload.CopyTo(frame.AsSpan(5));
        int checksum = kind;
        foreach (byte value in payload)
            checksum += value;
        frame[^1] = (byte)checksum;
        return frame;
    }

    internal static PokeldnStatus ParseStatus(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 5 || payload[0] > 1 || (payload.Length > 5 && payload.Length < 14))
            throw new IOException("pokeldn-pad 返回了无效的状态报文。");
        bool playing = payload.Length >= 14 && payload[5] != 0;
        string version = "";
        if (payload.Length > 14)
        {
            ReadOnlySpan<byte> text = payload[14..];
            int end = text.IndexOf((byte)0);
            version = Encoding.UTF8.GetString(end < 0 ? text : text[..end]);
        }
        return new PokeldnStatus(payload[0] != 0, playing, version);
    }
}

internal sealed record PokeldnStatus(bool Mounted, bool Playing, string Version);
internal sealed record PokeldnFrame(byte Kind, byte[] Payload);

/// <summary>允许串口分片、粘包和开机日志；校验失败后重新查找帧头。</summary>
internal sealed class PokeldnFrameReader
{
    private readonly List<byte> _buffer = [];

    internal void Reset() => _buffer.Clear();

    internal IEnumerable<PokeldnFrame> Feed(byte[] bytes, int count)
    {
        _buffer.AddRange(bytes.AsSpan(0, count).ToArray());
        List<PokeldnFrame> frames = [];
        while (_buffer.Count >= 2)
        {
            if (_buffer[0] != 0xA5 || _buffer[1] != 0x5A)
            {
                _buffer.RemoveAt(0);
                continue;
            }
            if (_buffer.Count < 4)
                break;
            int length = _buffer[2] | (_buffer[3] << 8);
            if (length is < 1 or > PokeldnProtocol.MaxBodyLength)
            {
                _buffer.RemoveAt(0);
                continue;
            }
            if (_buffer.Count < length + 5)
                break;
            int checksum = 0;
            for (int i = 4; i < length + 4; i++)
                checksum += _buffer[i];
            if ((byte)checksum != _buffer[length + 4])
            {
                _buffer.RemoveAt(0);
                continue;
            }
            frames.Add(new PokeldnFrame(_buffer[4], _buffer.GetRange(5, length - 1).ToArray()));
            _buffer.RemoveRange(0, length + 5);
        }
        return frames;
    }
}