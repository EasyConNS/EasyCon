using System.IO.Ports;

namespace EC.Device;

public partial class ECDevice
{
    public static List<string> GetPortNames() => SerialPort.GetPortNames().ToList();
}
