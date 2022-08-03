using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.IO.Ports;

namespace ECDevice.Connection
{
    public class TTLSerialClient : IConnClient
    {
        const bool DEBUG_MESSAGE = false;

        readonly string _connStr;
        readonly int _port;
        bool _sayhello = true;

        SerialPort _sport;

        List<byte> _inBuffer = new();
        List<byte[]> _outBuffer = new();
        DateTime _time = DateTime.MinValue;
        Status _status = Status.Connecting;

        public override event BytesTransferedHandler BytesSent;
        public override event BytesTransferedHandler BytesReceived;
        public override event StatusChangedHandler StatusChanged;

        Task _t;
        CancellationTokenSource source;
        CancellationToken token;

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
            _sayhello = sayhello;

            source = new CancellationTokenSource();
            token = source.Token;
            _t = Task.Run(()=>
            {
                Loop();
            },
            token);
        }

        public override void Disconnect()
        {
            _sport?.Close();
            if (source != null)
            {
                source.Cancel();
            }
        }

        void Loop()
        {
            try
            {
                _sport.Open();

                // say hello
                if (_sayhello)
                {
                    var hellobytes = new byte[] { Command.Ready, Command.Ready, Command.Hello };
                    _sport.Write(hellobytes, 0, hellobytes.Length);
                    BytesSent?.Invoke(_connStr, hellobytes);
                }
                else
                    CurrentStatus = Status.ConnectedUnsafe;
                bool hellocheck = _sayhello;

                byte[] inBuffer = new byte[255];
                var outBuffer = new List<byte>();
                while (true)
                {
                    // read
                    if (_sport.BytesToRead > 0)
                    {
                        int count = _sport.Read(inBuffer, 0, inBuffer.Length);
                        if (DEBUG_MESSAGE)
                            Debug.WriteLine($"[{_connStr}] " + string.Join(" ", inBuffer.Take(count).Select(b => b.ToString("X2"))));
                        lock (_inBuffer)
                        {
                            _inBuffer.Clear();
                            _inBuffer.AddRange(inBuffer.Take(count));
                        }
                        if (hellocheck && _inBuffer[0] == Reply.Hello)
                        {
                            // hello received
                            hellocheck = false;
                            CurrentStatus = Status.Connected;
                        }
                        BytesReceived?.Invoke(_connStr, _inBuffer.ToArray());
                        if (DEBUG_MESSAGE && _time != DateTime.MinValue)
                            Debug.WriteLine("Delay: " + (DateTime.Now - _time).TotalMilliseconds);
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
                        _sport.Write(bytes, 0, bytes.Length);
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
            if (DEBUG_MESSAGE)
                Debug.WriteLine("Output: " + string.Join(" ", val.Select(b => b.ToString("X2"))));
            lock (_outBuffer)
            {
                _outBuffer.Add(val.ToArray());
                _time = DateTime.Now;
            }
        }
    }
}
