using EasyDevice.Connection;

namespace EasyDevice;

public partial class NintendoSwitch
{
    private IConnection clientCon { get; set; }

    private ConnectResult _TryConnect(string connStr, int baudrate=115200)
    {
        if (connStr == "")
            return ConnectResult.InvalidArgument;

        var ewh = new EventWaitHandle(false, EventResetMode.AutoReset);
        var result = ConnectResult.None;
        void statuschanged(Status status)
        {
            lock (ewh)
            {
                if (result != ConnectResult.None)
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
        clientCon = new TTLSerialClient(connStr,baudrate);
        clientCon.BytesSent += BytesSent;
        clientCon.BytesReceived += BytesReceived;
        clientCon.StatusChanged += StatusChanged;
        clientCon.CPUOpt = need_cpu_opt;
        clientCon.OpenDelay = need_open_delay;

        clientCon.StatusChanged += statuschanged;
        clientCon.Connect();
        if (!ewh.WaitOne(1000))
        {
            clientCon.Disconnect();
            clientCon = null;
            return ConnectResult.Timeout;
        }
        if (result != ConnectResult.Success)
        {
            clientCon.Disconnect();
            clientCon = null;
            return result;
        }
        clientCon.StatusChanged -= statuschanged;

        return ConnectResult.Success;
    }

    public void Disconnect()
    {
        clientCon?.Disconnect();
        clientCon = null;
        source?.Cancel();
    }

    public bool IsConnected() => clientCon?.CurrentStatus == Status.Connected;

    void WriteReport(Span<byte> b)
    {
        clientCon.Write(b.ToArray());
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
            clientCon.BytesReceived += h;
            clientCon.Write(bytes);
            if (!ewh.WaitOne(timeout))
                return false;
            return true;
        }
        finally
        {
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
            clientCon.BytesReceived += h;
            for (int i = 0; i < 3; i++)
                clientCon.Write(Command.Ready, Command.Hello);
            return ewh.WaitOne(50);
        }
        finally
        {
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
                        Command.Ready,
                        (byte)(i & 0x7F),
                        (byte)(i >> 7),
                        (byte)(len & 0x7F),
                        (byte)(len >> 7),
                        Command.Flash)
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
        return SendSync(b => b == Reply.ScriptAck, 200, Command.Ready, Command.ScriptStart);
    }

    public bool RemoteStop()
    {
        return SendSync(b => b == Reply.ScriptAck, 200, Command.Ready, Command.ScriptStop);
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
        Command.Ready,
        Command.Version);
        return ver;
    }

    public bool TriggerLED()
    {
        return SendSync(b => b == 0, 200, Command.Ready, Command.LED);
    }

    public bool UnPair()
    {
        return SendSync(b => b == Reply.Ack, 200, Command.Ready, Command.UnPair);
    }

    public bool ChangeControllerMode(byte mode)
    {
        return SendSync(b => b == Reply.Ack, 200, Command.Ready, mode,Command.ChangeControllerMode);
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
        Command.Ready,
        (byte)(i & 0x7F),
        (byte)(i >> 7),
        (byte)(len & 0x7F),
        (byte)(len >> 7),
        Command.ChangeControllerColor);

        ret &= SendSync(
        b => b == Reply.Ack,
        1000,
        packet);
        return ret;
    }

    public bool SaveAmiibo(byte index,byte[] amiibo)
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
                        Command.Ready,
                        (byte)(i & 0x7F),
                        (byte)(i >> 7),
                        (byte)(len & 0x7F),
                        (byte)(len >> 7),
                        index,
                        Command.SaveAmiibo)
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
        return SendSync(b => b == Reply.Ack, 200, Command.Ready, index, Command.ChangeAmiiboIndex);
    }
}

public enum Status
{
    Connecting,
    Connected,
    Error,
}
