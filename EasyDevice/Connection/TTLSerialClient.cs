using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.IO.Ports;

namespace ECDevice.Connection;

class TTLSerialClient : IConnection
{
    readonly string _connStr;
    readonly int _port;

    SerialPort _sport;

    readonly List<byte> _inBuffer = new();
    readonly List<byte[]> _outBuffer = new();
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
            Task.Run(() => StatusChanged?.Invoke(_status));
        }
    }

        public TTLSerialClient(string connStr, int port = 115200)
        {
            _connStr = connStr;
            _port = port;
        }

    public override void Connect(bool sayhello = true)
    {
        if (_t != null)
            return;

        _sport = new SerialPort(_connStr, _port);

        source = new CancellationTokenSource();
        var token = source.Token;
        _t = Task.Run(()=>
        {
            Loop();
        },
        token);
    }

    public override void Disconnect()
    {
        source?.Cancel();
    }

    void Loop()
    {
        try
        {
            byte[] inBuffer = new byte[2550];
            _sport.Open();
            _sport.DiscardInBuffer();
            _sport.DiscardOutBuffer();
            var stream = _sport.BaseStream;
            // sleep for a mount of data get in when open port
            if(OpenDelay)
                Thread.Sleep(700);
            Debug.WriteLine("left byte:" + _sport.BytesToRead.ToString());
            _sport.DiscardInBuffer();
            // say hello
            var hellobytes = new byte[] { Command.Ready, Command.Ready, Command.Hello };
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
                        // hello received
                        CurrentStatus = Status.Connected;
                    }
                    BytesReceived?.Invoke(_connStr, _inBuffer.ToArray());
#if DEBUG
                    if (_time != DateTime.MinValue)
                        Debug.WriteLine("Delay: " + (DateTime.Now - _time).TotalMilliseconds);
#endif
                }

                // write
                lock (_outBuffer)
                {
                    outBuffer.Clear();
                    _outBuffer.ForEach(item => outBuffer.AddRange(item));
                    _outBuffer.Clear();
                }
                if (outBuffer.Count > 0)
                {
                    var bytes = outBuffer.ToArray();
                    stream.Write(bytes, 0, bytes.Length);
                    BytesSent?.Invoke(_connStr, bytes);
                }

                // cpu optimization
                if (CPUOpt)
                    Thread.Sleep(1);
            }
        }
        catch (Exception)
        {
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
        lock (_outBuffer)
        {
            _outBuffer.Add(val);
            _time = DateTime.Now;
        }
    }
}
