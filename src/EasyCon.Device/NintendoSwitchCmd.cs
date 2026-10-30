using EasyDevice.Connection;
using EasyDevice.Connection.Pokeldn;

namespace EasyDevice;

public enum Status
{
    Connecting,
    Connected,
    Error,
}

public partial class NintendoSwitch
{
    private IConnection? clientCon { get; set; }

    public bool SupportsLegacyCommands => clientCon?.SupportsLegacyCommands == true;
    public string? FirmwareName => clientCon?.FirmwareName;
    public string? FirmwareVersion => clientCon?.FirmwareVersion;
    public bool? ConsoleConnected => clientCon?.ConsoleConnected;
    public string? LastConnectionError { get; private set; }

    /// <summary>创建底层连接；测试可覆写以注入假连接（不改变协议与队列逻辑）。</summary>
    protected virtual IConnection CreateConnection(string connStr, int baudrate)
        => new TTLSerialClient(connStr, baudrate);

    /// <summary>创建 pokeldn 串口或官方服务连接；保留第三档探测的测试接缝。</summary>
    protected virtual IConnection CreatePokeldnConnection(string connStr)
    {
        if (connStr.Equals(ECDevice.PokeldnServiceEndpoint, StringComparison.OrdinalIgnoreCase)
            || connStr == ECDevice.PokeldnServiceName)
            return new PokeldnConnection(connStr, new PokeldnServiceTransport());
        if (connStr.StartsWith("pokeldn:", StringComparison.OrdinalIgnoreCase))
        {
            connStr = connStr["pokeldn:".Length..];
            if (string.IsNullOrWhiteSpace(connStr) || connStr.StartsWith("//", StringComparison.Ordinal))
                throw new ArgumentException("pokeldn 连接地址应为 pokeldn:COM3、pokeldn:/dev/ttyUSB0 或 pokeldn://service。");
        }
        return new PokeldnSerialClient(connStr);
    }

    private ConnectResult _TryConnect(string connStr, int baudrate = 115200, bool pokeldn = false)
    {
        Disconnect();
        LastConnectionError = null;
        IConnection connection;
        try
        {
            connection = pokeldn ? CreatePokeldnConnection(connStr) : CreateConnection(connStr, baudrate);
        }
        catch (ArgumentException ex)
        {
            LastConnectionError = ex.Message;
            return ConnectResult.InvalidArgument;
        }
        clientCon = connection;
        TaskCompletionSource<ConnectResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void statuschanged(Status status)
        {
            if (status == Status.Connected)
                completion.TrySetResult(ConnectResult.Success);
            if (status == Status.Error)
                completion.TrySetResult(ConnectResult.Error);
        }
        connection.BytesSent += (port, bytes) => { if (ReferenceEquals(clientCon, connection)) BytesSent?.Invoke(port, bytes); };
        connection.BytesReceived += (port, bytes) => { if (ReferenceEquals(clientCon, connection)) BytesReceived?.Invoke(port, bytes); };
        connection.StatusChanged += status =>
        {
            if (ReferenceEquals(clientCon, connection))
            {
                if (status == Status.Error)
                {
                    LastConnectionError = connection.LastError;
                    Log?.Invoke(LastConnectionError ?? "设备连接已断开。");
                }
                StatusChanged?.Invoke(status);
            }
        };
        connection.StatusChanged += statuschanged;
        try
        {
            connection.Connect();
            ConnectResult result = completion.Task.Wait(connection.ConnectTimeoutMilliseconds)
                ? completion.Task.Result : ConnectResult.Timeout;
            if (result != ConnectResult.Success)
            {
                clientCon = null;
                connection.Disconnect();
                LastConnectionError = connection.LastError ?? LastConnectionError;
            }
            return result;
        }
        finally
        {
            connection.StatusChanged -= statuschanged;
        }
    }

    public void Disconnect()
    {
        CancelStaleLoop();
        IConnection? connection = clientCon;
        clientCon = null;
        connection?.Disconnect();
    }

    /// <summary>复位并等待串口实际写出中性包；不表示主机/游戏已确认。</summary>
    public async Task<bool> ResetAndWaitForWriteAsync(TimeSpan timeout, CancellationToken token = default)
    {
        IConnection? connection = clientCon;
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

    void WriteReport(SwitchReport report)
    {
        Log?.Invoke($"[Send {DateTime.Now:ss.fff}] {_report}");

        var con = clientCon;   // 局部快照：Disconnect 可并发置 null
        con?.WriteReport(report);
    }

    bool SendSync(Func<byte, bool> predicate, int timeout = 100, params byte[] bytes)
    {
        if (!RequireLegacyCommands())
            return false;
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
        if (!RequireLegacyCommands())
            return false;
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
        if (!RequireLegacyCommands())
            return false;
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
        if (!RequireLegacyCommands())
            return false;
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

    private bool RequireLegacyCommands()
    {
        if (SupportsLegacyCommands)
            return true;
        if (IsConnected())
            Log?.Invoke("pokeldn-pad 仅支持在线手柄控制；不支持伊机控离线脚本烧录、远程运行、Amiibo、颜色及解除配对命令。");
        return false;
    }
}