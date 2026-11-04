namespace EasyDevice.Connection;

/// <summary>按传输地址提供候选协议；工厂仅在轮到该候选时调用，避免提前占用硬件。</summary>
public interface IConnectionProvider
{
    IEnumerable<Func<IConnection>> GetCandidates(ConnectionEndpoint endpoint);
}

public sealed record SerialProtocol(int BaudRate, Func<string, int, IConnection> CreateConnection);

/// <summary>串口的协议/波特率探测顺序由注册项决定，宿主无需识别固件名称。</summary>
public sealed class SerialConnectionProvider(IEnumerable<SerialProtocol> protocols) : IConnectionProvider
{
    private readonly SerialProtocol[] _protocols = [.. protocols];

    public IEnumerable<Func<IConnection>> GetCandidates(ConnectionEndpoint endpoint)
    {
        if (endpoint.Transport != ConnectionTransport.Serial)
            yield break;
        foreach (SerialProtocol protocol in _protocols)
            yield return () => protocol.CreateConnection(endpoint.Address, protocol.BaudRate);
    }
}

/// <summary>TCP 桥接的协议适配器集合。蓝牙扫描、配对和连接由桥接服务实现。</summary>
public sealed class BridgeConnectionProvider(IEnumerable<Func<ConnectionEndpoint, IConnection>> protocols) : IConnectionProvider
{
    private readonly Func<ConnectionEndpoint, IConnection>[] _protocols = [.. protocols];

    public IEnumerable<Func<IConnection>> GetCandidates(ConnectionEndpoint endpoint)
    {
        if (endpoint.Transport != ConnectionTransport.TcpBridge)
            yield break;
        foreach (Func<ConnectionEndpoint, IConnection> protocol in _protocols)
            yield return () => protocol(endpoint);
    }
}