#nullable enable
using EasyCon.Capture;
using EasyCon.Core.Capabilities;
using EasyScript;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Core.Maa;

public enum RunCancellationReason { UserStop, Deadline, DeviceLost, CaptureLost, HostShutdown }
public sealed record EffectiveRunConfig(string? InputEndpoint, long? InputEpoch, string? CaptureSource,
    string? CaptureBackend, long? SourceEpoch, bool RequiresInput, bool RequiresCapture);
public sealed record HostRunResult(string RunId, string EntryKind, string? Source,
    string BusinessStatus, string NativeStatus, string? ErrorCode, TimeSpan Elapsed,
    CleanupStatus CleanupStatus, EffectiveRunConfig EffectiveConfig,
    string CompatibilityProfile = "EasyCon.Legacy", string? ResourceGeneration = null,
    string? InvocationId = null, string? ErrorNode = null, string? DiagnosticPath = null);

/// <summary>顶层运行的统一停止域；模块按需登记依赖，不从其他模块的状态推断连接。</summary>
public sealed class HostRunCoordinator(IControllerSession controller, FrameHub? frames = null)
{
    public HostRunResult? LastResult { get; private set; }
    public string DiagnosticDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyCon", "run-reports");
    public event Action<HostRunResult>? Completed;

    public void Run(string entryKind, string? source, bool requiresInput, CancellationToken token,
        Action<HostRunScope> execute, bool requiresCapture = false, string compatibilityProfile = "EasyCon.Legacy")
    {
        using HostRunScope scope = new(controller, frames, token, requiresInput, requiresCapture);
        Stopwatch elapsed = Stopwatch.StartNew();
        string status = "Succeeded";
        string? error = null;
        try
        {
            scope.Token.ThrowIfCancellationRequested();
            if (requiresInput) scope.RequireInput();
            if (requiresCapture) scope.RequireCapture(waitForFrame: true);
            execute(scope);
            scope.Token.ThrowIfCancellationRequested();
            if (scope.BusinessError != null) { status = "Failed"; error = scope.BusinessError; }
        }
        catch (OperationCanceledException)
        {
            RunCancellationReason reason = scope.Reason ?? RunCancellationReason.UserStop;
            status = reason == RunCancellationReason.UserStop ? "Cancelled" : "Failed";
            error = reason.ToString();
            throw;
        }
        catch (Exception ex)
        {
            status = "Failed";
            error = ex is ControllerOperationException controllerError ? controllerError.ErrorCode : ex.GetType().Name;
            throw;
        }
        finally
        {
            CleanupStatus cleanup = scope.Finish();
            if (cleanup == CleanupStatus.ReleaseUnconfirmed) { status = "Failed"; error ??= "ReleaseUnconfirmed"; }
            HostRunResult result = new(scope.RunId, entryKind, source, status, scope.NativeStatus, error,
                elapsed.Elapsed, cleanup, scope.EffectiveConfig, CompatibilityProfile: compatibilityProfile,
                InvocationId: entryKind == "ecs" ? scope.RunId + ":ecs" : null, ErrorNode: scope.ErrorNode);
            try
            {
                Directory.CreateDirectory(DiagnosticDirectory);
                string path = Path.Combine(DiagnosticDirectory, "run-" + scope.RunId + ".json");
                result = result with { DiagnosticPath = path };
                File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() },
                }));
                foreach (FileInfo old in new DirectoryInfo(DiagnosticDirectory).GetFiles("run-*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(128))
                    old.Delete();
            }
            catch (IOException ex) { Logging.CoreLog.Warn($"运行报告未能写入: {ex.Message}"); }
            LastResult = result;
            Completed?.Invoke(result);
            if (cleanup == CleanupStatus.ReleaseUnconfirmed && error == "ReleaseUnconfirmed")
                throw new ControllerOperationException("ReleaseUnconfirmed", "输入释放未确认；请检查连接并重新连接手柄");
        }
    }
}

/// <summary>一次运行按需借用两个模块。纯计算不取得硬件租约；已有图片 OCR 不登记采集依赖。</summary>
public sealed class HostRunScope : IDisposable
{
    private readonly IControllerSession _controller;
    private readonly FrameHub? _frames;
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _externalCancellation;
    private readonly object _gate = new();
    private ControllerRunLease? _input;
    private CaptureUsageLease? _capture;
    private CancellationTokenRegistration _inputCancellation;
    private CancellationTokenRegistration _captureCancellation;
    private bool _finishing;
    private bool _finished;
    private CleanupStatus _cleanup;
    private RunCancellationReason? _reason;
    private readonly long _initialSourceEpoch;
    private readonly long _initialInputConnection;
    public string RunId { get; } = Guid.NewGuid().ToString("N");
    public CancellationToken Token { get; }
    public RunCancellationReason? Reason => _reason;
    public string? BusinessError { get; private set; }
    public string? ErrorNode { get; private set; }
    public void MarkFailed(string code, string? node = null) { BusinessError ??= code; ErrorNode ??= node; }
    public string NativeStatus => _input?.Binding.LastNativeStatus ?? "NotRequired";
    public EffectiveRunConfig EffectiveConfig { get; private set; } = new(null, null, null, null, null, false, false);

    internal HostRunScope(IControllerSession controller, FrameHub? frames, CancellationToken token, bool requiresInput = false, bool requiresCapture = false)
    {
        _controller = controller;
        EffectiveConfig = EffectiveConfig with { RequiresInput = requiresInput, RequiresCapture = requiresCapture };
        _frames = frames;
        _initialSourceEpoch = frames?.SourceEpoch ?? 0;
        _initialInputConnection = controller.ConnectionEpoch;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Token = _cancellation.Token;
        _externalCancellation = token.Register(() => Cancel(RunCancellationReason.UserStop));
    }

    public void Cancel(RunCancellationReason reason)
    {
        lock (_gate)
        {
            if (_finished || _finishing) return;
            _reason ??= reason;
        }
        _cancellation.Cancel();
    }

    public ControllerRunLease RequireInput()
    {
        lock (_gate)
        {
            Token.ThrowIfCancellationRequested();
            if (_input != null) return _input;
            if (_controller.ConnectionEpoch != _initialInputConnection)
                throw new ControllerOperationException("InputChanged", "本次运行选定的手柄连接已更换");
            _input = _controller.BeginRun(RunId, Token, _initialInputConnection);
            _inputCancellation = _input.Token.Register(() => Cancel(_input.CancellationReason ?? RunCancellationReason.DeviceLost));
            EffectiveConfig = EffectiveConfig with
            {
                InputEndpoint = _controller.InputEndpoint,
                InputEpoch = _input.Epoch,
                RequiresInput = true,
            };
            return _input;
        }
    }

    public void RequireCapture(bool waitForFrame = false)
    {
        if (_frames == null) return;
        lock (_gate)
        {
            Token.ThrowIfCancellationRequested();
            if (_capture == null)
            {
                if (_frames.SourceEpoch != _initialSourceEpoch)
                    throw new ControllerOperationException("CaptureChanged", "本次运行选定的采集源已更换");
                _capture = _frames.AcquireUsage();
                _captureCancellation = _capture.Token.Register(() => Cancel(_capture.Failure == CaptureFailure.HostShutdown
                    ? RunCancellationReason.HostShutdown : RunCancellationReason.CaptureLost));
                EffectiveConfig = EffectiveConfig with
                {
                    CaptureSource = _frames.SourceId,
                    CaptureBackend = _frames.Backend?.ToString(),
                    SourceEpoch = _capture.SourceEpoch,
                    RequiresCapture = true,
                };
            }
        }
        if (waitForFrame && !_frames.FrameReady)
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(_frames.FrameTimeout);
            long freshAfter = Stopwatch.GetTimestamp() - (long)(_frames.FrameTimeout.TotalSeconds * Stopwatch.Frequency);
            try
            {
                using FrameLease frame = _frames.WaitForFrameAfterAsync(0, freshAfter, deadline.Token).GetAwaiter().GetResult();
                if (frame.Metadata.SourceEpoch != _capture!.SourceEpoch)
                    throw new ControllerOperationException("CaptureChanged", "等待期间采集代次已更换");
            }
            catch (OperationCanceledException) when (!Token.IsCancellationRequested)
            {
                Cancel(RunCancellationReason.CaptureLost);
                throw new ControllerOperationException("FrameUnavailable", "等待当前采集源的首帧超时");
            }
        }
    }

    public IPadInput CreatePad(DelayType timing = DelayType.LowCPU) => new RunPad(this, timing);
    public ICaptureSource WrapCapture(ICaptureSource capture) => new RunCapture(this, capture);
    public FrameLease? AcquireFrame(Func<FrameLease?> fallback)
    {
        if (_frames?.CaptureConnected == true) RequireCapture();
        Token.ThrowIfCancellationRequested();
        return _frames != null ? _frames.AcquireLatest() : fallback();
    }

    internal CleanupStatus Finish()
    {
        lock (_gate)
        {
            if (_finished) return _cleanup;
            _finishing = true;
        }
        _cleanup = _input?.StopAsync().GetAwaiter().GetResult() ?? CleanupStatus.NotRequired;
        _inputCancellation.Dispose();
        _captureCancellation.Dispose();
        _capture?.Dispose();
        lock (_gate) _finished = true;
        return _cleanup;
    }

    public void Dispose()
    {
        Finish();
        _externalCancellation.Dispose();
        _cancellation.Dispose();
    }

    private sealed class RunPad(HostRunScope scope, DelayType timing) : IPadInput
    {
        private IPadInput Pad => scope._controller.CreatePad(timing, scope.RequireInput());
        public void ClickButtons(GamePadKey key, int duration, CancellationToken token) => Pad.ClickButtons(key, duration, token);
        public void PressButtons(GamePadKey key) => Pad.PressButtons(key);
        public void ReleaseButtons(GamePadKey key) => Pad.ReleaseButtons(key);
        public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token) => Pad.ClickStick(key, x, y, duration, token);
        public void SetStick(GamePadKey key, byte x, byte y) => Pad.SetStick(key, x, y);
        public void ChangeAmiibo(uint index) => Pad.ChangeAmiibo(index);
    }

    private sealed class RunCapture(HostRunScope scope, ICaptureSource capture) : ICaptureSource
    {
        public long? FrameIndex { get { scope.RequireCapture(); return capture.FrameIndex; } }
        public string? CaptureFrame(int x, int y, int width, int height)
        {
            scope.RequireCapture();
            scope.Token.ThrowIfCancellationRequested();
            return capture.CaptureFrame(x, y, width, height);
        }
    }
}