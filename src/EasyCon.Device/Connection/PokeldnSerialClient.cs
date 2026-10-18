using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;

namespace EasyDevice.Connection;

/// <summary>
/// pokeldn 手柄固件（Decryptu/pokeldn <c>firmware/pad</c>，经典 ESP32 蓝牙手柄）的串口连接实现。
/// 帧协议为依据公开规范的独立实现（未复制上游代码）：
/// <c>A5 5A | len(u16 LE，= type+payload 长度) | type | payload | checksum(type 与 payload 逐字节求和取低 8 位)</c>。
/// 识别：以 921600 波特率周期发送 STATUS 查询（type 0x02），收到合法 STATUS_REPLY（type 0x82）即判定为该固件；
/// NintendoSwitch 的 A5 A5 81 握手在该固件上不会得到任何回应，因此两种协议可在同一串口上安全互探。
/// 报文：<see cref="Write"/> 收到的 8 字节 7-bit-packed SwitchReport（7 字节数据打包，末字节带结束标志）
/// 在此解包，映射为 pokeldn
/// 8 字节报告帧（type 0x01）下发——两协议的按键位、HAT 与摇杆取值完全一致，仅需换容器。
/// </summary>
public class PokeldnSerialClient : IConnection
{
    public const int DefaultBaudRate = 921600;

    const byte Magic0 = 0xA5;
    const byte Magic1 = 0x5A;
    const byte TypeReport = 0x01;       // host -> board：8 字节手柄报告
    const byte TypeStatusQuery = 0x02;  // host -> board：状态查询
    const byte TypeStatusReply = 0x82;  // board -> host：状态应答

    const int MaxPayloadLength = 600;

    /// <summary>握手总预算必须小于 NintendoSwitch._TryConnect 的 1 秒等待，否则外层会先按超时断开。</summary>
    const int HandshakeTimeoutMs = 900;
    const int HandshakeReplyWaitMs = 300;

    readonly string _connStr;

    SerialPort _sport = null!;

    readonly ConcurrentQueue<(byte[] Frame, byte[] Original)> _outQueue = new();

    Status _status = Status.Connecting;

    public override event BytesTransferedHandler BytesSent;
    public override event BytesTransferedHandler BytesReceived;
    public override event StatusChangedHandler StatusChanged;

    Task _t = null!;
    CancellationTokenSource source = null!;

    public override Status CurrentStatus
    {
        get => _status;

        protected set
        {
            if (_status == value)
                return;
            _status = value;
            var status = value;   // 快照：闭包读字段可能已被后续翻转覆盖
            Task.Run(() => StatusChanged?.Invoke(status));
        }
    }

    public PokeldnSerialClient(string connStr, int baudrate = DefaultBaudRate)
    {
        _connStr = connStr;
        _sport = new SerialPort(connStr, baudrate)
        {
            // pokeldn 主机端约定：打开串口不得拉 DTR/RTS——CP2102 的 DTR 会复位 ESP32。
            // 因此与 TTLSerialClient 相反：直接以撤销状态打开，不做先拉高再撤销的 dance。
            DtrEnable = false,
            RtsEnable = false,
            ReadTimeout = 50,
            WriteTimeout = 200,
        };
    }

    public override void Connect()
    {
        if (_t != null)
            return;

        source = new CancellationTokenSource();
        var token = source.Token;
        _t = Task.Run(() =>
        {
            Loop();
        },
        token);
    }

    public override void Disconnect()
    {
        source?.Cancel();
        ClearQueue();
        // 有界等待循环退出：其 finally 会 Close 串口，避免与新连接的 Open 竞争同一 COM 口。
        var t = _t;
        if (t != null)
        {
            try { t.Wait(1500); }
            catch { /* 任务已故障结束也视为退出（循环 finally 已关闭串口） */ }
        }
    }

    /// <summary>
    /// 连接循环：先探测固件（周期发 STATUS 查询直至收到 STATUS_REPLY），识别成功进入联机循环；
    /// 超时判定非 pokeldn 固件并报 Error，由上层继续尝试其他协议。
    /// </summary>
    void Loop()
    {
        try
        {
            _sport.Open();
            _sport.DiscardInBuffer();
            _sport.DiscardOutBuffer();

            if (!Probe())
                return;

            CurrentStatus = Status.Connected;
            RunLoop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{_connStr}] PokeldnSerialClient 异常退出: {ex}");
            CurrentStatus = Status.Error;
        }
        finally
        {
            _sport.Close();
        }
    }

    /// <summary>探测 pokeldn 固件：周期发送 STATUS 查询，收到合法 STATUS_REPLY 即成功。</summary>
    bool Probe()
    {
        var query = BuildFrame(TypeStatusQuery);
        var deadline = Environment.TickCount64 + HandshakeTimeoutMs;
        var buffer = new List<byte>();
        while (Environment.TickCount64 < deadline && !source.IsCancellationRequested)
        {
            _sport.DiscardInBuffer();
            buffer.Clear();
            SendFrame(query);

            // 给固件一段响应时间，再解析收到的字节
            var replyDeadline = Environment.TickCount64 + HandshakeReplyWaitMs;
            while (Environment.TickCount64 < replyDeadline && !source.IsCancellationRequested)
            {
                if (TryReadFrame(buffer, out var type) && type == TypeStatusReply)
                    return true;
                Thread.Sleep(5);
            }
        }
        Debug.WriteLine($"[{_connStr}] pokeldn STATUS 无应答，判定非 pokeldn 固件");
        return false;
    }

    /// <summary>联机循环：把排队的报告帧写向固件，同时收下固件状态帧透传给订阅方。</summary>
    void RunLoop()
    {
        var buffer = new List<byte>();
        var outBuffer = new List<byte>();
        while (!source.IsCancellationRequested)
        {
            while (TryReadFrame(buffer, out var type))
            {
                if (type == TypeStatusReply)
                    BytesReceived?.Invoke(_connStr, [.. buffer]);
            }

            outBuffer.Clear();
            var sentOriginals = new List<byte>();
            while (_outQueue.TryDequeue(out var item))
            {
                outBuffer.AddRange(item.Frame);
                sentOriginals.AddRange(item.Original);
            }
            if (outBuffer.Count > 0)
            {
                _sport.BaseStream.Write(outBuffer.ToArray(), 0, outBuffer.Count);
                // BytesSent 语义对齐 TTLSerialClient：回传未翻译的 HID 包层载荷（队列出队输出），
                // 而非实际写串口的 pokeldn 帧——上游 ResetAndWaitForWriteAsync 靠它 EndsWith(中立包) 判定串口写出。
                BytesSent?.Invoke(_connStr, sentOriginals.ToArray());
            }

            Thread.Sleep(1);
        }

        // 退出前回中立报告，避免手柄残留按键状态
        try
        {
            SendFrame(BuildFrame(TypeReport, NeutralReportPayload));
        }
        catch
        {
            // 串口可能已被外层关闭，尽力而为
        }
    }

    /// <summary>从串口收取字节并同步到下一帧；返回帧类型，无完整帧返回 false。</summary>
    bool TryReadFrame(List<byte> buffer, out byte type)
    {
        type = 0;
        while (_sport.BytesToRead > 0)
        {
            var chunk = new byte[_sport.BytesToRead];
            var read = _sport.BaseStream.Read(chunk, 0, chunk.Length);
            if (read <= 0)
                break;
            buffer.AddRange(chunk.Take(read));
        }

        while (true)
        {
            // 同步到帧头
            var start = -1;
            for (var i = 0; i + 1 < buffer.Count; i++)
            {
                if (buffer[i] == Magic0 && buffer[i + 1] == Magic1)
                {
                    start = i;
                    break;
                }
            }
            if (start < 0)
            {
                // 保留末字节：可能是被截断的 0xA5
                if (buffer.Count > 1)
                    buffer.RemoveRange(0, buffer.Count - 1);
                return false;
            }
            if (start > 0)
                buffer.RemoveRange(0, start);

            // A5 5A + len(2) + type + checksum 至少 6 字节
            if (buffer.Count < 6)
                return false;

            var len = buffer[2] | (buffer[3] << 8);
            if (len is < 1 or > MaxPayloadLength)
            {
                buffer.RemoveAt(0);   // 长度非法：跳过该帧头继续同步
                continue;
            }

            var total = 5 + len;      // 帧头 4 + (type+payload) len + checksum 1
            if (buffer.Count < total)
                return false;         // 帧未收全，等下一批字节

            var checksum = buffer[4];
            for (var i = 5; i < 4 + len; i++)
                checksum += buffer[i];
            if (checksum != buffer[total - 1])
            {
                buffer.RemoveAt(0);   // 校验失败：跳过该帧头继续同步
                continue;
            }

            type = buffer[4];
            buffer.RemoveRange(0, total);
            return true;
        }
    }

    public override void Write(params byte[] val)
    {
        if (_t == null)
            return;

        var frame = TranslateReport(val);
        if (frame == null)
        {
            // EasyCon 命令帧（A5 xx 等）在 pokeldn 固件上无对应操作：
            // 静默忽略，避免把命令字节当原始数据写坏链路。
            return;
        }
        _outQueue.Enqueue((frame, val));
    }

    public override void ClearQueue()
    {
        while (_outQueue.TryDequeue(out _))
        {
        }
    }

    /// <summary>中立报告：无按键、HAT 居中（8）、双摇杆居中（128）、vendor 0。</summary>
    static byte[] NeutralReportPayload => [0, 0, 8, 128, 128, 128, 128, 0];

    /// <summary>
    /// 上层 8 字节 7-bit-packed SwitchReport 报文（7 字节数据打包，末字节带结束标志）→ pokeldn 报告帧；
    /// 其余（命令帧等）返回 null。纯函数，供测试直接断言。
    /// </summary>
    public static byte[]? TranslateReport(byte[]? packet)
    {
        // EasyCon 数据包：首字节 bit7=0（数据），末字节 bit7=1（结束标志），命令帧以 A5 开头必不含此模式
        if (packet == null || packet.Length != 8 || (packet[0] & 0x80) != 0 || (packet[^1] & 0x80) == 0)
            return null;

        // 逆 7-bit 打包：8×7 = 56 位大端重组出 7 个原始字节（Button BE + HAT + LX + LY + RX + RY）
        ulong acc = 0;
        foreach (var b in packet)
            acc = (acc << 7) | (byte)(b & 0x7F);
        Span<byte> serialized = stackalloc byte[7];
        for (var i = 0; i < 7; i++)
            serialized[i] = (byte)(acc >> (8 * (6 - i)));

        var button = (ushort)((serialized[0] << 8) | serialized[1]);
        // 位定义与 SwitchButton 完全一致（Y=1 B=2 A=4 X=8 L R ZL ZR MINUS PLUS LCLICK RCLICK HOME CAPTURE）
        byte[] payload =
        [
            (byte)button,
            (byte)(button >> 8),   // pokeldn 为 u16 LE
            serialized[2],         // HAT：两协议取值相同（0=上顺时针至 7=左上，8=中）
            serialized[3],         // LX：128 居中，两协议相同
            serialized[4],         // LY
            serialized[5],         // RX
            serialized[6],         // RY
            0,                     // vendor
        ];
        return BuildFrame(TypeReport, payload);
    }

    /// <summary>组装 pokeldn 帧（帧头 4 + type 1 + payload + checksum 1）。纯函数，供测试直接断言。</summary>
    public static byte[] BuildFrame(byte type, params byte[] payload)
    {
        var frame = new byte[6 + payload.Length];
        frame[0] = Magic0;
        frame[1] = Magic1;
        var len = 1 + payload.Length;
        frame[2] = (byte)len;
        frame[3] = (byte)(len >> 8);
        frame[4] = type;
        var checksum = type;
        for (var i = 0; i < payload.Length; i++)
        {
            frame[5 + i] = payload[i];
            checksum += payload[i];
        }
        frame[^1] = checksum;
        return frame;
    }

    void SendFrame(byte[] frame)
    {
        _sport.BaseStream.Write(frame, 0, frame.Length);
        // 探测/退出用的协议控制帧不走报告队列，BytesSent 直发帧本身（同 TTLSerialClient 对 hello 的处理）。
        BytesSent?.Invoke(_connStr, frame);
    }
}