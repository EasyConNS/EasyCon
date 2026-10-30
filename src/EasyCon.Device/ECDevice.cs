using System.IO.Ports;

namespace EasyDevice;

public sealed class ECDevice
{
    public const string PokeldnServiceName = "pokeldn (BLE 服务)";
    public const string PokeldnServiceEndpoint = "pokeldn://service";

    public static List<string> GetPortNames() => [.. SerialPort.GetPortNames()];

    public static List<string> GetConnectionNames() => [.. GetPortNames(), PokeldnServiceName];
}