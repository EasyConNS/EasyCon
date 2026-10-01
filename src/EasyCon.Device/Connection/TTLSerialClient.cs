using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;

namespace EasyDevice.Connection;

class TTLSerialClient : IConnection
{
    readonly string _connStr;
    readonly int _port;

    SerialPort _sport;

    readonly List<byte> _inBuffer = new();
    readonly ConcurrentQueue<byte[]> _outQueue = new();
    DateTime _time = DateTime.MinValue;
    Status _status = Status.Connecting;

    public override event BytesTransferedHandler BytesSent;
    public override event BytesTransferedHandler BytesReceived;
    public override event StatusChangedHandler StatusChanged;

    Task _t;
    CancellationTokenSource source;


    TimeSpan _Timeout => TimeSpan.FromMilliseconds(Timeout);

    public double Timeout { get; set; } = 200;

    public bool Connected { get; protected set; }

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

    public TTLSerialClient(string connStr, int port = 115200)
    {
        _connStr = connStr;
        _port = port;
    }

    public override void Connect()
    {
        if (_t != null)
            return;

        _sport = new SerialPort(_connStr, _port);
        // 非 Windows 平台打开串口时系统会先拉高 DTR/RTS，若随后先撤销 DTR，会短暂出现
        // “RTS 高、DTR 低”，经 ESP32 等板子的自动下载电路拉低 EN 导致单片机复位。
        // 因此以拉高状态打开，再在 Open 后按“先 RTS、后 DTR”的顺序撤销（见 Loop）。
        if (!OperatingSystem.IsWindows())
        {
            _sport.DtrEnable = true;
            _sport.RtsEnable = true;
        }

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
        // 有界等待写循环退出：其 finally 会 Close 串口。不同步等待时，
        // 重连路径上新 client 的 _sport.Open() 会与旧 Close 竞争同一 COM 口
        // （Windows 独占打开），造成间歇性 UnauthorizedAccessException。
        var t = _t;
        if (t != null)
        {
            try { t.Wait(1500); }
            catch { /* 任务已故障结束也视为退出（循环 finally 已关闭串口） */ }
        }
    }

    void Loop()
    {
        try
        {
            byte[] inBuffer = new byte[2550];
            _sport.Open();
            if (!OperatingSystem.IsWindows())
            {
                _sport.RtsEnable = false;
                _sport.DtrEnable = false;
            }
            _sport.DiscardInBuffer();
            _sport.DiscardOutBuffer();
            var stream = _sport.BaseStream;

            Debug.WriteLine("left byte:" + _sport.BytesToRead.ToString());
            _sport.DiscardInBuffer();
            // say hello
            var hellobytes = new byte[] { EzDvCommand.Ready, EzDvCommand.Ready, EzDvCommand.Hello };
            stream.Write(hellobytes, 0, hellobytes.Length);
            BytesSent?.Invoke(_connStr, hellobytes);
            var outBuffer = new List<byte>();
            while (!source.IsCancellationRequested)
            {
                // read
                if (_sport.BytesToRead > 0)
                {
                    int count = stream.Read(inBuffer, 0, inBuffer.Length);
#if DEBUG
                    Debug.WriteLine($"[{_connStr}] " + string.Join(" ", inBuffer.Take(count).Select(b => b.ToString("X2"))));
#endif
                    lock (_inBuffer)
                    {
                        _inBuffer.Clear();
                        _inBuffer.AddRange(inBuffer.Take(count));
                    }

                    if (inBuffer[0] == Reply.Hello)
                    {
                        // hello received（状态翻转经 setter 统一触发，此处不再重复发事件）
                        CurrentStatus = Status.Connected;
                    }
                    BytesReceived?.Invoke(_connStr, _inBuffer.ToArray());
#if DEBUG
                    if (_time != DateTime.MinValue)
                        Debug.WriteLine("Delay: " + (DateTime.Now - _time).TotalMilliseconds);
#endif
                }

                // write
                outBuffer.Clear();
                while (_outQueue.TryDequeue(out var item))
                    outBuffer.AddRange(item);
                if (outBuffer.Count > 0)
                {
                    var bytes = outBuffer.ToArray();
                    stream.Write(bytes, 0, bytes.Length);
                    BytesSent?.Invoke(_connStr, bytes);
                }

                // cpu optimization
                Thread.Sleep(1);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{_connStr}] TTLSerialClient 异常退出: {ex}");
            CurrentStatus = Status.Error;
        }
        finally
        {
            _sport.Close();
        }
    }

    public override void Write(params byte[] val)
    {
        if (_t == null)
            return;
#if DEBUG
        Debug.WriteLine("Output: " + string.Join(" ", val.Select(b => b.ToString("X2"))));
#endif
        _outQueue.Enqueue(val);
        _time = DateTime.Now;
    }

    public override void ClearQueue()
    {
        while (_outQueue.TryDequeue(out _))
        {
        }
    }
}