using static EasyDevice.Connection.SerialPortClient;

namespace EasyDevice.Connection;

internal class TTLv2SerialClient(string name, int port) : IConnection
{
    private SerialPortClient _serialPortClient = new(name, port);

    public override event BytesTransferedHandler BytesSent;
    public override event BytesTransferedHandler BytesReceived;
    public override event StatusChangedHandler StatusChanged;

    public override Status CurrentStatus
    {
        get => _serialPortClient.CurrentState switch
        {
            ConnectionState.Connected => Status.Connected,
            ConnectionState.Connecting => Status.Connecting,
            _ => Status.Error
        }; protected set => throw new NotImplementedException();
    }

    public override void Connect()
    {
        _serialPortClient.SetHeartbeat([Command.Ready, Command.Ready, Command.Hello], (bs) => bs.Length == 1 && bs[0] == Reply.Hello);

        void checkopend(object sender, string message)
        {
            bool check(byte[] bs) => bs.Length == 1 && bs[0] == Reply.Hello;
                var recv = _serialPortClient.SendCommand([Command.Ready, Command.Ready, Command.Hello]);

                Console.WriteLine($"[{_serialPortClient.ConnectPort}] --recv-- " + string.Join(" ", recv.Select(b => b.ToString("X2"))));

                if (!check(recv))
                {
                    _serialPortClient.Close();
                    StatusChanged?.Invoke(Status.Error);
                }
        };
        _serialPortClient.ConnectionOpened += checkopend;
        _serialPortClient.DataReceived += (sender, args) =>
        {
            BytesReceived?.Invoke(_serialPortClient.ConnectPort, args.Data);
        };
        Task.Run(() => {
            try
            {
                if (_serialPortClient.Open())
                {
                    StatusChanged?.Invoke(CurrentStatus);
                    _serialPortClient.ConnectionStateChanged += (sender, args) =>
                    {
                        StatusChanged?.Invoke(CurrentStatus);
                    };
                }
                throw new Exception("连接失败");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                StatusChanged?.Invoke(Status.Error);
            }
        });
    }

    public override void Disconnect()
    {
        _serialPortClient.Close();
    }

    public override void Write(params byte[] val)
    {
        BytesSent?.Invoke(_serialPortClient.ConnectPort, val);
        _serialPortClient.SendCommand(val);
    }
}
