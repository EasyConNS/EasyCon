namespace ECDevice.Connection
{
    public abstract class IConnClient
    {
        public delegate void BytesTransferedHandler(string connString, byte[] bytes);
        public delegate void StatusChangedHandler(Status status);
        public virtual event BytesTransferedHandler BytesSent;
        public virtual event BytesTransferedHandler BytesReceived;
        public virtual event StatusChangedHandler StatusChanged;

        public abstract Status CurrentStatus { get; protected set; }

        public bool CPUOpt { get;  set; }

        public abstract void Connect(bool sayhello = true);
        public abstract void Disconnect();
        public abstract void Write(params byte[] val);
    }
}
