#nullable enable
using EasyCon.Capture;
using EasyCon.Core.Input;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using System.Text.Json;

namespace EasyCon.Core.Maa;

/// <summary>独立停止域的 Maa 原生句柄。只借用所绑定模块，不连接/销毁另一模块。</summary>
public sealed class MaaControllerBinding : IDisposable
{
    private static int _activeNativeBindings;
    public static int ActiveNativeBindings => Volatile.Read(ref _activeNativeBindings);
    private readonly PadSession? _pad;
    private readonly InputOwner? _owner;
    private readonly IFrameHub? _frames;
    private readonly long _sourceEpoch;
    private readonly object _gate = new();
    private readonly object _lifecycle = new();
    private readonly Queue<string> _recentRequests = new();
    private readonly HashSet<string> _seenRequests = new(StringComparer.Ordinal);
    private readonly string _bindingId = Guid.NewGuid().ToString("N");
    private readonly Lazy<MaaCustomController> _native;
    private MaaResource? _resource;
    private MaaTasker? _tasker;
    private CancellationToken _operationToken;
    private bool _disposed;
    private bool _stopped;
    private CaptureUsageLease? _captureUsage;
    private CancellationTokenRegistration _captureCancellation;

    internal MaaControllerBinding(PadSession? pad, InputOwner? owner, IFrameHub? frames = null,
        string? resourceDirectory = null)
    {
        _pad = pad;
        _owner = owner;
        _frames = frames;
        _sourceEpoch = frames?.SourceEpoch ?? 0;
        if ((pad == null) != (owner == null)) throw new ArgumentException("输入端口和所有者必须同时绑定");
        if (pad == null && frames == null) throw new ArgumentException("无硬件任务不创建控制器绑定");
        _native = new(() =>
        {
            _ = MaaEnvironment.CacheDirectory;
            MaaCustomController controller = new(new Callbacks(this), LinkOption.None, CheckStatusOption.None);
            try
            {
                if (!controller.SetOption(ControllerOption.ScreenshotUseRawSize, true))
                    throw new InvalidOperationException("Maa ScreenshotUseRawSize 设置失败");
                _resource = MaaEnvironment.LoadBootstrap(resourceDirectory);
                _tasker = new MaaTasker
                {
                    Controller = controller,
                    Resource = _resource,
                    DisposeOptions = DisposeOptions.None,
                };
                if (BindingReady && !_tasker.IsInitialized) throw new InvalidOperationException("Maa Tasker 未初始化");
                Interlocked.Increment(ref _activeNativeBindings);
                return controller;
            }
            catch { _tasker?.Dispose(); _resource?.Dispose(); controller.Dispose(); throw; }
        });
    }

    public static MaaControllerBinding CaptureOnly(IFrameHub frames)
    {
        CaptureUsageLease? usage = (frames as FrameHub)?.AcquireUsage();
        try
        {
            MaaControllerBinding binding = new(null, null, frames) { _captureUsage = usage };
            if (usage != null)
                binding._captureCancellation = usage.Token.Register(() => _ = Task.Run(binding.Dispose));
            return binding;
        }
        catch { usage?.Dispose(); throw; }
    }
    internal MaaCustomController Native => _native.Value;
    public bool RequiresInput => _pad != null;
    public bool RequiresCapture => _frames != null;
    internal bool HasNative => _native.IsValueCreated;
    public string LastNativeStatus { get; private set; } = "NotStarted";
    public bool IsInitialized => _native.IsValueCreated && _tasker?.IsInitialized == true;
    public bool BindingReady
    {
        get
        {
            if (_disposed || _stopped) return false;
            if (_pad != null)
            {
                try { _pad.Validate(_owner!, cleanup: true); }
                catch (Exception ex) when (ex is ControllerOperationException or OperationCanceledException or ObjectDisposedException) { return false; }
                if (!_pad.IsConnected) return false;
            }
            return _frames == null || (_frames.CaptureConnected && _frames.SourceEpoch == _sourceEpoch);
        }
    }

    internal ControllerReply Send(ControllerCommand command, CancellationToken token)
    {
        if (_pad == null || _owner == null) throw new ControllerOperationException("UnsupportedInput", "此绑定只提供截图");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pad.Validate(_owner);
            token.ThrowIfCancellationRequested();
            command = command with { RunId = _owner.RunId, Epoch = _owner.Epoch };
            _operationToken = token;
            try
            {
                MaaJobStatus status = Native.Shell(JsonSerializer.Serialize(command, ControllerJson.Options), -1).Wait();
                LastNativeStatus = status.ToString();
                token.ThrowIfCancellationRequested();
                _owner.Token.ThrowIfCancellationRequested();
                if (status != MaaJobStatus.Succeeded || !Native.GetShellOutput(out string? output) || output == null)
                    throw new ControllerOperationException("NativeFailure", "Maa Shell 回调未完成");
                ControllerReply reply = JsonSerializer.Deserialize<ControllerReply>(output, ControllerJson.Options)
                    ?? throw new ControllerOperationException("InvalidReply", "控制器响应为空");
                if (reply.RequestId != command.RequestId) throw new ControllerOperationException("InvalidReply", "requestId 不一致");
                if (!reply.Accepted) throw new ControllerOperationException(reply.ErrorCode ?? "InputFailed", reply.Value ?? "控制器拒绝操作");
                return reply;
            }
            finally { _operationToken = default; }
        }
    }

    /// <summary>不跨队列传 Mat；同一租约的 PNG 编码仅保留在其帧槽内。</summary>
    public byte[] Screenshot()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!BindingReady) throw new ControllerOperationException("BindingNotReady", "截图绑定已失效或来源未连接");
            if (Native.Screencap().Wait() != MaaJobStatus.Succeeded)
                throw new ControllerOperationException("FrameUnavailable", "当前代次尚无有效画面");
            using MaaImageBuffer buffer = new();
            if (!Native.GetCachedImage(buffer) || !buffer.TryGetEncodedData(out byte[]? png) || png == null || png.Length == 0)
                throw new InvalidDataException("Maa 截图返回空图像");
            return png;
        }
    }

    internal bool ReleaseInput()
    {
        if (_pad == null) return true;
        lock (_gate) return Native.Inactive().Wait() == MaaJobStatus.Succeeded;
    }

    public void Stop()
    {
        lock (_lifecycle)
        {
            if (_disposed || _stopped) return;
            if (!_native.IsValueCreated) { _stopped = true; return; }
            // Stop 先通知原生队列，再等已进入的回调退出；不在回调中等待自身。
            _tasker?.Stop().Wait();
            lock (_gate) { }
            _stopped = true;
        }
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            Stop();
            lock (_gate)
            {
                if (_disposed) return;
                if (_native.IsValueCreated)
                {
                    _tasker?.Dispose();
                    _resource?.Dispose();
                    _native.Value.Dispose();
                    Interlocked.Decrement(ref _activeNativeBindings);
                }
                _disposed = true;
                _captureCancellation.Dispose();
                _captureUsage?.Dispose();
            }
        }
    }

    internal bool InactiveForOwner()
    {
        if (_pad == null) return true;
        try { return _pad.Release(_owner!); }
        catch (Exception ex) when (ex is ControllerOperationException or ObjectDisposedException) { return false; }
    }

    internal bool KeyForOwner(int code, bool down)
    {
        try { return _pad?.StandardKey(_owner!, code, down) ?? false; }
        catch (Exception ex) when (ex is ControllerOperationException or OperationCanceledException or ObjectDisposedException) { return false; }
    }

    private sealed class Callbacks(MaaControllerBinding binding) : IMaaCustomController
    {
        public string Name { get; set; } = "EasyCon.Controller";
        public bool Connect() => binding.BindingReady;
        public bool Connected() => binding.BindingReady;
        public ControllerFeatures GetFeatures() => ControllerFeatures.UseKeyboardDownAndUpInsteadOfClick;
        public bool RequestUuid(IMaaStringBuffer buffer) => buffer.TrySetValue("EasyCon.Binding/" + binding._bindingId);
        public bool GetInfo(IMaaStringBuffer buffer) => buffer.TrySetValue(JsonSerializer.Serialize(new
        {
            inputConnected = binding._pad?.IsConnected,
            captureConnected = binding._frames?.CaptureConnected,
            bindingReady = binding.BindingReady,
            frameReady = binding._frames?.FrameReady ?? false,
            binding.RequiresInput,
            binding.RequiresCapture,
            inputEndpoint = binding._pad?.Endpoint,
            inputEpoch = binding._owner?.Epoch,
            sourceEpoch = binding._sourceEpoch,
            sourceId = (binding._frames as FrameHub)?.SourceId,
        }));
        public bool Screencap(IMaaImageBuffer buffer)
        {
            if (binding._frames == null || !binding.BindingReady || !binding._frames.FrameReady) return false;
            using FrameLease? frame = binding._frames.AcquireLatest();
            return frame != null && frame.Metadata.SourceEpoch == binding._sourceEpoch
                && buffer.TrySetEncodedData(frame.EncodedPng.Span);
        }
        public bool Shell(string cmd, long millisecondsTimeout, IMaaStringBuffer buffer)
        {
            string requestId = "";
            ControllerReply reply;
            try
            {
                ControllerCommand command = JsonSerializer.Deserialize<ControllerCommand>(cmd, ControllerJson.Options)
                    ?? throw new ControllerOperationException("InvalidCommand", "空控制器命令");
                requestId = command.RequestId;
                if (command.Protocol != "easycon.controller/1" || string.IsNullOrWhiteSpace(requestId))
                    throw new ControllerOperationException("InvalidProtocol", "无效协议或 requestId");
                if (binding._pad == null) throw new ControllerOperationException("UnsupportedInput", "此绑定只提供截图");
                if (!binding._seenRequests.Add(requestId)) throw new ControllerOperationException("DuplicateRequest", "重复请求不会再次执行");
                binding._recentRequests.Enqueue(requestId);
                if (binding._recentRequests.Count > 1024) binding._seenRequests.Remove(binding._recentRequests.Dequeue());
                reply = binding._pad.Execute(command, binding._owner!, binding._operationToken);
            }
            catch (OperationCanceledException) { reply = new(requestId, false, "Cancelled"); }
            catch (ControllerOperationException ex) { reply = new(requestId, false, ex.ErrorCode, Value: ex.Message); }
            catch (Exception ex) { reply = new(requestId, false, "CallbackFailure", Value: ex.Message); }
            return buffer.TrySetValue(JsonSerializer.Serialize(reply, ControllerJson.Options));
        }
        public bool Inactive() => binding.InactiveForOwner();
        private bool Key(int code, bool down)
        {
            return binding.KeyForOwner(code, down);
        }
        public bool KeyDown(int keycode) => Key(keycode, true);
        public bool KeyUp(int keycode) => Key(keycode, false);
        public bool ClickKey(int keycode) => false;
        public bool TouchDown(int contact, int x, int y, int pressure) => false;
        public bool TouchMove(int contact, int x, int y, int pressure) => false;
        public bool TouchUp(int contact) => false;
        public bool StartApp(string intent) => false;
        public bool StopApp(string intent) => false;
        public bool Click(int x, int y) => false;
        public bool Swipe(int x1, int y1, int x2, int y2, int duration) => false;
        public bool InputText(string text) => false;
        public bool Scroll(int dx, int dy) => false;
        public bool RelativeMove(int dx, int dy) => false;
        public void Dispose() { }
    }
}