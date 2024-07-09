using System.IO.Ports;

namespace EC.Core;

public partial class ECCore
{
    public static List<string> GetPortNames() => SerialPort.GetPortNames().ToList();
}
