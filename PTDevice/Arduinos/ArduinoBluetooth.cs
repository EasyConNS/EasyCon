namespace PTDevice.Arduino
{
    public class ArduinoBluetooth : IArduino
    {
        readonly string _address;

        public ArduinoBluetooth(string address = "5051a97cf8b9")
        {
            _address = address;
        }

        public override Status CurrentStatus { get => throw new System.NotImplementedException(); protected set => throw new System.NotImplementedException(); }

        public override event BytesTransferedHandler BytesSent;
        public override event BytesTransferedHandler BytesReceived;
        public override event StatusChangedHandler StatusChanged;

        public override void Connect(bool sayhello = true)
        {
            throw new System.NotImplementedException();
        }

        public override void Disconnect()
        {
            throw new System.NotImplementedException();
        }

        public override void Write(params byte[] val)
        {
            throw new System.NotImplementedException();
        }
    }
}
