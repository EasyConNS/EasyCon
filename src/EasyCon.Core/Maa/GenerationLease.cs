#nullable enable
using System.Text.Json;

namespace EasyCon.Core.Maa;

/// <summary>派生目录的活跃引用；旧运行退出前不删除其版本。只清理有转换器标记的缓存。</summary>
internal sealed class GenerationLease : IDisposable
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> References = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string _path;
    private bool _disposed;
    public static int ActiveVersions { get { lock (Gate) return References.Count; } }

    public GenerationLease(string path)
    {
        _path = Path.GetFullPath(path);
        lock (Gate) References[_path] = References.GetValueOrDefault(_path) + 1;
    }

    public static void Prune(string root, string current, int retain)
    {
        lock (Gate)
        {
            int kept = 0;
            foreach (string path in Directory.GetDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc))
            {
                string name = Path.GetFileName(path);
                if (name.Length != 64 || !name.All(char.IsAsciiHexDigit) || name == current || References.ContainsKey(Path.GetFullPath(path))) continue;
                string manifest = Path.Combine(path, "easycon", "manifest.json");
                if (!File.Exists(manifest)) continue;
                try
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
                    if (!document.RootElement.TryGetProperty("converter", out JsonElement converter) || converter.GetString() != MaaLabelConverter.Version) continue;
                    if (++kept < retain) continue;
                    Directory.Delete(path, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (JsonException) { }
            }
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (References[_path] == 1) References.Remove(_path);
            else References[_path]--;
        }
    }
}