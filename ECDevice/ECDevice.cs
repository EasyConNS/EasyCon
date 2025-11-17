using System.Collections.Generic;
using System.IO.Ports;

namespace ECDevice;

public sealed class ECDevice
{
    public static List<string> GetPortNames() => [.. SerialPort.GetPortNames()];
}