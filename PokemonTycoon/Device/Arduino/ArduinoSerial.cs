using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Diagnostics;

namespace PokemonTycoon.Device.Arduino
{
    public class ArduinoSerial
    {
        const bool DEBUG_MESSAGE = false;

        string _name;
        SerialPort _port;
        Thread _t;
        List<byte> _inBuffer = new List<byte>();
        List<byte[]> _outBuffer = new List<byte[]>();
        DateTime _time = DateTime.MinValue;

        public delegate void BytesReceivedHandler(byte[] bytes);
        public event BytesReceivedHandler BytesReceived;

        public ArduinoSerial(string portName)
        {
            _name = portName;
        }

        public void Start()
        {
            if (_t != null)
                return;

            _port = new SerialPort();
            _port.PortName = _name;
            _port.BaudRate = 9600;

            _t = new Thread(Do);
            _t.IsBackground = true;
            _t.Start();
        }

        void Do()
        {
            try
            {
                _port.Open();
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
                            _inBuffer.AddRange(inBuffer.Take(count));
                        BytesReceived?.Invoke(_inBuffer.ToArray());
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
                        _port.Write(outBuffer.ToArray(), 0, outBuffer.Count);
                        lock (_inBuffer)
                            _inBuffer.Clear();
                    }
                }
            }
            finally
            {
                _port.Close();
            }
        }

        public void Write(byte[] val)
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

        public void Write(byte val)
        {
            Write(new byte[] { val });
        }

        public byte[] Read()
        {
            if (_t == null)
                return null;
            while (true)
            {
                lock (_inBuffer)
                {
                    if (_inBuffer.Count > 0)
                    {
                        var a = _inBuffer.ToArray();
                        _inBuffer.Clear();
                        return a;
                    }
                }
            }
        }

        public byte ReadByte()
        {
            return Read().Last();
        }
    }
}
