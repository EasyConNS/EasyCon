namespace EasyCon.Core.Capabilities;

/// <summary>Factory for isolated script runs. Only explicitly loaded labels subscribe to audio.</summary>
public interface IAudioService
{
    IAudioSession BeginRun(CancellationToken token);
}

/// <summary>Owned by the execution bridge, never by the shared capture device.</summary>
public interface IAudioSession : IDisposable
{
    void Load(string labelName, CancellationToken token);
    /// <summary>Cumulative highest completed score, 0–100; initially zero. Reading never consumes it.</summary>
    double Score(string labelName, CancellationToken token);
    void Unload(string labelName, CancellationToken token);
    /// <summary>Surface background failures even when a script finishes without reading again.</summary>
    void CheckHealth(CancellationToken token);
}