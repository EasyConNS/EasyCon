using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyDevice;

class EasyConPad
{
    private Stream _stream { get; }

    public EasyConPad(Stream stream)
    {
        _stream = stream;
    }

    public void DisConnect()
    {
        _stream?.Close();
    }

    /// <summary>
    /// Reply.Ack for success action,
    /// Reply.Busy for script running busy
    /// </summary>
    /// <param name="report"></param>
    /// <returns></returns>
    public byte SendReport(SwitchReport report)
    {
        var reportBytes = GetReportBytes(report);
        return SendResponse(reportBytes);
    }

    public bool HandShake()
    {
        var hellobytes = new byte[] { Command.Ready, Command.Hello };
        var buf = SendResponse(hellobytes);
        return buf == Reply.Hello;
    }

    const int PacketSize = 20;
    public bool Flash(byte[] data)
    {
        List<byte> list = new(data);
        for (int i = 0; i < list.Count; i += PacketSize)
        {
            int len = Math.Min(PacketSize, list.Count - i);
            var header = new byte[] { Command.Ready, (byte)(i & 0x7F), (byte)(i >> 7), (byte)(len & 0x7F), (byte)(len >> 7), };
            var packet = list.GetRange(i, len).ToArray();

            while (true)
            {
                var buf = SendResponse(header);
                if (buf == Reply.FlashStart)
                {
                    buf = SendResponse(packet);
                    if (buf == Reply.FlashEnd)
                    {
                        break;
                    }
                }
                var handshake = HandShake();
                if(!handshake)
                {
                    return false;
                }
                continue;
            }
        }
        return true;
    }

    public bool StartScript()
    {
        var sendbytes = new byte[] { Command.Ready, Command.ScriptStart };
        var buf = SendResponse(sendbytes);
        return buf == Reply.ScriptAck;
    }

    public bool StopScript()
    {
        var sendbytes = new byte[] { Command.Ready, Command.ScriptStop };
        var buf = SendResponse(sendbytes);
        return buf == Reply.ScriptAck;
    }

    public byte GetVersion()
    {
        var sendbytes = new byte[] { Command.Ready, Command.Version };
        var buf = SendResponse(sendbytes);
        return buf;
    }

    public bool TriggerLED()
    {
        var sendbytes = new byte[] { Command.Ready, Command.LED };
        var buf = SendResponse(sendbytes);
        return buf == 0;
    }

    byte SendResponse(byte[] data)
    {
        lock (_stream)
        {
            _stream.Write(data.AsSpan());
            while (true)
            {
                if (_stream.CanRead)
                    break;
            }
            var buffer = new byte[1];
            _ = _stream.Read(buffer.AsSpan());
            return buffer[0];
        }
    }

    static byte[] GetReportBytes(SwitchReport r)
    {
        // Protocal packet structure:
        // bit 7 (highest):    0 = data byte, 1 = end flag
        // bit 6~0:            data (Big-Endian)
        // serialize data
        var serialized = new List<byte>();
        serialized.AddRange(BitConverter.GetBytes(r.Button));
        serialized.Reverse();
        serialized.Add(r.HAT);
        serialized.Add(r.LX);
        serialized.Add(r.LY);
        serialized.Add(r.RX);
        serialized.Add(r.RY);

        // generate packet
        var packet = new List<byte>();
        long n = 0;
        int bits = 0;
        foreach (var b in serialized)
        {
            n = (n << 8) | b;
            bits += 8;
            while (bits >= 7)
            {
                bits -= 7;
                packet.Add((byte)(n >> bits));
                n &= (1 << bits) - 1;
            }
        }
        packet[packet.Count - 1] |= 0x80;
        return packet.ToArray();
    }
}