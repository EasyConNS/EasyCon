#nullable enable
using EasyCon.Core.Capabilities;
using EasyDevice;
using EasyDevice.Connection;
using EasyScript;

namespace EasyCon.Core.Maa;

public enum ControllerConnectResult { None, Success, InvalidArgument, Timeout, Error, Busy, ReleaseUnconfirmed }
public enum InputCompletion { Accepted, Written, Acknowledged }
public enum CleanupStatus { NotRequired, Written, ReleaseUnconfirmed }

/// <summary>宿主设备端口；协议对象仅存在于 Maa 回调内部。</summary>
public interface IControllerSession : IReporter, IDisposable
{
    bool InputConnected { get; }
    string? InputEndpoint { get; }
    long InputEpoch { get; }
    long ConnectionEpoch { get; }
    bool ControllerConnected { get; }
    bool CanAcceptManualInput { get; }
    bool IsConnected();
    event LogHandler Log;
    event BytesTransferedHandler BytesSent;
    event BytesTransferedHandler BytesReceived;
    event StatusChangedHandler StatusChanged;
    ControllerConnectResult TryConnect(string port);
    void Disconnect();
    ControllerRunLease BeginRun(string runId, CancellationToken token, long? expectedConnectionEpoch = null);
    IPadInput CreatePad(DelayType timing, ControllerRunLease? run = null);
    void Down(ECKey key);
    void Up(ECKey key);
    void LeftDirection(DirectionKey key, bool down);
    void RightDirection(DirectionKey key, bool down);
    void HatDirection(DirectionKey key, bool down);
    void ApplyReport(SwitchReport report);
    void Reset();
    bool Flash(byte[] bytes);
    bool RemoteStart();
    bool RemoteStop();
    int GetVersion();
    bool UnPair();
    bool ChangeAmiiboIndex(byte index);
    bool ChangeControllerMode(byte mode);
    bool ChangeControllerColor(byte[] color);
    bool SaveAmiibo(byte index, byte[] data);
    void StartRecord();
    void StopRecord();
    void PauseRecord();
    string GetRecordScript();
}

public sealed class ControllerOperationException(string errorCode, string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}