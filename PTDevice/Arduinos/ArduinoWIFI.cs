using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace PTDevice.Arduino
{
    public class ArduinoWIFI : IArduino
    {
        const bool DEBUG_MESSAGE = false;

        readonly string _ipaddr;
        readonly int _port;
        private EndPoint _point;
        Socket tcpClient;
        bool _sayhello;
        Thread _t;
        List<byte> _inBuffer = new List<byte>();
        List<byte[]> _outBuffer = new List<byte[]>();
        DateTime _time = DateTime.MinValue;

        public override event BytesTransferedHandler BytesSent;
        public override event BytesTransferedHandler BytesReceived;
        public override event StatusChangedHandler StatusChanged;
        Status _status = Status.Connecting;
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

        public ArduinoWIFI(string addr, int port = 8266)
        {
            _ipaddr = addr;
            _port = port;
        }

        public override void Connect(bool sayhello = true)
        {
            if (_t != null)
                return;

            tcpClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPAddress ipaddress = IPAddress.Parse(_ipaddr);
            _point = new IPEndPoint(ipaddress, _port);

            _sayhello = sayhello;
            _t = new Thread(Do);
            _t.IsBackground = true;
            _t.Start();
        }

        public override void Disconnect()
        {
            _t?.Abort();
            _t?.Join(100);
            _t = null;
        }

        void Do()
        {
            try
            {
                tcpClient.Connect(_point);

                // say hello
                if (_sayhello)
                {
                    var hellobytes = new byte[] { Command.Ready, Command.Ready, Command.Hello };
                    tcpClient.Send(hellobytes);
                    // _port.Write(hellobytes, 0, hellobytes.Length);
                    BytesSent?.Invoke(_ipaddr, hellobytes);
                }
                else
                    CurrentStatus = Status.ConnectedUnsafe;
                bool hellocheck = _sayhello;

                byte[] inBuffer = new byte[255];
                List<byte> outBuffer = new List<byte>();
                while (true)
                {
                    // read
                    if (tcpClient.Available > 0)
                    {
                        int count = tcpClient.Receive(inBuffer);
                        if (DEBUG_MESSAGE)
                            Debug.WriteLine($"[{_ipaddr}] " + string.Join(" ", inBuffer.Take(count).Select(b => b.ToString("X2"))));
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
                        BytesReceived?.Invoke(_ipaddr, _inBuffer.ToArray());
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
                        tcpClient.Send(bytes);
                        BytesSent?.Invoke(_ipaddr, bytes);
                    }
                }
            }
            catch (Exception ex)
            {
                CurrentStatus = Status.Error;
            }
            finally
            {
                tcpClient.Close();
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
