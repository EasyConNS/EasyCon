#nullable enable
using EasyCon.Capture;
using EasyDevice.Connection;
using OpenCvSharp;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Core.Config;

public sealed record ControllerProfile(string? Endpoint = null, string Transport = "Serial")
{
    public static ControllerProfile FromEndpoint(string? endpoint) => new(endpoint,
        endpoint == null ? nameof(ConnectionTransport.Serial) : ConnectionEndpoint.Parse(endpoint).Transport.ToString());

    public ControllerProfile Validate()
    {
        if (!Enum.GetNames<ConnectionTransport>().Contains(Transport, StringComparer.Ordinal))
            throw new InvalidDataException($"不支持的手柄传输方式 {Transport}");
        if (Endpoint != null)
        {
            ConnectionEndpoint target;
            try { target = ConnectionEndpoint.Parse(Endpoint); }
            catch (ArgumentException ex) { throw new InvalidDataException(ex.Message, ex); }
            if (target.Transport.ToString() != Transport)
                throw new InvalidDataException("手柄地址与 profile 传输方式不一致");
        }
        return this;
    }
}
public sealed record CaptureProfile(CaptureSourceDescriptor? Source = null, int? Index = null,
    string Backend = "ANY", FrameNormalization? Normalization = null, string? DisplayKey = null)
{
    public CaptureProfile Validate()
    {
        VideoCaptureAPIs backend = HostProfiles.ParseCaptureBackend(Backend);
        if (Index < 0) throw new InvalidDataException("采集索引不能为负数");
        if (Source is { } source)
        {
            if (string.IsNullOrWhiteSpace(source.Id) || source.Index < 0 ||
                !Enum.IsDefined(source.Backend) || source.Backend == VideoCaptureAPIs.ANY)
                throw new InvalidDataException("采集来源描述无效");
            if (CaptureSourceCatalog.ResolveBackend(backend) != source.Backend)
                throw new InvalidDataException("采集来源的后端与 profile 后端矛盾");
            // 两者都是保存时的枚举快照；连接前通过稳定 ID 重新解析出的新索引允许不同。
            if (Index is { } index && index != source.Index)
                throw new InvalidDataException("采集来源 ID 的缓存索引与 profile 索引矛盾");
        }
        return this;
    }
}

/// <summary>GUI/CLI 共用的独立选择。覆盖一侧不会重新构造另一侧配置。</summary>
public sealed record HostProfiles
{
    [JsonPropertyName("controller_profile")]
    public ControllerProfile? Controller { get; init; }
    [JsonPropertyName("capture_profile")]
    public CaptureProfile? Capture { get; init; }

    public static HostProfiles Load(string path) => JsonSerializer.Deserialize<HostProfiles>(File.ReadAllText(path),
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } })
        ?.Validate() ?? throw new InvalidDataException("设备配置为空");

    public HostProfiles WithOverrides(string? port = null, int? captureIndex = null, string? backend = null)
    {
        Validate();
        if (captureIndex < 0) throw new InvalidDataException("采集索引不能为负数");
        bool backendChanged = backend != null && CaptureSourceCatalog.ResolveBackend(ParseCaptureBackend(backend)) !=
            CaptureSourceCatalog.ResolveBackend(ParseCaptureBackend(Capture?.Backend ?? "ANY"));
        HostProfiles result = this with
        {
            Controller = port == null ? Controller : ControllerProfile.FromEndpoint(port),
            Capture = captureIndex == null && backend == null ? Capture : (Capture ?? new()) with
            {
                Source = captureIndex != null || backendChanged ? null : Capture?.Source,
                Index = captureIndex ?? (backendChanged ? null : Capture?.Index ?? Capture?.Source?.Index),
                Backend = backend ?? Capture?.Backend ?? "ANY",
                DisplayKey = captureIndex != null || backendChanged ? null : Capture?.DisplayKey,
            },
        };
        return result.Validate();
    }

    public HostProfiles Validate()
    {
        Controller?.Validate();
        Capture?.Validate();
        return this;
    }

    public static VideoCaptureAPIs ParseCaptureBackend(string? name)
        => name != null && Enum.GetNames<VideoCaptureAPIs>().Contains(name, StringComparer.OrdinalIgnoreCase) &&
            Enum.TryParse(name, true, out VideoCaptureAPIs backend)
                ? backend : throw new InvalidDataException($"未知采集后端 {name}");

    public VideoCaptureAPIs CaptureBackend() => ParseCaptureBackend(Capture?.Backend ?? "ANY");
}