using System.Diagnostics;

namespace EasyDevice.Connection.Pokeldn;

/// <summary>串行请求/ACK、状态轮询和停止释放共用一条工作循环，不在 UI 线程进行 I/O。</summary>
internal sealed class PokeldnConnection(string endpoint, IPokeldnTransport transport) : IConnection
{
    private readonly object _gate = new();
    private sealed record PendingReport(byte[] Payload, byte[] Packed);
    private readonly Queue<PendingReport?> _queue = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _cancel = new();
    private Task? _worker;
    private PokeldnStatus? _padStatus;
    private string? _lastError;
    private int _disconnected;
    private volatile Status _status = Status.Connecting;

    public override event BytesTransferedHandler BytesSent = delegate { };
    public override event StatusChangedHandler StatusChanged = delegate { };

    public override int ConnectTimeoutMilliseconds => transport.ConnectTimeoutMilliseconds;
    public override bool SupportsLegacyCommands => false;
    public override string FirmwareName => transport is PokeldnSerialTransport ? "pokeldn-pad (串口)" : "pokeldn-pad (本地服务)";
    public override string? FirmwareVersion => Volatile.Read(ref _padStatus)?.Version;
    public override bool? ConsoleConnected => Volatile.Read(ref _padStatus)?.Mounted;
    public override string? LastError => _lastError;

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
            _worker ??= Task.Run(Run);
    }

    public override void Write(params byte[] val)
        => throw new NotSupportedException("pokeldn-pad 不支持伊机控原生固件命令。");

    public override void WriteReport(SwitchReport report)
    {
        lock (_gate)
        {
            if (_disconnected != 0 || _cancel.IsCancellationRequested || CurrentStatus != Status.Connected)
                return;
            // 保留按下/释放边沿；慢 BLE 链路积压时明确报错，不能静默丢掉释放包。
            if (_queue.Count >= 256)
            {
                _lastError = "pokeldn 发送队列已满，请降低脚本按键频率。";
                _cancel.Cancel();
                return;
            }
            _queue.Enqueue(Snapshot(report));
            _wake.Set();
        }
    }

    public override void ClearQueue()
    {
        lock (_gate)
        {
            _queue.Clear();
            if (_disconnected == 0 && !_cancel.IsCancellationRequested && CurrentStatus == Status.Connected)
            {
                // STOP 同时中止固件宏并释放所有按键；仅发中立报告会被正在播放的宏忽略。
                _queue.Enqueue(null);
                _wake.Set();
            }
        }
    }

    public override void Disconnect()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) != 0)
            return;
        // 已连接时先收完正在进行的 ACK。中途取消 ReadLine 会丢掉半行，后续释放请求
        // 可能误用迟到的 ACK；连接/扫描尚未完成时则立即取消。
        if (CurrentStatus != Status.Connected)
            _cancel.Cancel();
        _wake.Set();
        lock (_gate)
            _queue.Clear();
        Task? worker = _worker;
        if (worker == null)
        {
            transport.Dispose();
            return;
        }
        if (!worker.Wait(3500))
        {
            _cancel.Cancel();
            if (!worker.Wait(2500))
                transport.Dispose();
        }
    }

    private void Run()
    {
        bool opened = false;
        try
        {
            CancellationToken token = _cancel.Token;
            _padStatus = transport.Connect(token);
            opened = true;
            transport.Stop(token);
            Send(Snapshot(new SwitchReport()), token);
            token.ThrowIfCancellationRequested();
            CurrentStatus = Status.Connected;
            Stopwatch poll = Stopwatch.StartNew();
            while (Volatile.Read(ref _disconnected) == 0 && !token.IsCancellationRequested)
            {
                // 队列锁不覆盖 I/O：慢 BLE 请求不能阻塞 UI/Reset，也不能在锁内回调宿主。
                // ClearQueue 丢弃排队项；已出队的一条结束后，下一条必为 STOP。
                bool hasWork;
                PendingReport? report;
                lock (_gate)
                    hasWork = _queue.TryDequeue(out report);
                if (hasWork)
                {
                    token.ThrowIfCancellationRequested();
                    if (report == null)
                        transport.Stop(token);
                    else
                        Send(report, token);
                }
                if (poll.ElapsedMilliseconds >= 1000)
                {
                    Volatile.Write(ref _padStatus, transport.GetStatus(token));
                    poll.Restart();
                }
                bool idle;
                lock (_gate)
                    idle = _queue.Count == 0;
                if (idle)
                    WaitHandle.WaitAny([_wake, token.WaitHandle], 100);
            }
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _lastError = ex.Message;
        }
        finally
        {
            if (opened)
            {
                // 正常退出/停止时，UART 固件不会感知电脑关闭了端口，必须主动释放。
                using CancellationTokenSource release = new(TimeSpan.FromSeconds(2));
                try { transport.Stop(release.Token); }
                catch (Exception ex) { Debug.WriteLine(ex); }
                try { transport.SendReport(PokeldnProtocol.EncodeReport(new SwitchReport()), release.Token); }
                catch (Exception ex) { Debug.WriteLine(ex); }
                try { transport.Disconnect(release.Token); }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }
            transport.Dispose();
            CurrentStatus = _lastError == null ? Status.Connecting : Status.Error;
        }
    }

    private static PendingReport Snapshot(SwitchReport report) => new(PokeldnProtocol.EncodeReport(report), report.GetBytes());

    private void Send(PendingReport report, CancellationToken token)
    {
        transport.SendReport(report.Payload, token);
        // 与 TTLSerialClient 保持同一事件契约；上层用打包后的中立报告确认释放。
        // pokeldn 额外等待固件/服务 ACK，但这仍不代表 NS 或游戏已确认输入。
        BytesSent(endpoint, report.Packed);
    }
}