using EasyDevice.Connection;

namespace EasyDevice;

public enum Status
{
    Connecting,
    Connected,
    Error,
}

public partial class NintendoSwitch
{
    private IConnection clientCon { get; set; }

    /// <summary>创建底层连接；测试可覆写以注入假连接（不改变协议与队列逻辑）。</summary>
    protected virtual IConnection CreateConnection(string connStr, int baudrate)
        => new TTLSerialClient(connStr, baudrate);

    /// <summary>创建 pokeldn 手柄固件连接（921600，A5 5A 帧协议）；测试可覆写注入假连接。</summary>
    protected virtual IConnection CreatePokeldnConnection(string connStr)
        => new PokeldnSerialClient(connStr, PokeldnSerialClient.DefaultBaudRate);

    private ConnectResult _TryConnect(string connStr, int baudrate = 115200, bool pokeldn = false)
    {
        if (connStr == "")
            return ConnectResult.InvalidArgument;

        using EventWaitHandle ewh = new(false, EventResetMode.AutoReset);
        var result = ConnectResult.None;
        bool listening = true;
        void statuschanged(Status status)
        {
            lock (ewh)
            {
                if (!listening || result != ConnectResult.None)
                    return;
                if (status == Status.Connected)
                {
                    result = ConnectResult.Success;
                    ewh.Set();
                }
                if (status == Status.Error)
                {
                    result = ConnectResult.Error;
                    ewh.Set();
                }
            }
        }

        Disconnect();
        IConnection connection = pokeldn ? CreatePokeldnConnection(connStr) : CreateConnection(connStr, baudrate);
        clientCon = connection;
        connection.BytesSent += (port, bytes) => { if (ReferenceEquals(clientCon, connection)) BytesSent?.Invoke(port, bytes); };
        connection.BytesReceived += (port, bytes) => { if (ReferenceEquals(clientCon, connection)) BytesReceived?.Invoke(port, bytes); };
        connection.StatusChanged += status => { if (ReferenceEquals(clientCon, connection)) StatusChanged?.Invoke(status); };
        connection.StatusChanged += statuschanged;
        try
        {
            connection.Connect();
            if (!ewh.WaitOne(1000)) result = ConnectResult.Timeout;
            if (result != ConnectResult.Success)
            {
                clientCon = null;
                connection.Disconnect();
            }
            return result;
        }
        finally
        {
            lock (ewh) listening = false;
            connection.StatusChanged -= statuschanged;
        }
    }

    public void Disconnect()
    {
        CancelStaleLoop();
        IConnection connection = clientCon;
        clientCon = null;
        connection?.Disconnect();
    }

    /// <summary>复位并等待串口实际写出中性包；不表示主机/游戏已确认。</summary>
    public async Task<bool> ResetAndWaitForWriteAsync(TimeSpan timeout, CancellationToken token = default)
    {
        IConnection connection = clientCon;
        if (connection == null || !IsConnected()) { Reset(); return false; }
        byte[] neutral = new SwitchReport().GetBytes().ToArray();
        TaskCompletionSource<bool> written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnWritten(string port, byte[] bytes)
        {
            // 一次串口写可合并多个包，最后一个必须是中性包。
            if (bytes.AsSpan().EndsWith(neutral)) written.TrySetResult(true);
        }
        void OnStatus(Status status)
        {
            if (status == Status.Error) written.TrySetResult(false);
        }
        connection.BytesSent += OnWritten;
        connection.StatusChanged += OnStatus;
        try
        {
            Reset();
            return await written.Task.WaitAsync(timeout, token).ConfigureAwait(false);
        }
        catch (TimeoutException) { return false; }
        finally
        {
            connection.BytesSent -= OnWritten;
            connection.StatusChanged -= OnStatus;
        }
    }

    public bool IsConnected() => clientCon?.CurrentStatus == Status.Connected;

    void WriteReport(Span<byte> b)
    {
        Log?.Invoke($"[Send {DateTime.Now:ss.fff}] {_report}");

        var con = clientCon;   // 局部快照：Disconnect 可并发置 null
        con?.Write(b.ToArray());
    }

    bool SendSync(Func<byte, bool> predicate, int timeout = 100, params byte[] bytes)
    {
        var ewh = new EventWaitHandle(false, EventResetMode.AutoReset);
        void h(string port, byte[] bytes_)
        {
            foreach (var b in bytes_)
                if (predicate(b))
                {
                    ewh.Set();
                    break;
                }
        }
        try
        {
            var con = clientCon;
            if (con == null)
                return false;
            con.BytesReceived += h;
            con.Write(bytes);
            if (!ewh.WaitOne(timeout))
                return false;
            return true;
        }
        finally
        {
            if (clientCon != null)
                clientCon.BytesReceived -= h;
        }
    }

    bool ResetControl()
    {
        var ewh = new EventWaitHandle(false, EventResetMode.AutoReset);
        void h(string port, byte[] bytes_)
        {
            if (bytes_.Contains(Reply.Hello))
                ewh.Set();
        }
        try
        {
            var con = clientCon;
            if (con == null)
                return false;
            con.BytesReceived += h;
            for (int i = 0; i < 3; i++)
                con.Write(EzDvCommand.Ready, EzDvCommand.Hello);
            return ewh.WaitOne(50);
        }
        finally
        {
            if (clientCon != null)
                clientCon.BytesReceived -= h;
        }
    }

    public bool Flash(byte[] asmBytes)
    {
        const int PacketSize = 20;
        List<byte> list = new(asmBytes);
        for (int i = 0; i < list.Count; i += PacketSize)
        {
            int len = Math.Min(PacketSize, list.Count - i);
            var packet = list.GetRange(i, len).ToArray();
            while (true)
            {
                if (!SendSync(
                        b => b == Reply.FlashStart,
                        1000,
                        EzDvCommand.Ready,
                        (byte)(i & 0x7F),
                        (byte)(i >> 7),
                        (byte)(len & 0x7F),
                        (byte)(len >> 7),
                        EzDvCommand.Flash)
                    || !SendSync(
                        b => b == Reply.FlashEnd,
                        1000,
                        packet)
                    )
                {
                    // error, retry
                    if (!ResetControl())
                        return false;
                    continue;
                }
                break;
            }
        }
        return true;
    }

    public bool RemoteStart()
    {
        return SendSync(b => b == Reply.ScriptAck, 200, EzDvCommand.Ready, EzDvCommand.ScriptStart);
    }

    public bool RemoteStop()
    {
        return SendSync(b => b == Reply.ScriptAck, 200, EzDvCommand.Ready, EzDvCommand.ScriptStop);
    }

    public int GetVersion()
    {
        int ver = -1;
        SendSync(b =>
        {
            if (b >= 0x40 && b <= 0x80)
            {
                ver = b;
                return true;
            }
            return false;
        }, 200,
        EzDvCommand.Ready,
        EzDvCommand.Version);
        return ver;
    }

    public bool TriggerLED()
    {
        return SendSync(b => b == 0, 200, EzDvCommand.Ready, EzDvCommand.LED);
    }

    public bool UnPair()
    {
        return SendSync(b => b == Reply.Ack, 200, EzDvCommand.Ready, EzDvCommand.UnPair);
    }

    public bool ChangeControllerMode(byte mode)
    {
        return SendSync(b => b == Reply.Ack, 200, EzDvCommand.Ready, mode, EzDvCommand.ChangeControllerMode);
    }

    public bool ChangeControllerColor(byte[] color)
    {
        List<byte> list = [.. color];
        var packet = list.ToArray();
        uint i = 0;
        uint len = 12;
        bool ret = false;
        ret = SendSync(
        b => b == Reply.Ack,
        1000,
        EzDvCommand.Ready,
        (byte)(i & 0x7F),
        (byte)(i >> 7),
        (byte)(len & 0x7F),
        (byte)(len >> 7),
        EzDvCommand.ChangeControllerColor);

        ret &= SendSync(
        b => b == Reply.Ack,
        1000,
        packet);
        return ret;
    }

    public bool SaveAmiibo(byte index, byte[] amiibo)
    {
        const int PacketSize = 20;
        List<byte> list = [.. amiibo];
        for (int i = 0; i < list.Count; i += PacketSize)
        {
            int len = Math.Min(PacketSize, list.Count - i);
            var packet = list.GetRange(i, len).ToArray();
            while (true)
            {
                if (!SendSync(
                        b => b == Reply.Ack,
                        1000,
                        EzDvCommand.Ready,
                        (byte)(i & 0x7F),
                        (byte)(i >> 7),
                        (byte)(len & 0x7F),
                        (byte)(len >> 7),
                        index,
                        EzDvCommand.SaveAmiibo)
                    || !SendSync(
                        b => b == Reply.Ack,
                        1000,
                        packet)
                    )
                {
                    // error, retry
                    if (!ResetControl())
                        return false;
                    continue;
                }
                break;
            }
        }
        return true;
    }

    public bool ChangeAmiiboIndex(byte index)
    {
        return SendSync(b => b == Reply.Ack, 200, EzDvCommand.Ready, index, EzDvCommand.ChangeAmiiboIndex);
    }
}