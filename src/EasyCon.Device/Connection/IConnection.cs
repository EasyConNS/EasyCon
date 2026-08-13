namespace EasyDevice.Connection;

public delegate void BytesTransferedHandler(string comPort, byte[] bytes);
public delegate void StatusChangedHandler(Status status);

abstract class IConnection
{
    public virtual event BytesTransferedHandler BytesSent;
    public virtual event BytesTransferedHandler BytesReceived;
    public virtual event StatusChangedHandler StatusChanged;

    public abstract Status CurrentStatus { get; protected set; }

    public abstract void Connect();
    public abstract void Disconnect();
    public abstract void Write(params byte[] val);

    /// <summary>
    /// 清空待发送队列，脚本终止时调用以丢弃尚未发出的 HID 报文。
    /// </summary>
    public abstract void ClearQueue();
}