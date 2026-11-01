using System.Net;

namespace EasyDevice.Connection;

public enum ConnectionTransport { Serial, TcpBridge }

/// <summary>传输地址和资源标识。固件协议不参与地址解析或来源占用锁。</summary>
public sealed record ConnectionEndpoint
{
    private ConnectionEndpoint(ConnectionTransport transport, string address, string? host = null, int? port = null)
    {
        Transport = transport;
        Address = address;
        Host = host;
        Port = port;
    }

    public ConnectionTransport Transport { get; }
    public string Address { get; }
    public string? Host { get; }
    public int? Port { get; }
    public string OwnershipKey => Transport == ConnectionTransport.Serial ? "serial:" + Address : Address;

    public static ConnectionEndpoint Parse(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        string endpoint = address.Trim();
        if (endpoint.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme != "tcp"
                || uri.Port is < 1 or > 65535 || string.IsNullOrEmpty(uri.Host)
                || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || uri.AbsolutePath is not ("" or "/"))
                throw new ArgumentException("桥接地址应为 tcp://主机:端口，不能包含用户信息、路径或参数。", nameof(address));
            string host = uri.DnsSafeHost;
            if (IPAddress.TryParse(host, out IPAddress? ip))
                host = ip.ToString();
            else
                host = uri.IdnHost.ToLowerInvariant();
            string canonical = new UriBuilder("tcp", host, uri.Port).Uri.AbsoluteUri.TrimEnd('/');
            return new ConnectionEndpoint(ConnectionTransport.TcpBridge, canonical, host, uri.Port);
        }

        if (OperatingSystem.IsWindows())
        {
            endpoint = endpoint.StartsWith(@"\\.\", StringComparison.Ordinal) ? endpoint[4..] : endpoint;
            if (endpoint.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && endpoint.Length > 3 && endpoint[3..].All(char.IsAsciiDigit))
                endpoint = endpoint.ToUpperInvariant();
        }
        else if (Path.IsPathRooted(endpoint))
        {
            endpoint = new FileInfo(endpoint).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(endpoint);
        }
        return new ConnectionEndpoint(ConnectionTransport.Serial, endpoint);
    }
}