using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EasyCon2.Avalonia.Core.VPad;

internal static class VPadResources
{
    private static readonly Dictionary<string, Bitmap> _cache = [];

    public static IImage LoadImage(string name)
    {
        if (_cache.TryGetValue(name, out var cached))
            return cached;

        var uri = new Uri($"avares://EasyCon2.Avalonia.Core/VPad/Resources/{name}.png");
        var asset = AssetLoader.Open(uri);
        var bitmap = new Bitmap(asset);
        _cache[name] = bitmap;
        return bitmap;
    }
}