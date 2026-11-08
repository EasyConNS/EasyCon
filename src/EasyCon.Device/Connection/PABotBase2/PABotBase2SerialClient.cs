using System.Diagnostics;

namespace EasyDevice.Connection.PABotBase2;

/// <summary>Online NS1 wireless Pro Controller input over a PABotBase2 USB serial connection.</summary>
public sealed class PABotBase2SerialClient : IConnection
{
    public const int DefaultBaudRate = 921600;
    private readonly string _endpoint;
    private readonly IPABotBase2SerialPort _port;
    private readonly PABotBase2Session _session;
    private readonly object _gate = new();
    private readonly Queue<PendingReport> _queue = new();
    private readonly CancellationTokenSource _cancel = new();
    private sealed record PendingReport(byte[] Buttons, byte[] Packed);
    private Task? _worker;
    private volatile Status _status = Status.Connecting;
    private string? _lastError;
    private int _disconnected;
    private bool _resetRequested;

    public PABotBase2SerialClient(string endpoint, int baudRate = DefaultBaudRate)
        : this(endpoint, new PABotBase2SerialPort(endpoint, baudRate)) { }

    internal PABotBase2SerialClient(string endpoint, IPABotBase2SerialPort port)
    {
        _endpoint = endpoint;
        _port = port;
        _session = new(port);
    }

    public override event BytesTransferedHandler BytesSent = delegate { };
    public override event StatusChangedHandler StatusChanged = delegate { };
    public override int ConnectTimeoutMilliseconds => 7000;
    public override bool SupportsLegacyCommands => false;
    public override string FirmwareName => "PABotBase2 (串口)";
    public override string? FirmwareVersion => _session.FirmwareVersion;
    public override bool? ConsoleConnected => CurrentStatus == Status.Connected ? _session.ConsoleConnected : null;
    public override string? LastError => Volatile.Read(ref _lastError);

    public override Status CurrentStatus
    {
        get => _status;
        protected set
        {
            if (_status == value)
                return;
            _status = value;
            StatusChanged(value);
        }
    }

    public override void Connect()
    {
        lock (_gate)
        {
            if (_disconnected == 0)
                _worker ??= Task.Run(Run);
        }
    }

    public override void Write(params byte[] val)
        => throw new NotSupportedException("PABotBase2 不支持伊机控原生固件命令；手柄输入请使用 WriteReport。");

    public override void WriteReport(SwitchReport report)
    {
        lock (_gate)
        {
            if (_disconnected != 0 || _cancel.IsCancellationRequested || CurrentStatus != Status.Connected)
                return;
            if (_queue.Count >= 256)
            {
                _lastError = "PABotBase2 发送队列已满，请降低脚本按键频率。";
                _cancel.Cancel();
                return;
            }
            // Copy both encodings before returning: callers reuse and mutate SwitchReport.
            _queue.Enqueue(new(PABotBase2Protocol.Buttons(report), report.GetBytes()));
        }
    }

    public override void ClearQueue()
    {
        lock (_gate)
        {
            _queue.Clear();
            if (_disconnected == 0 && CurrentStatus == Status.Connected)
                _resetRequested = true;
        }
    }

    public override void Disconnect()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) != 0)
            return;
        Task? worker;
        lock (_gate)
        {
            _queue.Clear();
            worker = _worker;
        }
        if (CurrentStatus != Status.Connected)
            _cancel.Cancel();
        if (worker == null)
        {
            _port.Dispose();
            return;
        }
        if (Task.CurrentId == worker.Id)
            return;
        if (!worker.Wait(3500))
        {
            _cancel.Cancel();
            if (!worker.Wait(1500))
                _port.Dispose();
        }
    }

    private void Run()
    {
        try
        {
            using (CancellationTokenSource connect = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token))
            {
                connect.CancelAfter(6000);
                _session.Connect(connect.Token);
            }
            _cancel.Token.ThrowIfCancellationRequested();
            CurrentStatus = Status.Connected;
            byte[] neutral = PABotBase2Protocol.Buttons(new SwitchReport());
            PendingReport? held = null;
            long nextReport = 0;
            long nextRefresh = 0;
            long nextStatus = AfterMilliseconds(1000);
            while (Volatile.Read(ref _disconnected) == 0 && !_cancel.IsCancellationRequested)
            {
                long now = Stopwatch.GetTimestamp();
                bool reset;
                PendingReport? report = null;
                lock (_gate)
                {
                    reset = _resetRequested;
                    _resetRequested = false;
                    if (!reset && now >= nextReport)
                        _queue.TryDequeue(out report);
                }
                if (reset)
                {
                    _session.Stop(_cancel.Token);
                    held = null;
                    nextReport = AfterMilliseconds(30);
                    BytesSent(_endpoint, new SwitchReport().GetBytes());
                }
                else if (report != null)
                {
                    _session.SendReport(report.Buttons, _cancel.Token);
                    held = report.Buttons.AsSpan().SequenceEqual(neutral) ? null : report;
                    nextReport = AfterMilliseconds(30);
                    nextRefresh = AfterMilliseconds(200);
                    // Same acknowledgement payload as the existing EasyCon reset/write contract.
                    BytesSent(_endpoint, report.Packed);
                }
                else if (held != null && now >= nextRefresh)
                {
                    _session.SendReport(held.Buttons, _cancel.Token);
                    nextRefresh = AfterMilliseconds(200);
                }
                if (Stopwatch.GetTimestamp() >= nextStatus)
                {
                    _session.PollStatus(_cancel.Token);
                    nextStatus = AfterMilliseconds(1000);
                }
                // Read even while idle so firmware messages are ACKed promptly.
                _session.Pump(_cancel.Token);
            }
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        catch (OperationCanceledException) { _lastError = "PABotBase2 连接初始化超时。"; }
        catch (Exception ex) { _lastError = ex.Message; }
        finally
        {
            if (_session.Ready)
            {
                using CancellationTokenSource release = new(TimeSpan.FromSeconds(1));
                try { _session.Stop(release.Token); }
                catch (Exception ex)
                {
                    _lastError ??= $"PABotBase2 释放输入未确认: {ex.Message}";
                    Debug.WriteLine(ex);
                }
            }
            _port.Dispose();
            CurrentStatus = _lastError == null ? Status.Connecting : Status.Error;
        }
    }

    private static long AfterMilliseconds(int milliseconds)
        => Stopwatch.GetTimestamp() + milliseconds * Stopwatch.Frequency / 1000;
}