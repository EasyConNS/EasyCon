using EasyDevice;
using EasyDevice.Connection;
using System.Collections.Concurrent;

namespace EasyCon.SDLInput.Tests;

/// <summary>
/// IConnection 测试替身：把每次 Write 的报文记录到阻塞队列，供测试断言设备
/// 实际出队并写出的 HID 包，无需任何串口/单片机。
/// </summary>
internal sealed class FakeConnection : IConnection
{
    private readonly BlockingCollection<byte[]> _writes = new();
    private readonly string _port;

    public FakeConnection(string port)
    {
        _port = port;
    }

    public override event BytesTransferedHandler BytesSent = delegate { };
    public override event StatusChangedHandler StatusChanged = delegate { };

    public override Status CurrentStatus { get; protected set; } = Status.Connecting;

    /// <summary>取出下一次写入的报文；超时返回 false，bytes 为空数组。</summary>
    public bool TryTake(out byte[] bytes, int timeoutMs)
    {
        if (_writes.TryTake(out byte[]? packet, timeoutMs))
        {
            bytes = packet;
            return true;
        }
        bytes = [];
        return false;
    }

    public override void Connect()
    {
        CurrentStatus = Status.Connected;
        StatusChanged?.Invoke(Status.Connected);
    }

    public override void Disconnect()
    {
        CurrentStatus = Status.Connecting;
    }

    public override void Write(params byte[] val)
    {
        byte[] packet = val.ToArray();
        BytesSent?.Invoke(_port, packet);
        _writes.Add(packet);
    }

    public override void ClearQueue()
    {
        while (_writes.TryTake(out _))
        {
        }
    }
}