#nullable enable
using EasyCon.Core.Maa;
using EasyDevice;
using EasyDevice.Connection;
using EasyScript;
using System.Diagnostics;

namespace EasyCon.Core.Input;

/// <summary>物理手柄会话。只管理协议、输入所有权与连接，不认识采集或 Maa 原生对象。</summary>
internal sealed class PadSession : IDisposable
{
    private readonly NintendoSwitch _device;
    private readonly object _gate = new();
    private SourceOwnership? _ownership;
    private InputOwner _owner;
    private long _epoch;
    private bool _ready;
    private bool _disposed;

    public PadSession() : this(new NintendoSwitch()) { }
    public PadSession(NintendoSwitch device)
    {
        _device = device;
        _owner = NewOwner(null, default);
        _device.StatusChanged += status =>
        {
            if (status == Status.Error)
            {
                bool wasReady = _ready;
                _ready = false;
                InputOwner owner = CurrentOwner;
                if (wasReady || owner.RunId != null) owner.Cancel(RunCancellationReason.DeviceLost);
            }
            StatusChanged?.Invoke(status);
        };
    }

    public bool IsConnected => _ready && _device.IsConnected();
    public string? Endpoint { get; private set; }
    public long InputEpoch { get { lock (_gate) return _owner.Epoch; } }
    public bool HeldByUser { get; private set; }
    public long ConnectionEpoch { get; private set; }
    public InputOwner CurrentOwner { get { lock (_gate) return _owner; } }
    public CleanupStatus LastCleanup { get; private set; }
    public event LogHandler Log { add => _device.Log += value; remove => _device.Log -= value; }
    public event BytesTransferedHandler BytesSent { add => _device.BytesSent += value; remove => _device.BytesSent -= value; }
    public event BytesTransferedHandler BytesReceived { add => _device.BytesReceived += value; remove => _device.BytesReceived -= value; }
    public event StatusChangedHandler? StatusChanged;
    public SwitchReport GetReport() => ((IReporter)_device).GetReport();

    private InputOwner NewOwner(string? runId, CancellationToken token) => new(runId, ++_epoch, token);

    public void Validate(InputOwner owner, bool cleanup = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(owner, _owner))
                throw new ControllerOperationException("StaleEpoch", "设备写代次已失效");
            if (!cleanup) owner.Token.ThrowIfCancellationRequested();
        }
    }

    public InputOwner Transfer(string? runId, CancellationToken token)
    {
        lock (_gate)
        {
            _owner.Cancel(RunCancellationReason.UserStop);
            _owner = NewOwner(runId, token);
            return _owner;
        }
    }

    public bool Release(InputOwner owner)
    {
        lock (_gate)
        {
            Validate(owner, cleanup: true);
            bool written = _device.ResetAndWaitForWriteAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            LastCleanup = written ? CleanupStatus.Written : CleanupStatus.ReleaseUnconfirmed;
            if (!written) _ready = false;
            return written;
        }
    }

    public ControllerReply Execute(ControllerCommand command, InputOwner owner, CancellationToken operationToken)
    {
        Validate(owner);
        if (command.RunId != owner.RunId || command.Epoch != owner.Epoch)
            throw new ControllerOperationException("InvalidOwner", "请求与固定绑定的输入所有者不一致");
        if (command.Op == "device.connect")
        {
            _ready = false;
            ConnectionEpoch++;
            _device.Disconnect();
            Endpoint = null;
            HeldByUser = false;
            _ownership?.Dispose();
            _ownership = null;
            string port = command.Port ?? throw new ControllerOperationException("InvalidPort", "缺少通信端口");
            string endpoint = NormalizeEndpoint(port);
            try { _ownership = new SourceOwnership("serial:" + endpoint); }
            catch (IOException ex) { throw new ControllerOperationException("Busy", ex.Message); }
            try
            {
                NintendoSwitch.ConnectResult result = _device.TryConnect(endpoint);
                if (result != NintendoSwitch.ConnectResult.Success)
                {
                    _ownership.Dispose(); _ownership = null;
                    return new(command.RequestId, true, Value: result.ToString());
                }
                Endpoint = endpoint;
                HeldByUser = true;
                _ready = Release(owner);
                return new(command.RequestId, true, Completion: _ready ? InputCompletion.Written : InputCompletion.Accepted,
                    Value: (_ready ? ControllerConnectResult.Success : ControllerConnectResult.ReleaseUnconfirmed).ToString());
            }
            catch
            {
                _device.Disconnect();
                _ownership?.Dispose(); _ownership = null;
                throw;
            }
        }
        if (command.Op == "device.disconnect")
        {
            bool written = Release(owner);
            _ready = false;
            HeldByUser = false;
            ConnectionEpoch++;
            _device.Disconnect();
            _ownership?.Dispose(); _ownership = null;
            Endpoint = null;
            return new(command.RequestId, true, written ? null : "ReleaseUnconfirmed",
                written ? InputCompletion.Written : InputCompletion.Accepted);
        }
        if (!IsConnected) throw new ControllerOperationException("InputDisconnected", "手柄未连接或中性报告尚未确认");
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, operationToken);
        CancellationToken token = cancellation.Token;
        token.ThrowIfCancellationRequested();
        string? value = null;
        bool accepted = true;
        InputCompletion completion = InputCompletion.Accepted;
        switch (command.Op)
        {
            case "input.down": _device.Down(RequiredKey(command)); break;
            case "input.up": _device.Up(RequiredKey(command)); break;
            case "input.pulse":
                if (command.Duration < 0) throw new ControllerOperationException("InvalidDuration", "按键持续时间不能为负");
                ECKey key = RequiredKey(command);
                _device.Down(key);
                try { Delay(command.Duration, command.Timing, token); }
                finally { lock (_gate) { Validate(owner, cleanup: true); _device.Up(key); } }
                break;
            case "input.report":
                if (command.Report == null || command.Report.Hat > 8) throw new ControllerOperationException("InvalidReport", "无效的完整手柄报告");
                _device.ApplyReport(command.Report.ToReport()); break;
            case "input.left": ValidateDirection(command.Direction); _device.LeftDirection(command.Direction, command.Down); break;
            case "input.right": ValidateDirection(command.Direction); _device.RightDirection(command.Direction, command.Down); break;
            case "input.hat": ValidateDirection(command.Direction); _device.HatDirection(command.Direction, command.Down); break;
            case "input.reset": accepted = Release(owner); completion = InputCompletion.Written; break;
            case "amiibo.index": accepted = _device.ChangeAmiiboIndex(command.Index); break;
            case "device.flash": accepted = _device.Flash(RequiredData(command)); break;
            case "device.start": accepted = _device.RemoteStart(); break;
            case "device.stop": accepted = _device.RemoteStop(); break;
            case "device.version": value = _device.GetVersion().ToString(); break;
            case "device.unpair": accepted = _device.UnPair(); break;
            case "device.mode": accepted = _device.ChangeControllerMode(command.Index); break;
            case "device.color": accepted = _device.ChangeControllerColor(RequiredData(command)); break;
            case "amiibo.save": accepted = _device.SaveAmiibo(command.Index, RequiredData(command)); break;
            case "record.start": _device.StartRecord(); break;
            case "record.stop": _device.StopRecord(); break;
            case "record.pause": _device.PauseRecord(); break;
            case "record.script": value = _device.GetRecordScript(); break;
            default: throw new ControllerOperationException("UnsupportedOperation", $"不支持的控制器操作 {command.Op}");
        }
        return new(command.RequestId, accepted, accepted ? null : "ProtocolFailure", accepted ? completion : InputCompletion.Accepted, value);
    }

    public bool StandardKey(InputOwner owner, int code, bool down)
    {
        lock (_gate)
        {
            Validate(owner, cleanup: !down);
            if (!IsConnected) return false;
            ECKey? key = code switch
            {
                >= 0 and <= 15 => ECKeyUtil.Button((SwitchButton)(1 << code)),
                >= 16 and <= 24 => ECKeyUtil.HAT((SwitchHAT)(code - 16)),
                _ => null,
            };
            if (key == null) return false;
            if (down) _device.Down(key); else _device.Up(key);
            return true;
        }
    }

    private static string NormalizeEndpoint(string port)
    {
        string endpoint = port.Trim();
        if (OperatingSystem.IsWindows())
        {
            endpoint = endpoint.StartsWith(@"\\.\", StringComparison.Ordinal) ? endpoint[4..] : endpoint;
            return endpoint.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && endpoint.Length > 3 && endpoint[3..].All(char.IsAsciiDigit)
                ? endpoint.ToUpperInvariant() : endpoint;
        }
        if (Path.IsPathRooted(endpoint))
            return new FileInfo(endpoint).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(endpoint);
        return endpoint;
    }
    private static ECKey RequiredKey(ControllerCommand command) => command.Key?.ToKey() ?? throw new ControllerOperationException("InvalidKey", "缺少按键");
    private static byte[] RequiredData(ControllerCommand command) => command.Data ?? throw new ControllerOperationException("InvalidData", "缺少数据");
    private static void ValidateDirection(DirectionKey direction)
    {
        if (((int)direction & ~15) != 0) throw new ControllerOperationException("InvalidDirection", "无效方向");
    }
    private static void Delay(int duration, DelayType timing, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (timing == DelayType.HighResolution) CustomDelay.Delay(duration, token);
        else if (timing == DelayType.Normal) token.WaitHandle.WaitOne(duration);
        else
        {
            long end = Stopwatch.GetTimestamp() + (long)(duration * (double)Stopwatch.Frequency / 1000);
            while (Stopwatch.GetTimestamp() < end)
            {
                token.ThrowIfCancellationRequested();
                int remaining = (int)((end - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency);
                if (remaining > 0) CustomDelay.AISleep(Math.Min(remaining, 10));
                else Thread.Yield();
            }
        }
        token.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Cancel(RunCancellationReason.HostShutdown);
        _device.Disconnect();
        _ownership?.Dispose();
        _owner.Dispose();
    }
}

internal sealed class InputOwner : IDisposable
{
    private readonly CancellationTokenSource _cancellation;
    public InputOwner(string? runId, long epoch, CancellationToken token)
    {
        RunId = runId;
        Epoch = epoch;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Token = _cancellation.Token;
    }
    public string? RunId { get; }
    public long Epoch { get; }
    public CancellationToken Token { get; }
    public RunCancellationReason? Reason { get; private set; }

    public void Cancel(RunCancellationReason reason)
    {
        Reason ??= reason;
        try { _cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
    }
    public void Dispose() => _cancellation.Dispose();
}