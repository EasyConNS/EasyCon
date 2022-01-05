using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.IO.Ports;

namespace ECDevice.Arduino
{
    public class ConClient
    {
        const bool DEBUG_MESSAGE = false;

        string _connStr;
        int _port;

        SerialPort _sport;

        bool _sayhello = true;
        public bool CpuOpt = true;

        public delegate void BytesTransferedHandler(string portName, byte[] bytes);
        public delegate void StatusChangedHandler(Status status);

        public event BytesTransferedHandler BytesSent;
        public event BytesTransferedHandler BytesReceived;
        public event StatusChangedHandler StatusChanged;

        List<byte> _inBuffer = new List<byte>();
        List<byte[]> _outBuffer = new List<byte[]>();
        DateTime _time = DateTime.MinValue;
        Status _status = Status.Connecting;

        Task _t;
        CancellationTokenSource source;
        CancellationToken token;

        public Status CurrentStatus
        {
            get => _status;

            private set
            {
                if (_status == value)
                    return;
                _status = value;
                Task.Run(() => StatusChanged?.Invoke(_status));
            }
        }

        public ConClient(string connStr, int port = 9600)
        {
            _connStr = connStr;
            _port = port;
        }

        public void Connect(bool sayhello = true)
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

        public void Disconnect()
        {
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
                List<byte> outBuffer = new List<byte>();
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
                    if (CpuOpt)
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

        public void Write(params byte[] val)
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
