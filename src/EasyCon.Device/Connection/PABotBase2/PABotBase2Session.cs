using System.Buffers.Binary;
using System.Security.Cryptography;

namespace EasyDevice.Connection.PABotBase2;

/// <summary>Single-owner reliable stream and message session. All I/O runs on the connection worker.</summary>
internal sealed class PABotBase2Session(IPABotBase2SerialPort port)
{
    private readonly PABotBase2PacketReader _reader = new();
    private readonly byte[] _readBuffer = new byte[512];
    private readonly Dictionary<byte, byte[]> _incoming = [];
    private readonly List<byte> _messages = [];
    private uint _session;
    private bool _needsReset;
    private byte _sendSequence;
    private byte _receiveSequence;
    private ushort _sendOffset;
    private ushort _receiveOffset;
    private int _incomingBytes;
    private int _packetSize = 24;
    private uint _peerFreeBytes;
    private byte? _ackOpcode;
    private byte[]? _ack;
    private byte _requestId;
    private byte? _waitingRequest;
    private byte[]? _reply;
    private byte _commandId;
    private byte? _activeCommand;
    private int _consoleConnected;

    internal string? FirmwareVersion { get; private set; }
    internal bool Ready { get; private set; }
    internal bool ConsoleConnected => Volatile.Read(ref _consoleConnected) != 0;

    internal void Connect(CancellationToken token)
    {
        port.Open();
        ResetStream(token, 3000);
        PABotBase2Protocol.RequireCompatible(ReadU32(Exchange(0x02, [], token)), "RSC");
        uint packetSize = ReadU32(Exchange(0x03, [], token));
        if (packetSize is < 16 or > 256 || ReadU32(Exchange(0x04, [], token)) == 0)
            throw new IOException("PABotBase2 返回了无效的接收窗口。");
        _packetSize = (int)Math.Min(packetSize, 128);
        _peerFreeBytes = ReadU32(Exchange(0x05, [], token));
        if (_peerFreeBytes < 16)
            throw new IOException("PABotBase2 接收缓冲区过小。");
        PABotBase2Protocol.RequireCompatible(QueryU32(0x20, token), "MLC");
        Send(PABotBase2Protocol.Message(0x25, 0, PABotBase2Protocol.U32(0)), token);
        FirmwareVersion = QueryU32(0x21, token).ToString(System.Globalization.CultureInfo.InvariantCulture);
        byte[] controllers = Query(0x24, 0x13, token);
        bool supportsProController = false;
        if (controllers.Length % 4 != 0)
            throw new IOException("PABotBase2 手柄类型列表损坏。");
        for (int offset = 0; offset < controllers.Length; offset += 4)
            supportsProController |= BinaryPrimitives.ReadUInt32LittleEndian(controllers.AsSpan(offset, 4)) == PABotBase2Protocol.WirelessProController;
        if (!supportsProController)
            throw new NotSupportedException("此 PABotBase2 固件不支持 NS1 无线 Pro 手柄；请使用 Pico W / Pico 2 W 的对应固件。");
        if (QueryU32(0x32, token) != PABotBase2Protocol.WirelessProController)
        {
            // CHANGE preserves pairing/settings. RESET_TO_CONTROLLER would erase them.
            Send(PABotBase2Protocol.Message(0x33, 0, PABotBase2Protocol.U32(PABotBase2Protocol.WirelessProController)), token);
            while (QueryU32(0x32, token) != PABotBase2Protocol.WirelessProController)
            {
                token.ThrowIfCancellationRequested();
                token.WaitHandle.WaitOne(50);
            }
        }
        if (QueryU32(0x26, token) == 0)
            throw new IOException("PABotBase2 命令队列不可用。");
        Ready = true;
        PollStatus(token);
        Stop(token);
    }

    internal void PollStatus(CancellationToken token)
    {
        byte[] status = Query(0x31, 0x14, token);
        if (status.Length != 12 || BinaryPrimitives.ReadUInt32LittleEndian(status) != PABotBase2Protocol.WirelessProController)
            throw new IOException("PABotBase2 无线手柄状态长度错误。");
        Volatile.Write(ref _consoleConnected, (status[4] & 3) == 3 ? 1 : 0);
    }

    internal void SendReport(byte[] buttons, CancellationToken token)
    {
        if (buttons.Length != 10)
            throw new ArgumentException("Expected a 10-byte OEM controller state.", nameof(buttons));
        byte[] payload = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, PABotBase2Protocol.HoldMilliseconds);
        buttons.CopyTo(payload, 2);
        _activeCommand = unchecked(++_commandId);
        // Replace only when the next complete state arrives, avoiding a neutral gap during a hold.
        byte[] command = PABotBase2Protocol.Message(0x97, _activeCommand.Value, payload);
        Send([.. PABotBase2Protocol.Message(0x42, 0), .. command], token);
    }

    internal void Stop(CancellationToken token)
    {
        _activeCommand = null; // Cancellation can legitimately report older commands as dropped.
        if (_needsReset)
        {
            // A timeout may leave half an MLC message or an unacknowledged packet on the wire.
            // A new session discards that ambiguous stream before issuing any release commands.
            ResetStream(token);
            _peerFreeBytes = ReadU32(Exchange(0x05, [], token));
        }
        Send(PABotBase2Protocol.Message(0x41, 0), token);
        SendReport(PABotBase2Protocol.Buttons(new SwitchReport()), token);
    }

    private void ResetStream(CancellationToken token, int timeoutMilliseconds = 1000)
    {
        _session = BinaryPrimitives.ReadUInt32LittleEndian(RandomNumberGenerator.GetBytes(4));
        _reader.Clear();
        _incoming.Clear();
        _messages.Clear();
        _incomingBytes = 0;
        _sendSequence = _receiveSequence = 0;
        _sendOffset = _receiveOffset = 0;
        Exchange(0x01, PABotBase2Protocol.U32(_session), token, timeoutMilliseconds);
        _needsReset = false;
    }

    internal void Pump(CancellationToken token)
    {
        try { ReceivePackets(token); }
        catch
        {
            _needsReset = true;
            throw;
        }
    }

    private void ReceivePackets(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int count = port.Read(_readBuffer);
        _reader.Append(_readBuffer.AsSpan(0, count));
        while (_reader.TryRead(_session, out PABotBase2Packet packet))
        {
            token.ThrowIfCancellationRequested();
            if (packet.Opcode == _ackOpcode && packet.Sequence == _sendSequence)
            {
                _ack = packet.Payload;
            }
            else if (packet.Opcode == 0x12)
            {
                ReceiveStream(packet);
            }
            else if (packet.Opcode is 0x10 or 0x11)
            {
                throw new IOException("PABotBase2 可靠连接已重置，请重新连接设备。");
            }
        }
    }

    private byte[] Exchange(byte opcode, byte[] payload, CancellationToken token, int timeoutMilliseconds = 1000)
    {
        _ackOpcode = (byte)(opcode | 0x40);
        _ack = null;
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        long retryAt = 0;
        bool sent = false;
        try
        {
            while (_ack == null)
            {
                token.ThrowIfCancellationRequested();
                long now = Environment.TickCount64;
                if (now >= deadline)
                    throw new TimeoutException($"PABotBase2 串口请求 0x{opcode:X2} 超时；请检查固件、USB 连接和串口占用。");
                if (now >= retryAt)
                {
                    // RESET always uses its original opcode and fixed CRC seed, including retries.
                    byte wireOpcode = sent && opcode != 0x01 ? (byte)(opcode | 0x80) : opcode;
                    port.Write(PABotBase2Protocol.Packet(_sendSequence, wireOpcode, payload, _session));
                    sent = true;
                    retryAt = now + 100;
                }
                Pump(token);
            }
            _sendSequence = unchecked((byte)(_sendSequence + 1));
            return _ack;
        }
        catch
        {
            _needsReset = true;
            throw;
        }
        finally { _ackOpcode = null; }
    }

    private void Send(byte[] message, CancellationToken token)
    {
        try { SendMessageBytes(message, token); }
        catch
        {
            _needsReset = true;
            throw;
        }
    }

    private void SendMessageBytes(byte[] message, CancellationToken token)
    {
        int position = 0;
        long deadline = Environment.TickCount64 + 2000;
        while (position < message.Length)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(message.Length - position, _packetSize - 10);
            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException("PABotBase2 接收缓冲区持续繁忙。");
            if (_peerFreeBytes < count)
            {
                // Empty stream packets advance only the packet sequence and refresh receiver credit.
                byte[] probe = new byte[2];
                BinaryPrimitives.WriteUInt16LittleEndian(probe, _sendOffset);
                _peerFreeBytes = ReadU32(Exchange(0x12, probe, token));
                token.WaitHandle.WaitOne(10);
                continue;
            }
            byte[] payload = new byte[count + 2];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, _sendOffset);
            message.AsSpan(position, count).CopyTo(payload.AsSpan(2));
            _peerFreeBytes = ReadU32(Exchange(0x12, payload, token));
            _sendOffset = unchecked((ushort)(_sendOffset + count));
            position += count;
        }
    }

    private void ReceiveStream(PABotBase2Packet packet)
    {
        if (packet.Payload.Length < 2)
            throw new IOException("PABotBase2 流数据包过短。");
        int distance = unchecked((byte)(packet.Sequence - _receiveSequence));
        if (distance < 128 && !_incoming.ContainsKey(packet.Sequence))
        {
            int count = packet.Payload.Length - 2;
            ushort offset = BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload);
            if (unchecked((ushort)(offset + count - _receiveOffset)) > PABotBase2Protocol.ReceiveWindowBytes ||
                _incomingBytes + count > PABotBase2Protocol.ReceiveWindowBytes)
                throw new IOException("PABotBase2 流数据超出接收窗口。");
            _incoming.Add(packet.Sequence, packet.Payload);
            _incomingBytes += count;
        }
        // Old retransmissions must still be ACKed, but must never deliver their messages twice.
        port.Write(PABotBase2Protocol.Packet(packet.Sequence, 0x52,
            PABotBase2Protocol.U32((uint)(PABotBase2Protocol.ReceiveWindowBytes - _incomingBytes)), _session));
        while (_incoming.Remove(_receiveSequence, out byte[]? payload))
        {
            int count = payload.Length - 2;
            if (count != 0 && BinaryPrimitives.ReadUInt16LittleEndian(payload) != _receiveOffset)
                throw new IOException("PABotBase2 流偏移不连续。");
            _receiveSequence = unchecked((byte)(_receiveSequence + 1));
            _receiveOffset = unchecked((ushort)(_receiveOffset + count));
            _incomingBytes -= count;
            for (int index = 2; index < payload.Length; index++)
                _messages.Add(payload[index]);
            DrainMessages();
        }
    }

    private void DrainMessages()
    {
        while (_messages.Count >= 4)
        {
            int length = _messages[0] | (_messages[1] << 8);
            if (length < 4)
                throw new IOException("PABotBase2 消息长度错误。");
            if (_messages.Count < length)
                return;
            byte opcode = _messages[2];
            byte id = _messages[3];
            if (opcode is >= 0x10 and <= 0x14 && id == _waitingRequest)
                _reply = _messages.GetRange(0, length).ToArray();
            else if (opcode == 0x40 && id == _activeCommand)
                throw new IOException("PABotBase2 拒绝了当前手柄输入，请重新连接设备。");
            else if (opcode == 0x37)
                Volatile.Write(ref _consoleConnected, 0);
            _messages.RemoveRange(0, length);
        }
    }

    private uint QueryU32(byte opcode, CancellationToken token) => ReadU32(Query(opcode, 0x12, token));

    private byte[] Query(byte opcode, byte responseOpcode, CancellationToken token)
    {
        _requestId = _requestId == 255 ? (byte)1 : (byte)(_requestId + 1);
        _waitingRequest = _requestId;
        _reply = null;
        long deadline = Environment.TickCount64 + 1500;
        try
        {
            Send(PABotBase2Protocol.Message(opcode, _requestId), token);
            while (_reply == null)
            {
                if (Environment.TickCount64 >= deadline)
                    throw new TimeoutException($"PABotBase2 消息 0x{opcode:X2} 未响应。");
                Pump(token);
            }
            if (_reply[2] != responseOpcode)
                throw new IOException($"PABotBase2 消息 0x{opcode:X2} 被拒绝或返回了错误类型。");
            return _reply[4..];
        }
        finally { _waitingRequest = null; }
    }

    private static uint ReadU32(byte[] data)
    {
        if (data.Length != 4)
            throw new IOException("PABotBase2 整数响应长度错误。");
        return BinaryPrimitives.ReadUInt32LittleEndian(data);
    }
}