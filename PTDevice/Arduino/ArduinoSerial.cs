using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Diagnostics;

namespace PTDevice.Arduino
{
    public class ArduinoSerial
    {
        const bool DEBUG_MESSAGE = false;

        string _name;
        SerialPort _port;
        bool _sayhello;
        Thread _t;
        List<byte> _inBuffer = new List<byte>();
        List<byte[]> _outBuffer = new List<byte[]>();
        DateTime _time = DateTime.MinValue;

        public delegate void BytesTransferedHandler(string portName, byte[] bytes);
        public event BytesTransferedHandler BytesSent;
        public event BytesTransferedHandler BytesReceived;

        public enum Status
        {
            Connecting,
            Connected,
            ConnectedUnsafe,
            Error,
            Timeout,
        }

        public delegate void StatusChangedHandler(Status status);
        public event StatusChangedHandler StatusChanged;
        Status _status = Status.Connecting;
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

        public static class Command
        {
            public const byte Ready = 0xA5;
            public const byte Debug = 0x80;
            public const byte Hello = 0x81;
            public const byte Flash = 0x82;
            public const byte ScriptStart = 0x83;
            public const byte ScriptStop = 0x84;
            public const byte Version = 0x85;
        }

        public static class Reply
        {
            public const byte Error = 0x0;
            public const byte Ack = 0xFF;
            public const byte Hello = 0x80;
            public const byte FlashStart = 0x81;
            public const byte FlashEnd = 0x82;
            public const byte ScriptAck = 0x83;
        }

        public ArduinoSerial(string portName)
        {
            _name = portName;
        }

        public void Connect(bool sayhello = true)
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

        public void Disconnect()
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
                var hellotimer = DateTime.Now.AddMilliseconds(100);

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

                    // hello time out
                    if (hellocheck && DateTime.Now >= hellotimer)
                    {
                        CurrentStatus = Status.Timeout;
                        break;
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
