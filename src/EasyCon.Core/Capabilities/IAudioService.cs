namespace EasyCon.Core.Capabilities;

/// <summary>Factory for isolated script runs. Opening a run must subscribe before any script operation.</summary>
public interface IAudioService
{
    IAudioSession BeginRun(CancellationToken token);
}

/// <summary>Owned by the execution bridge, never by the shared capture device.</summary>
public interface IAudioSession : IDisposable
{
    double Match(string labelName, int lookbackMs, CancellationToken token);
    bool Wait(string labelName, int timeoutMs, double threshold, CancellationToken token);
}