#nullable enable
using EasyCon.Capture;
using OpenCvSharp;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Core.Config;

public sealed record ControllerProfile(string? Endpoint = null, string Transport = "Serial");
public sealed record CaptureProfile(CaptureSourceDescriptor? Source = null, int? Index = null,
    string Backend = "ANY", FrameNormalization? Normalization = null, string? DisplayKey = null);

/// <summary>GUI/CLI 共用的独立选择。覆盖一侧不会重新构造另一侧配置。</summary>
public sealed record HostProfiles
{
    [JsonPropertyName("controller_profile")]
    public ControllerProfile? Controller { get; init; }
    [JsonPropertyName("capture_profile")]
    public CaptureProfile? Capture { get; init; }

    public static HostProfiles Load(string path) => JsonSerializer.Deserialize<HostProfiles>(File.ReadAllText(path),
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } })
        ?? throw new InvalidDataException("设备配置为空");

    public HostProfiles WithOverrides(string? port = null, int? captureIndex = null, string? backend = null)
        => this with
        {
            Controller = port == null ? Controller : (Controller ?? new()) with { Endpoint = port },
            Capture = captureIndex == null && backend == null ? Capture : (Capture ?? new()) with
            {
                Source = captureIndex != null || backend != null ? null : Capture?.Source,
                Index = captureIndex ?? (backend != null && backend != Capture?.Backend ? null : Capture?.Index ?? Capture?.Source?.Index),
                Backend = backend ?? Capture?.Backend ?? "ANY",
            },
        };

    public VideoCaptureAPIs CaptureBackend() => Enum.TryParse(Capture?.Backend, true, out VideoCaptureAPIs backend)
        ? backend : throw new InvalidDataException($"未知采集后端 {Capture?.Backend}");
}