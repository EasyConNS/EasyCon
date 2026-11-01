using System.IO.Ports;

namespace EasyDevice;

public sealed class ECDevice
{
    public static List<string> GetPortNames() => [.. SerialPort.GetPortNames()];
}