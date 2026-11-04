#nullable enable
using EasyCon.Capture;
using EasyCon.Core.Capabilities;
using OpenCvSharp;
using System.Text.Json;

namespace EasyCon.Core.Maa;

public sealed record LegacyRecognitionTrace(string Label, string Generation, string Route, double? NativeRaw, double Raw,
    double LegacyFloat, int LegacyInteger, int[] Box, string ImageDigest, long? FrameId, long? SourceEpoch);

/// <summary>旧标签查询的 Maa 入口；一次查询固定一帧，原生和兼容结果只经唯一评分适配器。</summary>
public sealed class MaaVisionService : IVisionService, IDisposable
{
    private readonly LabelGeneration _generation;
    private readonly Func<FrameLease?>? _acquire;
    private readonly CancellationToken _token;
    private readonly MaaRecognitionSession _session;
    private readonly GenerationLease _generationLease;
    private bool _disposed;
    private readonly object _gate = new();
    public LegacyRecognitionTrace? LastTrace { get; private set; }
    public event Action<LegacyRecognitionTrace>? Recognized;
    public MaaRecognitionSession Session => _session;
    public static int ActiveResourceVersions => GenerationLease.ActiveVersions;

    public MaaVisionService(LabelGeneration generation, Func<FrameLease?>? acquire = null, CancellationToken token = default)
    {
        _generation = generation;
        _acquire = acquire;
        _token = token;
        _generationLease = new(generation.Directory);
        try { _session = new(generation.Directory, generation.Labels); }
        catch { _generationLease.Dispose(); throw; }
    }

    public int MatchLabel(string labelName)
    {
        using FrameLease frame = _acquire?.Invoke() ?? throw new InvalidOperationException("没有可用的实时帧");
        return MatchImage(labelName, frame.EncodedPng, frame.Metadata.FrameId, frame.Metadata.SourceEpoch).LegacyInteger;
    }

    public LegacyRecognitionTrace MatchImage(string name, ReadOnlyMemory<byte> png, long? frameId = null, long? sourceEpoch = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _token.ThrowIfCancellationRequested();
            _generation.Preflight([name]);
            MaaLabelEntry entry = _generation.Labels[name];
            using Mat image = Cv2.ImDecode(png.ToArray(), ImreadModes.Color);
            if (image.Size() != new Size(FrameNormalization.Width, FrameNormalization.Height))
                throw new InvalidDataException("实时标签识别需要规范的 1920×1080 图像");
            RawMatchResult? native = null;
            if (entry.RoutingPolicy == "native-with-integer-verification")
            {
                string detail = Query(entry.NativeType!, entry.NativeParameters!, png);
                using JsonDocument document = JsonDocument.Parse(detail);
                if (document.RootElement.TryGetProperty("best", out JsonElement best) && best.ValueKind == JsonValueKind.Object)
                {
                    int[] box = best.GetProperty("box").EnumerateArray().Select(value => value.GetInt32()).ToArray();
                    native = new(best.GetProperty("score").GetDouble(), new Rect(box[0], box[1], box[2], box[3]), entry.Method, "maa/5.14.3");
                    _ = LegacyAlgorithmAdapter.ToLegacyFloat(native);
                }
            }
            // 无跨平台数值误差上界，必须同帧确认整数和最佳框；不以几个样本的误差代替保证。
            using JsonDocument compatibility = JsonDocument.Parse(Query(entry.CompatType, entry.CompatParameters, png));
            JsonElement rawElement = FindRaw(compatibility.RootElement);
            int[] compatibleBox = rawElement.GetProperty("box").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            RawMatchResult legacy = new(rawElement.GetProperty("raw").GetDouble(),
                new Rect(compatibleBox[0], compatibleBox[1], compatibleBox[2], compatibleBox[3]), entry.Method);
            int integer = LegacyAlgorithmAdapter.ToLegacyInteger(legacy, _generation.Profile);
            string route = entry.RoutingPolicy;
            if (entry.RoutingPolicy == "native-with-integer-verification")
                route = native == null ? "native-no-candidate" :
                    LegacyAlgorithmAdapter.ToLegacyInteger(native, _generation.Profile) != integer ? "integer-boundary" :
                    native.Box != legacy.Box ? "best-box-semantics" : "native-verified";
            LegacyRecognitionTrace trace = new(name, _generation.Id, route, native?.Score, legacy.Score,
                LegacyAlgorithmAdapter.ToLegacyFloat(legacy), integer, compatibleBox,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png.Span)), frameId, sourceEpoch);
            LastTrace = trace;
            Recognized?.Invoke(trace);
            return trace;
        }
    }

    private string Query(string type, string parameters, ReadOnlyMemory<byte> png)
    {
        MaaRecognitionResult result = _session.RecognizeAsync(new(type, parameters, png, Guid.NewGuid().ToString("N"), null, _generation.Id), _token).GetAwaiter().GetResult();
        return result.Detail ?? throw new InvalidOperationException("Maa 没有返回识别详情");
    }

    private static JsonElement FindRaw(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("raw", out _)) return element.Clone();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                JsonElement found = FindRaw(property.Value);
                if (found.ValueKind != JsonValueKind.Undefined) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                JsonElement found = FindRaw(item);
                if (found.ValueKind != JsonValueKind.Undefined) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.String && element.GetString() is { } text && text.StartsWith('{'))
        {
            using JsonDocument nested = JsonDocument.Parse(text);
            return FindRaw(nested.RootElement);
        }
        return default;
    }

    public string? Crop(string imageBase64, int x, int y, int width, int height) => MatExtensions.CropBase64(imageBase64, x, y, width, height);
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _session.Dispose(); }
            finally { _generationLease.Dispose(); }
        }
    }
}