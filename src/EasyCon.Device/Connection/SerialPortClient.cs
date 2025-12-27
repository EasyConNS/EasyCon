using System.Data;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Timers;

namespace EasyDevice.Connection;

class SerialPortClient : IDisposable
{
    private SerialPort _serialPort;
    private readonly object _lockObject = new();
    private AutoResetEvent _dataReceivedEvent = new(false);
    private List<byte> _receivedBuffer = new();
    private readonly int _receiveTimeout = 300;

    // 心跳检测定时器
    private System.Timers.Timer _heartbeatTimer;
    private bool _isHeartbeatEnabled = false;
    private byte[] _heartbeatCommand;
    private int _heartbeatInterval = 5000; // 默认5秒
    private int _heartbeatFailCount = 0;
    private int _maxHeartbeatFailCount = 3;

    // 连接状态
    private volatile ConnectionState _connectionState = ConnectionState.Disconnected;

    public string ConnectPort => _serialPort.PortName;

    /// <summary>
    /// 连接状态枚举
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Disconnecting,
        Error
    }

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    public event Action<ConnectionState, string> ConnectionStateChanged;

    /// <summary>
    /// 连接成功事件
    /// </summary>
    public event EventHandler<string> ConnectionOpened;

    /// <summary>
    /// 连接失败事件
    /// </summary>
    public event EventHandler<ConnectionErrorEventArgs> ConnectionFailed;

    /// <summary>
    /// 设备断开事件（被动断开）
    /// </summary>
    public event EventHandler<string> DeviceDisconnected;

    /// <summary>
    /// 数据接收事件
    /// </summary>
    public event EventHandler<DataReceivedEventArgs> DataReceived;

    /// <summary>
    /// 连接成功事件
    /// </summary>
    public event Func<byte[], bool> HeartbeatCheckHandler;

    /// <summary>
    /// 当前连接状态
    /// </summary>
    public ConnectionState CurrentState => _connectionState;

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected => _connectionState == ConnectionState.Connected;

    /// <summary>
    /// 初始化串口
    /// </summary>
    public SerialPortClient(string portName, int baudRate = 115200)
    {
        _serialPort = new SerialPort
        {
            PortName = portName,
            BaudRate = baudRate,
            ReadTimeout = 500,
            WriteTimeout = 500
        };

        _serialPort.DataReceived += SerialPort_DataReceived;
        _serialPort.ErrorReceived += SerialPort_ErrorReceived;

        // 初始化心跳定时器
        _heartbeatTimer = new System.Timers.Timer();
        _heartbeatTimer.Elapsed += HeartbeatTimer_Elapsed;
        _heartbeatTimer.AutoReset = true;
    }

    /// <summary>
    /// 异步打开串口
    /// </summary>
    public async Task<bool> OpenAsync()
    {
        if (_connectionState == ConnectionState.Connected ||
            _connectionState == ConnectionState.Connecting)
        {
            return true;
        }

        try
        {
            UpdateConnectionState(ConnectionState.Connecting, "正在连接串口...");

            _serialPort.Open();
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            UpdateConnectionState(ConnectionState.Connected, "串口连接成功");
            ConnectionOpened?.Invoke(this, $"串口 {_serialPort.PortName} 已成功连接");

            // 启用心跳检测
            StartHeartbeat();

            return true;
        }
        catch (Exception ex)
        {
            UpdateConnectionState(ConnectionState.Error, $"连接失败: {ex.Message}");
            OnConnectionError(new($"串口 {_serialPort.PortName} 连接失败", ex));

            return false;
        }
    }

    /// <summary>
    /// 同步打开串口
    /// </summary>
    public bool Open()
    {
        try
        {
            UpdateConnectionState(ConnectionState.Connecting, "正在连接串口...");

            if (!_serialPort.IsOpen)
            {
                _serialPort.Open();
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                UpdateConnectionState(ConnectionState.Connected, "串口连接成功");
                ConnectionOpened?.Invoke(this, $"串口 {_serialPort.PortName} 已成功连接");

                // 启用心跳检测
                StartHeartbeat();

                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            UpdateConnectionState(ConnectionState.Error, $"连接失败: {ex.Message}");
            OnConnectionError(new($"串口 {_serialPort.PortName} 连接失败", ex));

            return false;
        }
    }
    /// <summary>
    /// 触发连接错误事件
    /// </summary>
    protected virtual void OnConnectionError(ConnectionErrorEventArgs e)
    {
        ConnectionFailed?.Invoke(this, e);
    }

    /// <summary>
    /// 关闭串口
    /// </summary>
    public void Close()
    {
        try
        {
            if (_serialPort.IsOpen)
            {
                UpdateConnectionState(ConnectionState.Disconnecting, "正在断开连接...");

                // 停止心跳检测
                StopHeartbeat();

                _serialPort.Close();
                UpdateConnectionState(ConnectionState.Disconnected, "连接已断开");
            }
        }
        catch (Exception ex)
        {
            UpdateConnectionState(ConnectionState.Error, $"断开连接时发生错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 异步发送命令
    /// </summary>
    public async Task SendDataAsync(byte[] data)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("串口未连接");
        }

        if (data == null || data.Length == 0)
        {
            return;
        }

        try
        {
            lock (_lockObject)
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    throw new InvalidOperationException("串口已断开");
                }

                _serialPort.Write(data, 0, data.Length);

                // 重置心跳失败计数
                _heartbeatFailCount = 0;

                // 触发事件
            }
        }
        catch (Exception ex)
        {
            // 检查是否为连接断开错误
            if (IsConnectionLostException(ex))
            {
                HandleDeviceDisconnection($"发送数据时连接断开: {ex.Message}");
            }
            throw;
        }
    }

    /// <summary>
    /// 发送命令并接收返回数据
    /// </summary>
    public byte[] SendCommand(byte[] command, int? expectedResponseLength = null, int? timeout = null)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("串口未连接");
        }
        _serialPort.DiscardOutBuffer();
        _receivedBuffer.Clear();

        try
        {
            // 发送命令
            _serialPort.Write(command, 0, command.Length);

            // 重置心跳失败计数（有通信活动）
            _heartbeatFailCount = 0;

            // 等待接收数据
            var waitTime = timeout ?? _receiveTimeout;
            if (_dataReceivedEvent.WaitOne(waitTime))
            {
                if (expectedResponseLength.HasValue && _receivedBuffer.Count < expectedResponseLength.Value)
                {
                    int remainingTime = waitTime;
                    while (remainingTime > 0 && _receivedBuffer.Count < expectedResponseLength.Value)
                    {
                        Thread.Sleep(10);
                        remainingTime -= 10;
                    }
                }

                return _receivedBuffer.ToArray();
            }

            throw new TimeoutException("接收数据超时");
        }
        catch (Exception ex)
        {
            // 检查是否为连接断开错误
            if (IsConnectionLostException(ex))
            {
                HandleDeviceDisconnection($"发送命令时连接断开: {ex.Message}");
            }
            throw;
        }

    }

    /// <summary>
    /// 设置心跳检测
    /// </summary>
    /// <param name="heartbeatCommand">心跳命令</param>
    /// <param name="interval">检测间隔（毫秒）</param>
    /// <param name="maxFailCount">最大失败次数</param>
    public void SetHeartbeat(byte[] heartbeatCommand, Func<byte[], bool> check, int interval = 5000, int maxFailCount = 3)
    {
        _heartbeatCommand = heartbeatCommand;
        _heartbeatInterval = interval;
        _maxHeartbeatFailCount = maxFailCount;
        HeartbeatCheckHandler = check;
        _isHeartbeatEnabled = true;

        if (IsConnected)
        {
            StartHeartbeat();
        }
    }

    /// <summary>
    /// 启用心跳检测
    /// </summary>
    private void StartHeartbeat()
    {
        if (_isHeartbeatEnabled && _heartbeatCommand != null && _heartbeatCommand.Length > 0)
        {
            _heartbeatFailCount = 0;
            _heartbeatTimer.Interval = _heartbeatInterval;
            _heartbeatTimer.Start();
        }
    }

    /// <summary>
    /// 停止心跳检测
    /// </summary>
    private void StopHeartbeat()
    {
        if (_heartbeatTimer != null)
        {
            _heartbeatTimer.Stop();
        }
    }

    /// <summary>
    /// 心跳检测定时器事件
    /// </summary>
    private void HeartbeatTimer_Elapsed(object sender, ElapsedEventArgs e)
    {
        if (!IsConnected || _heartbeatCommand == null)
        {
            _heartbeatTimer.Stop();
            return;
        }

        try
        {
            // 发送心跳命令
            if (_serialPort.IsOpen)
            {
                _serialPort.Write(_heartbeatCommand, 0, _heartbeatCommand.Length);

                // 简单的心跳检测：检查是否有响应（可以更复杂，比如等待特定响应）
                if (_serialPort.BytesToRead > 0)
                {
                    // 读取心跳响应数据
                    byte[] buffer = new byte[_serialPort.BytesToRead];
                    _serialPort.Read(buffer, 0, buffer.Length);
                    //判断心跳行为
                    if (!HeartbeatCheckHandler?.Invoke(buffer) ?? false)
                    {
                        _heartbeatFailCount++;
                        Debug.WriteLine($"心跳检测失败 {_heartbeatFailCount}/{_maxHeartbeatFailCount}");

                        if (_heartbeatFailCount >= _maxHeartbeatFailCount)
                        {
                            HandleDeviceDisconnection("心跳检测失败，设备可能已断开");
                            return;
                        }

                        HandleDeviceDisconnection("心跳检测失败，心跳字节不正确");
                    }
                    else
                    {
                        // 有响应，重置失败计数
                        _heartbeatFailCount = 0;
                    }
                }

            }
        }
        catch (Exception ex)
        {
            if (IsConnectionLostException(ex))
            {
                HandleDeviceDisconnection($"心跳检测时连接断开: {ex.Message}");
            }
            else
            {
                _heartbeatFailCount++;
                if (_heartbeatFailCount >= _maxHeartbeatFailCount)
                {
                    HandleDeviceDisconnection($"心跳检测异常: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 处理设备断开连接
    /// </summary>
    private void HandleDeviceDisconnection(string reason)
    {
        if (_connectionState == ConnectionState.Connected)
        {
            UpdateConnectionState(ConnectionState.Error, reason);
            DeviceDisconnected?.Invoke(this, $"设备已断开: {reason}");

            // 尝试重新连接
            //ThreadPool.QueueUserWorkItem(state =>
            //{
            //    Thread.Sleep(2000); // 等待2秒后尝试重连
            //    TryReconnect();
            //});
        }
    }

    /// <summary>
    /// 尝试重新连接
    /// </summary>
    private void TryReconnect()
    {
        if (_connectionState == ConnectionState.Error)
        {
            Debug.WriteLine("尝试重新连接...");
            UpdateConnectionState(ConnectionState.Connecting, "尝试重新连接...");

            try
            {
                // 关闭现有连接
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }

                Thread.Sleep(1000);

                // 重新打开
                _serialPort.Open();
                UpdateConnectionState(ConnectionState.Connected, "重新连接成功");
                ConnectionOpened?.Invoke(this, $"串口 {_serialPort.PortName} 重新连接成功");

                // 重新启用心跳
                StartHeartbeat();
            }
            catch (Exception ex)
            {
                UpdateConnectionState(ConnectionState.Error, $"重新连接失败: {ex.Message}");
                // 继续尝试重连
                Thread.Sleep(5000);
                TryReconnect();
            }
        }
    }

    /// <summary>
    /// 检查是否为连接断开异常
    /// </summary>
    private bool IsConnectionLostException(Exception ex)
    {
        return ex is InvalidOperationException ||
               ex is UnauthorizedAccessException ||
               ex is IOException ||
               (ex.Message.Contains("端口") && ex.Message.Contains("关闭")) ||
               ex.Message.Contains("disconnected") ||
               ex.Message.Contains("not open");
    }

    /// <summary>
    /// 更新连接状态
    /// </summary>
    private void UpdateConnectionState(ConnectionState newState, string message)
    {
        if (_connectionState != newState)
        {
            _connectionState = newState;
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{_serialPort.PortName}]连接状态: {newState} - {message}");
            ConnectionStateChanged?.Invoke(newState, message);
        }
    }

    /// <summary>
    /// 串口数据接收事件
    /// </summary>
    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            int bytesToRead = _serialPort.BytesToRead;
            if (bytesToRead > 0)
            {
                byte[] buffer = new byte[bytesToRead];
                int bytesRead = _serialPort.Read(buffer, 0, bytesToRead);


                _receivedBuffer.AddRange(buffer);
                _dataReceivedEvent.Set();


                // 有数据接收，重置心跳失败计数
                _heartbeatFailCount = 0;

                DataReceived?.Invoke(this, new DataReceivedEventArgs(buffer));
            }
        }
        catch (Exception ex)
        {
            if (IsConnectionLostException(ex))
            {
                HandleDeviceDisconnection($"数据接收时连接断开: {ex.Message}");
            }
            UpdateConnectionState(ConnectionState.Error, $"数据接收异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 串口错误接收事件
    /// </summary>
    private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        string errorMsg = $"串口错误: {e.EventType}";
        UpdateConnectionState(ConnectionState.Error, errorMsg);

        // 根据错误类型处理
        switch (e.EventType)
        {
            case SerialError.RXOver:
            case SerialError.Overrun:
            case SerialError.RXParity:
            case SerialError.Frame:
                // 数据错误，可能不需要断开连接
                Debug.WriteLine($"数据通信错误: {e.EventType}");
                break;
            case SerialError.TXFull:
                // 发送缓冲区满
                Debug.WriteLine("发送缓冲区满");
                break;
        }
    }

    /// <summary>
    /// 分包发送数据
    /// </summary>
    public bool SendDataInPackets(byte[] data, int packetSize = 50,
                                 int delayBetweenPackets = 10,
                                 Action<int, int> sendProgress = null)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("串口未连接");
        }

        if (data == null || data.Length == 0)
        {
            return true;
        }

        int totalPackets = (int)Math.Ceiling(data.Length / (double)packetSize);

        try
        {
            for (int i = 0; i < totalPackets; i++)
            {
                if (!IsConnected)
                {
                    throw new InvalidOperationException("发送过程中连接断开");
                }

                int offset = i * packetSize;
                int remaining = data.Length - offset;
                int currentPacketSize = Math.Min(packetSize, remaining);

                _serialPort.Write(data, offset, currentPacketSize);
                sendProgress?.Invoke(i + 1, totalPackets);

                if (i < totalPackets - 1 && delayBetweenPackets > 0)
                {
                    Thread.Sleep(delayBetweenPackets);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            if (IsConnectionLostException(ex))
            {
                HandleDeviceDisconnection($"分包发送时连接断开: {ex.Message}");
            }
            throw;
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        StopHeartbeat();

        if (_heartbeatTimer != null)
        {
            _heartbeatTimer.Elapsed -= HeartbeatTimer_Elapsed;
            _heartbeatTimer.Dispose();
            _heartbeatTimer = null;
        }

        if (_serialPort != null)
        {
            _serialPort.DataReceived -= SerialPort_DataReceived;
            _serialPort.ErrorReceived -= SerialPort_ErrorReceived;

            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }

            _serialPort.Dispose();
            _serialPort = null;
        }

        _dataReceivedEvent?.Dispose();
        _dataReceivedEvent = null;
    }
}
#region 事件参数类

/// <summary>
/// 连接错误事件参数
/// </summary>
public class ConnectionErrorEventArgs(string errorMessage, Exception exception) : EventArgs
{
    public string ErrorMessage { get; } = errorMessage;
    public Exception Exception { get; } = exception;
    public DateTime ErrorTime { get; } = DateTime.Now;

    public override string ToString()
    {
        return $"{ErrorTime:yyyy-MM-dd HH:mm:ss} - {ErrorMessage}" +
               (Exception != null ? $"\n异常信息: {Exception.Message}" : "");
    }
}

/// <summary>
/// 数据接收事件参数
/// </summary>
public class DataReceivedEventArgs(byte[] data) : EventArgs
{
    public byte[] Data { get; } = data;
    public DateTime ReceiveTime { get; } = DateTime.Now;

    public string GetString(Encoding encoding = null)
    {
        encoding ??= Encoding.UTF8;
        return encoding.GetString(Data);
    }
}

#endregion