namespace EasyDevice.Connection;

public delegate void BytesTransferedHandler(string comPort, byte[] bytes);
public delegate void StatusChangedHandler(Status status);

public abstract class IConnection
{
    public virtual event BytesTransferedHandler BytesSent;
    public virtual event BytesTransferedHandler BytesReceived;
    public virtual event StatusChangedHandler StatusChanged;

    public abstract Status CurrentStatus { get; protected set; }

    public virtual int ConnectTimeoutMilliseconds => 1000;
    public virtual bool SupportsLegacyCommands => true;
    public virtual string FirmwareName => "EasyCon";
    public virtual string? FirmwareVersion => null;
    public virtual bool? ConsoleConnected => null;
    public virtual string? LastError => null;

    public abstract void Connect();
    public abstract void Disconnect();
    public abstract void Write(params byte[] val);

    /// <summary>由连接选择报文编码；旧固件保持原有的 7 位打包格式。</summary>
    public virtual void WriteReport(SwitchReport report) => Write(report.GetBytes());

    /// <summary>
    /// 清空待发送队列，脚本终止时调用以丢弃尚未发出的 HID 报文。
    /// </summary>
    public abstract void ClearQueue();
}