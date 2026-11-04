using EasyDevice;
using EasyDevice.Connection;

namespace EasyCon.SDLInput.Tests;

/// <summary>用 FakeConnection 替换真实串口连接的 NintendoSwitch，驱动键盘→HID 链路。</summary>
internal sealed class TestSwitch : NintendoSwitch
{
    private readonly FakeConnection _connection;

    public TestSwitch(FakeConnection connection)
    {
        _connection = connection;
    }

    protected override IConnection CreateConnection(string connStr, int baudrate)
    {
        return _connection;
    }
}