using EasyDevice.Connection.PABotBase2;
using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace EasyCon.Device.Tests;

// A firmware-side state machine: checks packet sequence and stream offsets separately,
// deduplicates retries, reassembles MLC requests, and sends OEM status tagged with controller ID.
internal sealed class FakePABotBase2Port : IPABotBase2SerialPort
{
    private readonly BlockingCollection<byte> _read = new();
    private readonly List<byte> _messages = [];
    private uint _session;
    private byte _hostSequence;
    private byte _deviceSequence;
    private ushort _hostOffset;
    private ushort _deviceOffset;
    private byte? _runningCommand;
    private bool _replace;
    internal readonly ConcurrentQueue<byte[]> Writes = new();
    internal readonly ConcurrentQueue<byte[]> Messages = new();
    internal readonly ConcurrentQueue<byte> Acknowledgements = new();
    internal uint RscVersion = 2026061800;
    internal uint MlcVersion = 2026061804;
    internal uint Controller = 0x1180;
    internal uint SupportedController = 0x1180;
    internal uint PacketSize = 24;
    internal int ReadChunk = 7;
    internal int ReplyChunk = 3;
    internal bool ReverseReplies;
    internal bool DuplicateReplies;
    internal volatile bool Silent;
    internal volatile bool WithholdStreamAcks;
    internal volatile bool WrongAckOnce;
    internal volatile bool DropAckOnce;
    internal volatile bool RejectCommands;
    internal volatile bool CorruptReplyOnce;
    internal volatile byte ConsoleStatus = 7;
    internal uint? NextCredit;
    internal bool Opened;
    internal volatile bool Disposed;

    public void Open() => Opened = true;
    public void Dispose() => Disposed = true;

    public int Read(byte[] buffer)
    {
        if (!_read.TryTake(out byte first, 10))
            return 0;
        buffer[0] = first;
        int count = 1;
        while (count < Math.Min(buffer.Length, ReadChunk) && _read.TryTake(out byte next))
            buffer[count++] = next;
        return count;
    }

    public void Write(byte[] packet)
    {
        Assert.That(Opened, Is.True);
        Assert.That(Disposed, Is.False);
        Writes.Enqueue(packet.ToArray());
        if (Silent)
            return;
        Assert.That(packet[0], Is.EqualTo(0x81));
        Assert.That(packet[2] == 0 ? 256 : packet[2], Is.EqualTo(packet.Length));
        byte sequence = packet[1];
        byte opcode = (byte)(packet[3] & 0x7f);
        uint seed = packet[3] == 1 ? uint.MaxValue : _session;
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(packet.Length - 4)),
            Is.EqualTo(PABotBase2Protocol.Crc32C(packet.AsSpan(0, packet.Length - 4), seed)));
        byte[] payload = packet[4..^4];
        if (opcode == 0x52)
        {
            Acknowledgements.Enqueue(sequence);
            return;
        }
        if (opcode == 1)
        {
            Assert.That(sequence, Is.Zero);
            _session = BinaryPrimitives.ReadUInt32LittleEndian(payload);
            _hostSequence = 1;
            _deviceSequence = 0;
            _hostOffset = _deviceOffset = 0;
            _messages.Clear();
            _runningCommand = null;
            _replace = false;
            Enqueue(PABotBase2Protocol.Packet(sequence, 0x41, [], _session));
            return;
        }
        if (opcode is >= 2 and <= 5)
        {
            if (sequence == _hostSequence)
                _hostSequence = unchecked((byte)(_hostSequence + 1));
            uint value = opcode switch { 2 => RscVersion, 3 => PacketSize, 4 => 128, _ => 16384 };
            Enqueue(PABotBase2Protocol.Packet(sequence, (byte)(opcode | 0x40), PABotBase2Protocol.U32(value), _session));
            return;
        }
        Assert.That(opcode, Is.EqualTo(0x12));
        Assert.That(packet.Length, Is.LessThanOrEqualTo(PacketSize));
        bool deliver = sequence == _hostSequence;
        if (deliver)
        {
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(payload), Is.EqualTo(_hostOffset));
            _hostSequence = unchecked((byte)(_hostSequence + 1));
            _hostOffset = unchecked((ushort)(_hostOffset + payload.Length - 2));
            _messages.AddRange(payload[2..]);
        }
        else
        {
            Assert.That(sequence, Is.EqualTo(unchecked((byte)(_hostSequence - 1))), "Only an outstanding retry may repeat.");
            Assert.That(packet[3] & 0x80, Is.Not.Zero);
        }
        if (!WithholdStreamAcks)
        {
            if (WrongAckOnce)
            {
                WrongAckOnce = false;
                Enqueue(PABotBase2Protocol.Packet(unchecked((byte)(sequence + 1)), 0x52, PABotBase2Protocol.U32(16384), _session));
            }
            else if (DropAckOnce)
            {
                DropAckOnce = false;
            }
            else
            {
                uint credit = NextCredit ?? 16384;
                NextCredit = null;
                Enqueue(PABotBase2Protocol.Packet(sequence, 0x52, PABotBase2Protocol.U32(credit), _session));
            }
        }
        if (deliver)
            DrainMessages();
    }

    private void DrainMessages()
    {
        while (_messages.Count >= 4)
        {
            int length = _messages[0] | (_messages[1] << 8);
            Assert.That(length, Is.GreaterThanOrEqualTo(4));
            if (_messages.Count < length)
                return;
            byte[] message = _messages.GetRange(0, length).ToArray();
            _messages.RemoveRange(0, length);
            Messages.Enqueue(message);
            byte opcode = message[2];
            byte id = message[3];
            switch (opcode)
            {
                case 0x20: Reply(0x12, id, PABotBase2Protocol.U32(MlcVersion)); break;
                case 0x21: Reply(0x12, id, PABotBase2Protocol.U32(2026101800)); break;
                case 0x24: Reply(0x13, id, PABotBase2Protocol.U32(SupportedController)); break;
                case 0x25: break;
                case 0x26: Reply(0x12, id, PABotBase2Protocol.U32(32)); break;
                case 0x31: Reply(0x14, id, [.. PABotBase2Protocol.U32(Controller), ConsoleStatus, 1, 1, 2, 3, 4, 5, 6]); break;
                case 0x32: Reply(0x12, id, PABotBase2Protocol.U32(Controller)); break;
                case 0x33: Controller = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(4)); break;
                case 0x41:
                    DropRunningCommand();
                    break;
                case 0x42:
                    _replace = true;
                    break;
                case 0x97:
                    if (_replace)
                        DropRunningCommand();
                    _replace = false;
                    if (RejectCommands)
                        Reply(0x40, id, []);
                    else
                        _runningCommand = id;
                    break;
                default: throw new AssertionException($"Unexpected MLC opcode {opcode:X2}");
            }
        }
    }

    private void DropRunningCommand()
    {
        if (_runningCommand.HasValue)
            Reply(0x40, _runningCommand.Value, []);
        _runningCommand = null;
    }

    internal void Reply(byte opcode, byte id, byte[] payload)
    {
        byte[] message = PABotBase2Protocol.Message(opcode, id, payload);
        List<byte[]> packets = [];
        for (int position = 0; position < message.Length;)
        {
            int count = Math.Min(ReplyChunk, message.Length - position);
            byte[] stream = new byte[count + 2];
            BinaryPrimitives.WriteUInt16LittleEndian(stream, _deviceOffset);
            message.AsSpan(position, count).CopyTo(stream.AsSpan(2));
            packets.Add(PABotBase2Protocol.Packet(_deviceSequence, 0x12, stream, _session));
            _deviceSequence = unchecked((byte)(_deviceSequence + 1));
            _deviceOffset = unchecked((ushort)(_deviceOffset + count));
            position += count;
        }
        if (ReverseReplies)
            packets.Reverse();
        if (CorruptReplyOnce)
        {
            CorruptReplyOnce = false;
            byte[] corrupt = packets[0].ToArray();
            corrupt[^1] ^= 1;
            Enqueue(corrupt);
        }
        foreach (byte[] packet in packets)
            Enqueue(packet);
        if (DuplicateReplies)
        {
            foreach (byte[] packet in packets)
                Enqueue(packet);
        }
    }

    private void Enqueue(byte[] packet)
    {
        foreach (byte value in packet)
            _read.Add(value);
    }
}