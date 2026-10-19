using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace EasyCon.Capture.Audio;

/// <summary>One loaded label: bounded background matching and a cumulative integer 0–100 peak.</summary>
public sealed class AudioRecognition : IDisposable
{
    private readonly AudioMatchMonitor _monitor;
    private readonly CancellationTokenSource _cancellation;
    private readonly Task _worker;
    private ExceptionDispatchInfo? _failure;
    private int _peak;
    private int _disposed;

    public AudioRecognition(IAudioCaptureSource source, AudioMatchTemplate template, CancellationToken token)
        : this(source, template, token, Stopwatch.GetTimestamp)
    {
    }

    internal AudioRecognition(IAudioCaptureSource source, AudioMatchTemplate template, CancellationToken token, Func<long> clock)
    {
        token.ThrowIfCancellationRequested();
        _monitor = new AudioMatchMonitor(source, template, clock, requireContinuousSession: true);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        // Always run finally, including when cancellation precedes task scheduling.
        _worker = Task.Run(RecognizeAsync);
    }

    public int Score
    {
        get
        {
            CheckHealth();
            return Volatile.Read(ref _peak);
        }
    }

    public void CheckHealth()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _cancellation.Token.ThrowIfCancellationRequested();
        Volatile.Read(ref _failure)?.Throw();
        _monitor.CheckHealth();
    }

    private async Task RecognizeAsync()
    {
        CancellationToken token = _cancellation.Token;
        try
        {
            while (true)
            {
                AudioMatchUpdate update = _monitor.Poll(token);
                if (update.Score.HasValue)
                    Volatile.Write(ref _peak, Math.Max(Volatile.Read(ref _peak), update.Score.Value));
                await Task.Delay(10, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Volatile.Write(ref _failure, ExceptionDispatchInfo.Capture(ex));
        }
        finally
        {
            _monitor.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _cancellation.Cancel();
        // Wait for cooperative FFT cancellation before allowing a reload to start a new generation.
        _worker.GetAwaiter().GetResult();
        _cancellation.Dispose();
    }
}