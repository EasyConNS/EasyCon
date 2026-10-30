#nullable enable
using EasyCon.Core.Capabilities;
using EasyCon.Core.Input;
using EasyDevice;
using EasyDevice.Connection;
using EasyScript;
using MaaFramework.Binding;

namespace EasyCon.Core.Maa;

/// <summary>手柄模块公开门面；只接收输入配置，不引用采集模块。每个运行拥有独立 Maa 绑定。</summary>
public sealed class MaaControllerSession : IControllerSession
{
    private readonly PadSession _pad;
    private readonly object _transition = new();
    private readonly object _stateGate = new();
    private readonly object _lifecycleGate = new();
    private readonly ManualInputQueue _manualQueue = new();
    private MaaControllerBinding _manual;
    private ControllerRunLease? _active;
    private bool _transitioning;
    private bool _disposed;

    public MaaControllerSession() : this(new PadSession()) { }
    internal MaaControllerSession(NintendoSwitch device) : this(new PadSession(device)) { }
    private MaaControllerSession(PadSession pad)
    {
        _pad = pad;
        _manual = new MaaControllerBinding(_pad, _pad.CurrentOwner);
    }

    public bool InputConnected => _pad.IsConnected;
    public bool SupportsLegacyCommands => _pad.SupportsLegacyCommands;
    public string? FirmwareName => _pad.FirmwareName;
    public string? FirmwareVersion => _pad.FirmwareVersion;
    public bool? ConsoleConnected => _pad.ConsoleConnected;
    public string? LastConnectionError => _pad.LastConnectionError;
    public string? InputEndpoint => _pad.Endpoint;
    public long InputEpoch => _pad.InputEpoch;
    public long ConnectionEpoch => _pad.ConnectionEpoch;
    public bool ControllerConnected => _manual.BindingReady;
    public bool IsConnected() => InputConnected;
    public bool CanAcceptManualInput { get { lock (_stateGate) return !_disposed && !_transitioning && _active == null && InputConnected; } }
    public CleanupStatus LastCleanup => _pad.LastCleanup;
    public event LogHandler Log { add => _pad.Log += value; remove => _pad.Log -= value; }
    public event BytesTransferedHandler BytesSent { add => _pad.BytesSent += value; remove => _pad.BytesSent -= value; }
    public event BytesTransferedHandler BytesReceived { add => _pad.BytesReceived += value; remove => _pad.BytesReceived -= value; }
    public event StatusChangedHandler? StatusChanged { add => _pad.StatusChanged += value; remove => _pad.StatusChanged -= value; }
    public SwitchReport GetReport() => _pad.GetReport();

    private void DrainActive(RunCancellationReason reason)
    {
        ControllerRunLease? run;
        lock (_stateGate) { _transitioning = true; run = _active; }
        if (run == null) return;
        run.Cancel(reason);
        run.Completion.GetAwaiter().GetResult();
    }

    private void RenewManual()
    {
        _manualQueue.Drain();
        InputOwner previous = _pad.CurrentOwner;
        MaaControllerBinding previousBinding = _manual;
        InputOwner owner = _pad.Transfer(null, default);
        _manual = new MaaControllerBinding(_pad, owner);
        previousBinding.Dispose();
        previous.Dispose();
    }

    public ControllerConnectResult TryConnect(string port)
    {
        lock (_lifecycleGate)
        {
            if (string.IsNullOrWhiteSpace(port)) return ControllerConnectResult.InvalidArgument;
            DrainActive(RunCancellationReason.DeviceLost);
            lock (_transition)
            {
                try
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    RenewManual();
                    ControllerReply reply = _manual.Send(new() { Op = "device.connect", Port = port }, default);
                    ControllerConnectResult result = Enum.Parse<ControllerConnectResult>(reply.Value!);
                    if (result == ControllerConnectResult.Success &&
                        (_manual.Native.LinkStart().Wait() != MaaJobStatus.Succeeded || !_manual.IsInitialized))
                        return ControllerConnectResult.Error;
                    return result;
                }
                catch (ControllerOperationException ex) when (ex.ErrorCode == "Busy") { return ControllerConnectResult.Busy; }
                catch (ControllerOperationException ex) when (ex.ErrorCode == "CallbackFailure")
                {
                    Logging.CoreLog.Warn($"手柄端点打开失败: {port}: {ex.Message}");
                    return ControllerConnectResult.Error;
                }
                finally { lock (_stateGate) _transitioning = false; }
            }
        }
    }

    public void Disconnect()
    {
        lock (_lifecycleGate)
        {
            DrainActive(RunCancellationReason.DeviceLost);
            lock (_transition)
            {
                try
                {
                    if (_disposed || _pad.Endpoint == null) return;
                    _manualQueue.Drain();
                    _manual.Send(new() { Op = "device.disconnect" }, default);
                    RenewManual();
                }
                finally { lock (_stateGate) _transitioning = false; }
            }
        }
    }

    public ControllerRunLease BeginRun(string runId, CancellationToken token, long? expectedConnectionEpoch = null)
    {
        lock (_lifecycleGate)
            lock (_transition)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                token.ThrowIfCancellationRequested();
                if (expectedConnectionEpoch != null && expectedConnectionEpoch != ConnectionEpoch)
                    throw new ControllerOperationException("InputChanged", "运行准备期间手柄连接已更换");
                lock (_stateGate)
                {
                    if (_active != null || _transitioning) throw new ControllerOperationException("Busy", "busy: 设备已有运行或正在收尾");
                    if (!InputConnected) throw new ControllerOperationException("InputDisconnected", "手柄未连接或中性报告尚未确认");
                    _transitioning = true;
                }
                try
                {
                    // 手动输入交接先写出中性包。旧句柄失效后销毁，不能释放新所有者的按键。
                    _manualQueue.Drain();
                    if ((_manual.HasNative || LastCleanup != CleanupStatus.Written) && !_manual.ReleaseInput())
                        throw new ControllerOperationException("ReleaseUnconfirmed", "手动输入中性报告未确认");
                    InputOwner previous = _pad.CurrentOwner;
                    InputOwner owner = _pad.Transfer(runId, token);
                    _manual.Dispose();
                    previous.Dispose();
                    ControllerRunLease run = new(this, owner, new MaaControllerBinding(_pad, owner));
                    lock (_stateGate) _active = run;
                    return run;
                }
                finally { lock (_stateGate) _transitioning = false; }
            }
    }

    public IPadInput CreatePad(DelayType timing, ControllerRunLease? run = null)
    {
        if (run != null && run.Owner != this) throw new ControllerOperationException("InvalidOwner", "运行租约不属于此控制器");
        return new MaaPadInput(this, timing, run);
    }

    internal ControllerReply Send(ControllerCommand command, ControllerRunLease? run = null, CancellationToken token = default)
    {
        if (run != null)
        {
            lock (_stateGate)
                if (_active != run) throw new ControllerOperationException("StaleEpoch", "运行代次已失效");
            return run.Binding.Send(command, token);
        }
        lock (_transition)
        {
            lock (_stateGate)
                if (_active != null || _transitioning) throw new ControllerOperationException("Busy", "busy: 设备正在运行或切换连接");
            _manualQueue.Drain();
            return _manual.Send(command, token);
        }
    }

    internal CleanupStatus EndRun(ControllerRunLease run)
    {
        run.Cancel();
        run.Binding.Stop();
        bool written = !run.Binding.HasNative && LastCleanup == CleanupStatus.Written || run.Binding.ReleaseInput();
        run.Binding.Dispose();
        lock (_transition)
        {
            InputOwner owner = _pad.Transfer(null, default);
            _manual = new MaaControllerBinding(_pad, owner);
            lock (_stateGate) _active = null;
            run.InputOwner.Dispose();
        }
        return written ? CleanupStatus.Written : CleanupStatus.ReleaseUnconfirmed;
    }

    private void PostManual(ControllerCommand command)
    {
        if (!Monitor.TryEnter(_transition)) throw new ControllerOperationException("Busy", "设备正在切换输入所有者");
        try
        {
            if (!CanAcceptManualInput) throw new ControllerOperationException("Busy", "设备正由其他运行使用或尚未连接");
            _manualQueue.Post(_manual, command);
        }
        finally { Monitor.Exit(_transition); }
    }
    public void Down(ECKey key) => PostManual(new() { Op = "input.down", Key = ControllerKey.FromKey(key) });
    public void Up(ECKey key) => PostManual(new() { Op = "input.up", Key = ControllerKey.FromKey(key) });
    public void LeftDirection(DirectionKey key, bool down) => PostManual(new() { Op = "input.left", Direction = key, Down = down });
    public void RightDirection(DirectionKey key, bool down) => PostManual(new() { Op = "input.right", Direction = key, Down = down });
    public void HatDirection(DirectionKey key, bool down) => PostManual(new() { Op = "input.hat", Direction = key, Down = down });
    public void ApplyReport(SwitchReport report) => PostManual(new() { Op = "input.report", Report = ControllerReport.FromReport(report) });
    public void Reset() { if (CanAcceptManualInput) PostManual(new() { Op = "input.reset" }); }
    public bool Flash(byte[] bytes) => Command("device.flash", data: bytes);
    public bool RemoteStart() => Command("device.start");
    public bool RemoteStop() => Command("device.stop");
    public int GetVersion() => int.Parse(Send(new() { Op = "device.version" }).Value!);
    public bool UnPair() => Command("device.unpair");
    public bool ChangeAmiiboIndex(byte index) => Command("amiibo.index", index);
    public bool ChangeControllerMode(byte mode) => Command("device.mode", mode);
    public bool ChangeControllerColor(byte[] color) => Command("device.color", data: color);
    public bool SaveAmiibo(byte index, byte[] data) => Command("amiibo.save", index, data);
    public void StartRecord() => Command("record.start");
    public void StopRecord() => Command("record.stop");
    public void PauseRecord() => Command("record.pause");
    public string GetRecordScript() => Send(new() { Op = "record.script" }).Value ?? "";
    private bool Command(string op, byte index = 0, byte[]? data = null)
        => Send(new() { Op = op, Index = index, Data = data?.ToArray() }).Accepted;

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            DrainActive(RunCancellationReason.HostShutdown);
            Disconnect();
            lock (_transition)
            {
                if (_disposed) return;
                _manualQueue.Dispose();
                _manual.Dispose();
                _pad.Dispose();
                _disposed = true;
            }
        }
    }
}