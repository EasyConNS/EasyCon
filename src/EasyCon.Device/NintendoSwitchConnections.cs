using EasyDevice.Connection;
using EasyDevice.Connection.PABotBase2;
using EasyDevice.Connection.Pokeldn;

namespace EasyDevice;

public partial class NintendoSwitch
{
    private readonly IConnectionProvider[]? _connectionProviders;

    public NintendoSwitch() { }

    public NintendoSwitch(IEnumerable<IConnectionProvider> connectionProviders)
    {
        ArgumentNullException.ThrowIfNull(connectionProviders);
        _connectionProviders = [.. connectionProviders];
    }

    /// <summary>默认协议组合；自定义宿主可通过构造函数提供其他串口或桥接协议。</summary>
    protected virtual IEnumerable<IConnectionProvider> GetConnectionProviders() => _connectionProviders ??
    [
        new SerialConnectionProvider(
        [
            new SerialProtocol(115200, CreateConnection),
            new SerialProtocol(9600, CreateConnection),
            new SerialProtocol(PokeldnSerialClient.DefaultBaudRate, (port, _) => CreatePokeldnConnection(port)),
            new SerialProtocol(PABotBase2SerialClient.DefaultBaudRate, (port, baud) => new PABotBase2SerialClient(port, baud)),
            new SerialProtocol(115200, (port, baud) => new PABotBase2SerialClient(port, baud))
        ]),
        new BridgeConnectionProvider(
        [
            endpoint => new PokeldnConnection(endpoint.Address, new PokeldnServiceTransport(endpoint.Host!, endpoint.Port!.Value))
        ])
    ];

    /// <summary>保留原生串口连接的测试接缝。</summary>
    protected virtual IConnection CreateConnection(string connStr, int baudrate) => new TTLSerialClient(connStr, baudrate);

    /// <summary>保留已公开的 pokeldn 串口测试接缝；它不解析桥接或固件专用地址。</summary>
    protected virtual IConnection CreatePokeldnConnection(string connStr) => new PokeldnSerialClient(connStr);
}