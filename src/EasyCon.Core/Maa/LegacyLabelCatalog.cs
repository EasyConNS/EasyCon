#nullable enable
using EasyCon.Capture;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EasyCon.Core.Maa;

public sealed record LabelDiagnostic(string Severity, string Code, string Source, string? Label, string Message);
public sealed record LegacyLabelSource(string Name, string Path, string Content, string Digest, int Priority)
{
    public ImgLabel Read()
    {
        ImgLabel label = JsonSerializer.Deserialize<ImgLabel>(Content) ?? throw new InvalidDataException("标签内容为空");
        label.name = Name;
        label.path = System.IO.Path.GetDirectoryName(Path) ?? "";
        return label;
    }
}

public sealed record LabelDiscovery(IReadOnlyList<LegacyLabelSource> Selected, IReadOnlyList<LabelDiagnostic> Diagnostics, int TotalFiles);

public static class LegacyLabelCatalog
{
    public static LabelDiscovery FromParents(params string[] parents)
        => Scan(parents.Select(parent => Path.Combine(parent, ECCore.ImgDir)));

    /// <summary>直接接受 ImgLabel 目录。按有序来源，第一个成功解析的同名标签胜出。</summary>
    public static LabelDiscovery Scan(IEnumerable<string> directories, bool recursive = false)
    {
        Dictionary<string, LegacyLabelSource> selected = new(StringComparer.Ordinal);
        List<LabelDiagnostic> diagnostics = [];
        int priority = 0, total = 0;
        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory)) { priority++; continue; }
            foreach (string file in Directory.GetFiles(directory, "*.IL", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                total++;
                string name = Path.GetFileNameWithoutExtension(file);
                try
                {
                    byte[] bytes = File.ReadAllBytes(file);
                    string content = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
                    LegacyLabelSource source = new(name, Path.GetFullPath(file), content,
                        Convert.ToHexStringLower(SHA256.HashData(bytes)), priority);
                    _ = source.Read();
                    if (!selected.TryAdd(name, source))
                        diagnostics.Add(new("warning", "shadowed", file, name, $"同名标签由更早来源胜出: {selected[name].Path}"));
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
                {
                    diagnostics.Add(new("error", "parse-error", file, name, ex.Message));
                }
            }
            priority++;
        }
        return new(selected.Values.ToArray(), diagnostics, total);
    }
}