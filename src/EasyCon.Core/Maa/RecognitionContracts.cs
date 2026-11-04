#nullable enable
namespace EasyCon.Core.Maa;

/// <summary>P2 接入点。显式图像始终是调用方提供的 PNG；识别实现不得在内部重新截图。</summary>
public sealed record MaaRecognitionRequest(string Type, string Parameters, ReadOnlyMemory<byte> Png,
    string RequestId, string? RunId, string? ResourceGeneration);

public sealed record MaaRecognitionResult(string RequestId, string NativeStatus, string? Detail, string? ErrorCode, bool? Hit = null);

public interface IMaaRecognitionSession : IAsyncDisposable
{
    Task<MaaRecognitionResult> RecognizeAsync(MaaRecognitionRequest request, CancellationToken token);
    Task StopAsync();
}