using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PTDevice.Arduino
{
    public class ArduinoBluetooth
    {
        readonly string _address;

        public ArduinoBluetooth(string address = "5051a97cf8b9")
        {
            _address = address;
        }
    }
}
