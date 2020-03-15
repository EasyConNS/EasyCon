using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO.Ports;
using System.Threading;
using System.Diagnostics;

namespace PTDevice.Arduino
{
    public class ArduinoSerial : IArduino
    {
        const bool DEBUG_MESSAGE = false;

        readonly string _name;
        SerialPort _port;
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

        public ArduinoSerial(string portName)
        {
            _name = portName;
        }

        public override void Connect(bool sayhello = true)
        {
            if (_t != null)
                return;

            _port = new SerialPort();
            _port.PortName = _name;
            _port.BaudRate = 9600;
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
                _port.Open();

                // say hello
                if (_sayhello)
                {
                    var hellobytes = new byte[] { Command.Ready, Command.Ready, Command.Hello };
                    _port.Write(hellobytes, 0, hellobytes.Length);
                    BytesSent?.Invoke(_name, hellobytes);
                }
                else
                    CurrentStatus = Status.ConnectedUnsafe;
                bool hellocheck = _sayhello;

                byte[] inBuffer = new byte[255];
                List<byte> outBuffer = new List<byte>();
                while (true)
                {
                    // read
                    if (_port.BytesToRead > 0)
                    {
                        int count = _port.Read(inBuffer, 0, inBuffer.Length);
                        if (DEBUG_MESSAGE)
                            Debug.WriteLine($"[{_name}] " + string.Join(" ", inBuffer.Take(count).Select(b => b.ToString("X2"))));
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
                        BytesReceived?.Invoke(_name, _inBuffer.ToArray());
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
                        _port.Write(bytes, 0, bytes.Length);
                        BytesSent?.Invoke(_name, bytes);
                    }
                }
            }
            catch (Exception ex)
            {
                CurrentStatus = Status.Error;
            }
            finally
            {
                _port.Close();
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
