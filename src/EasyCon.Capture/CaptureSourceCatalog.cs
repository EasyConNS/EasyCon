#nullable enable
using EasyCon.Capture.mac;
using EasyCon.Capture.win;
using OpenCvSharp;
using System.Security.Cryptography;
using System.Text;

namespace EasyCon.Capture;

public sealed record CaptureSourceDescriptor(string Id, string Name, int Index, VideoCaptureAPIs Backend,
    bool Persistent, string EnumerationRevision)
{
    public string DisplayKey => $"{Name} [{Backend}/{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Id)))[..8]}]";
}

/// <summary>来源与后端一起枚举。不能确认对应关系的后端明确失败，不套用其他后端的索引。</summary>
public static class CaptureSourceCatalog
{
    private static readonly string EnumerationSession = Guid.NewGuid().ToString("N");

    public static IReadOnlyList<string> GetBackendOptions()
        => OperatingSystem.IsMacOS() ? ["ANY", "AVFOUNDATION"]
            : OperatingSystem.IsLinux() ? ["ANY", "V4L2"]
            : ["ANY", "DSHOW", "MSMF", "DC1394"];

    public static string NormalizeBackendName(string? name)
        => GetBackendOptions().FirstOrDefault(option => string.Equals(option, name, StringComparison.OrdinalIgnoreCase)) ?? "ANY";

    public static VideoCaptureAPIs ResolveBackend(VideoCaptureAPIs requested)
        => requested != VideoCaptureAPIs.ANY ? requested
            : OperatingSystem.IsWindows() ? VideoCaptureAPIs.DSHOW
            : OperatingSystem.IsLinux() ? VideoCaptureAPIs.V4L2 : VideoCaptureAPIs.AVFOUNDATION;

    public static IReadOnlyList<CaptureSourceDescriptor> Enumerate(VideoCaptureAPIs backend)
    {
        backend = ResolveBackend(backend);
        if (OperatingSystem.IsWindows() && backend == VideoCaptureAPIs.DSHOW)
        {
            IReadOnlyList<(string Name, string? DevicePath)> devices = DirectShow.GetDevices();
            return Describe(devices.Select((d, index) => (d.Name, d.DevicePath, index)).ToArray(), backend);
        }
        if (OperatingSystem.IsMacOS() && backend == VideoCaptureAPIs.AVFOUNDATION)
        {
            return Describe(AvFoundationDevices.Enumerate(), backend);
        }
        if (OperatingSystem.IsLinux() && backend == VideoCaptureAPIs.V4L2)
        {
            List<(string, string?, int)> devices = new();
            if (!Directory.Exists("/sys/class/video4linux")) return [];
            foreach (string path in Directory.GetDirectories("/sys/class/video4linux").Order(StringComparer.Ordinal))
            {
                string file = Path.GetFileName(path);
                if (!int.TryParse(file.AsSpan(5), out int index)) continue;
                string name = File.ReadAllText(Path.Combine(path, "name")).Trim();
                string? stable = Directory.Exists("/dev/v4l/by-id")
                    ? Directory.EnumerateFiles("/dev/v4l/by-id").FirstOrDefault(p =>
                        new FileInfo(p).ResolveLinkTarget(true)?.FullName == $"/dev/video{index}") : null;
                devices.Add((name, stable, index));
            }
            return Describe(devices, backend);
        }
        throw new NotSupportedException($"{backend} 尚无可验证的来源枚举；请使用支持的后端，或 CLI 显式指定该后端的临时设备序号");
    }

    internal static IReadOnlyList<CaptureSourceDescriptor> Describe(
        IReadOnlyList<(string Name, string? StableId, int Index)> devices, VideoCaptureAPIs backend)
    {
        string revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', devices.Select(d => $"{d.Index}:{d.Name}:{d.StableId}")))));
        return devices.Select(d => new CaptureSourceDescriptor(
            string.IsNullOrEmpty(d.StableId) ? $"temporary:{EnumerationSession}:{backend}:{revision}:{d.Index}" : $"{backend}:{d.StableId}",
            d.Name, d.Index, backend, !string.IsNullOrEmpty(d.StableId), revision)).ToArray();
    }

    public static CaptureSourceDescriptor Resolve(CaptureSourceDescriptor selection,
        IReadOnlyList<CaptureSourceDescriptor> current)
    {
        CaptureSourceDescriptor[] matches = current.Where(d => d.Id == selection.Id && d.Backend == selection.Backend).ToArray();
        if (matches.Length != 1 || (!selection.Persistent && matches[0].EnumerationRevision != selection.EnumerationRevision))
            throw new InvalidOperationException("所选视频来源已消失或身份不再确定，请重新选择；不会回退其他来源");
        return matches[0];
    }
}