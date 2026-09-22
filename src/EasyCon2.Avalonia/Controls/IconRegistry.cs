using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace EasyCon2.Avalonia.Controls;

/// <summary>
/// 单个图标图形 —— 与 Icons.json 中一个 shape 对象一一对应。
/// </summary>
public sealed class IconShape
{
    [JsonPropertyName("d")]
    public string Data { get; init; } = "";

    [JsonPropertyName("fill")]
    public bool Fill { get; init; }

    [JsonPropertyName("stroke")]
    public bool Stroke { get; init; }

    [JsonPropertyName("width")]
    public double Width { get; init; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; init; } = 1;
}

/// <summary>
/// Icons.json 中单个图标的条目 —— 仅包含一组 shapes。
/// </summary>
internal sealed class IconEntry
{
    [JsonPropertyName("shapes")]
    public List<IconShape> Shapes { get; init; } = [];
}

/// <summary>
/// 图标注册表 —— 懒加载 <c>avares://EasyCon2.Avalonia/Resources/Icons.json</c>，
/// 供 <see cref="IconView"/> 按 id 查询图形数据。
/// </summary>
public static class IconRegistry
{
    private const string IconsUri = "avares://EasyCon2.Avalonia/Resources/Icons.json";

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<IconShape>>> Icons = new(
        LoadIcons,
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>强制完成加载 —— 供启动预热调用。</summary>
    public static void Preload()
    {
        _ = Icons.Value;
    }

    /// <summary>是否存在指定 id 的图标。</summary>
    public static bool Exists(string id)
    {
        return !string.IsNullOrEmpty(id) && Icons.Value.ContainsKey(id);
    }

    /// <summary>取指定 id 的图形列表；不存在时返回 null。</summary>
    public static IReadOnlyList<IconShape>? Get(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        return Icons.Value.TryGetValue(id, out IReadOnlyList<IconShape>? shapes) ? shapes : null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<IconShape>> LoadIcons()
    {
        Dictionary<string, IReadOnlyList<IconShape>> result = new(StringComparer.Ordinal);

        using Stream stream = AssetLoader.Open(new Uri(IconsUri));
        Dictionary<string, IconEntry>? raw = JsonSerializer.Deserialize<Dictionary<string, IconEntry>>(stream);
        if (raw == null)
            return result;

        foreach (KeyValuePair<string, IconEntry> pair in raw)
        {
            result[pair.Key] = pair.Value.Shapes;
        }

        return result;
    }
}