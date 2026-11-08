using System.IO.Ports;

namespace EasyDevice.Connection.PABotBase2;

internal interface IPABotBase2SerialPort : IDisposable
{
    void Open();
    void Write(byte[] data);
    int Read(byte[] buffer);
}

internal sealed class PABotBase2SerialPort(string port, int baudRate) : IPABotBase2SerialPort
{
    private readonly SerialPort _port = new(port, baudRate)
    {
        DtrEnable = true,
        RtsEnable = false,
        Handshake = Handshake.None,
        ReadTimeout = 5,
        WriteTimeout = 1000
    };

    public void Open()
    {
        _port.Open();
        _port.DiscardInBuffer();
    }

    public void Write(byte[] data) => _port.Write(data, 0, data.Length);

    public int Read(byte[] buffer)
    {
        try { return _port.Read(buffer, 0, buffer.Length); }
        catch (TimeoutException) { return 0; }
    }

    public void Dispose() => _port.Dispose();
}