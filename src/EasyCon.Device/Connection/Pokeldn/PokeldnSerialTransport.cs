using System.Diagnostics;
using System.IO.Ports;

namespace EasyDevice.Connection.Pokeldn;

internal interface IPokeldnSerialPort : IDisposable
{
    void Open();
    void DiscardInput();
    void Write(byte[] bytes);
    int Read(byte[] buffer);
}

internal sealed class PokeldnSerialPort(string portName, int baudrate = PokeldnProtocol.BaudRate) : IPokeldnSerialPort
{
    private readonly SerialPort _port = new(portName, baudrate)
    {
        DtrEnable = false,
        RtsEnable = false,
        Handshake = Handshake.None,
        ReadTimeout = 50,
        WriteTimeout = 1000
    };

    public void Open() => _port.Open();
    public void DiscardInput() => _port.DiscardInBuffer();
    public void Write(byte[] bytes) => _port.Write(bytes, 0, bytes.Length);
    public int Read(byte[] buffer) => _port.Read(buffer, 0, buffer.Length);
    public void Dispose() => _port.Dispose();
}

internal sealed class PokeldnSerialTransport(IPokeldnSerialPort port) : IPokeldnTransport
{
    private readonly PokeldnFrameReader _reader = new();

    public int ConnectTimeoutMilliseconds => 7000;

    public PokeldnStatus Connect(CancellationToken token)
    {
        port.Open();
        // CP210x 开口可能复位 ESP32；启动期间请求会丢失，按上游实现重试 3 秒。
        Stopwatch boot = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return GetStatus(token);
            }
            catch (TimeoutException) when (boot.ElapsedMilliseconds < 3000)
            {
                token.ThrowIfCancellationRequested();
            }
        }
    }

    public PokeldnStatus GetStatus(CancellationToken token)
        => PokeldnProtocol.ParseStatus(Ask(PokeldnProtocol.StatusRequest, [], PokeldnProtocol.StatusReply, token));

    public void SendReport(byte[] report, CancellationToken token) => Call(PokeldnProtocol.Report, report, token);
    public void Stop(CancellationToken token) => Call(PokeldnProtocol.Command, [PokeldnProtocol.Stop], token);
    public void Disconnect(CancellationToken token) { }
    public void Dispose() => port.Dispose();

    private void Call(byte kind, byte[] payload, CancellationToken token)
    {
        byte[] result = Ask(kind, payload, PokeldnProtocol.Result, token);
        if (result.Length != 1)
            throw new IOException("pokeldn-pad 返回了无效的 ACK。");
        if (result[0] != 0)
            throw new IOException($"pokeldn-pad 拒绝指令，错误码 {(sbyte)result[0]}。");
    }

    private byte[] Ask(byte kind, byte[] payload, byte expected, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        port.DiscardInput();
        _reader.Reset();
        port.Write(PokeldnProtocol.Frame(kind, payload));
        Stopwatch wait = Stopwatch.StartNew();
        byte[] buffer = new byte[256];
        while (wait.ElapsedMilliseconds < 1000)
        {
            token.ThrowIfCancellationRequested();
            int count;
            try
            {
                count = port.Read(buffer);
            }
            catch (TimeoutException)
            {
                continue;
            }
            foreach (PokeldnFrame frame in _reader.Feed(buffer, count))
                if (frame.Kind == expected)
                    return frame.Payload;
        }
        throw new TimeoutException("pokeldn-pad 未响应串口请求；请检查手柄固件、USB 连接和串口占用。");
    }
}