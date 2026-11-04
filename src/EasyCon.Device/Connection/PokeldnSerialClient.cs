using EasyDevice.Connection.Pokeldn;

namespace EasyDevice.Connection;

/// <summary>pokeldn 串口入口；与官方服务连接共用 ACK、状态轮询及停止释放逻辑。</summary>
public class PokeldnSerialClient : IConnection
{
    public const int DefaultBaudRate = PokeldnProtocol.BaudRate;
    private readonly PokeldnConnection _connection;
    private volatile Status _status = Status.Connecting;

    public PokeldnSerialClient(string connStr, int baudrate = DefaultBaudRate)
        : this(connStr, new PokeldnSerialTransport(new PokeldnSerialPort(connStr, baudrate))) { }

    internal PokeldnSerialClient(string connStr, IPokeldnTransport transport)
    {
        _connection = new PokeldnConnection(connStr, transport);
        _connection.StatusChanged += status =>
        {
            CurrentStatus = status;
            StatusChanged(status);
        };
    }

    public override Status CurrentStatus { get => _status; protected set => _status = value; }
    public override int ConnectTimeoutMilliseconds => _connection.ConnectTimeoutMilliseconds;
    public override bool SupportsLegacyCommands => false;
    public override string FirmwareName => _connection.FirmwareName;
    public override string? FirmwareVersion => _connection.FirmwareVersion;
    public override bool? ConsoleConnected => _connection.ConsoleConnected;
    public override string? LastError => _connection.LastError;
    public override event BytesTransferedHandler BytesSent { add => _connection.BytesSent += value; remove => _connection.BytesSent -= value; }
    public override event BytesTransferedHandler BytesReceived { add => _connection.BytesReceived += value; remove => _connection.BytesReceived -= value; }
    public override event StatusChangedHandler StatusChanged = delegate { };
    public override void Connect() => _connection.Connect();
    public override void Disconnect() => _connection.Disconnect();
    public override void ClearQueue() => _connection.ClearQueue();
    public override void WriteReport(SwitchReport report) => _connection.WriteReport(report);

    /// <summary>兼容已有调用者的 7-bit HID 包；EasyCon 私有命令不会被写到 pokeldn。</summary>
    public override void Write(params byte[] val)
    {
        SwitchReport? report = DecodeReport(val);
        if (report != null)
            WriteReport(report);
    }

    /// <summary>保留公开的报文翻译接缝；线上编码由共享协议实现提供。</summary>
    public static byte[]? TranslateReport(byte[]? packet)
    {
        SwitchReport? report = DecodeReport(packet);
        return report == null ? null : BuildFrame(PokeldnProtocol.Report, PokeldnProtocol.EncodeReport(report));
    }

    public static byte[] BuildFrame(byte type, params byte[] payload) => PokeldnProtocol.Frame(type, payload);

    private static SwitchReport? DecodeReport(byte[]? packet)
    {
        if (packet == null || packet.Length != 8 || (packet[^1] & 0x80) == 0)
            return null;
        for (int i = 0; i < packet.Length - 1; i++)
            if ((packet[i] & 0x80) != 0)
                return null;
        ulong bits = 0;
        foreach (byte value in packet)
            bits = (bits << 7) | (byte)(value & 0x7F);
        return new SwitchReport
        {
            Button = (ushort)(bits >> 40),
            HAT = (byte)(bits >> 32),
            LX = (byte)(bits >> 24),
            LY = (byte)(bits >> 16),
            RX = (byte)(bits >> 8),
            RY = (byte)bits
        };
    }
}