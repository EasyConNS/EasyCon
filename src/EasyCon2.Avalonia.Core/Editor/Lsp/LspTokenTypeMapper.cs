using Avalonia.Media;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace EasyCon2.Avalonia.Core.Editor.Lsp;

public class LspTokenTypeMapper
{
    private readonly Dictionary<string, (Color Light, Color Dark, IBrush? LightBrush, IBrush? DarkBrush)> _colors;
    private readonly Dictionary<int, string> _reverseIndex = new();
    private bool _isDark;

    public bool IsDark
    {
        get => _isDark;
        set
        {
            if (_isDark == value) return;
            _isDark = value;
            InvalidateBrushes();
        }
    }

    public LspTokenTypeMapper()
    {
        _colors = new Dictionary<string, (Color, Color, IBrush?, IBrush?)>
        {
            ["namespace"] = (Color.FromRgb(0, 0, 128), Color.FromRgb(192, 192, 255), null, null),
            ["type"] = (Color.FromRgb(43, 145, 175), Color.FromRgb(78, 201, 176), null, null),
            ["class"] = (Color.FromRgb(43, 145, 175), Color.FromRgb(78, 201, 176), null, null),
            ["enum"] = (Color.FromRgb(184, 115, 51), Color.FromRgb(255, 198, 109), null, null),
            ["interface"] = (Color.FromRgb(43, 145, 175), Color.FromRgb(78, 201, 176), null, null),
            ["struct"] = (Color.FromRgb(43, 145, 175), Color.FromRgb(78, 201, 176), null, null),
            ["typeParameter"] = (Color.FromRgb(43, 145, 175), Color.FromRgb(78, 201, 176), null, null),
            ["parameter"] = (Color.FromRgb(128, 128, 128), Color.FromRgb(200, 200, 200), null, null),
            ["variable"] = (Color.FromRgb(0, 100, 0), Color.FromRgb(100, 200, 100), null, null),
            ["property"] = (Color.FromRgb(0, 100, 0), Color.FromRgb(100, 200, 100), null, null),
            ["enumMember"] = (Color.FromRgb(0, 120, 120), Color.FromRgb(100, 220, 220), null, null),
            ["function"] = (Color.FromRgb(120, 80, 0), Color.FromRgb(220, 200, 120), null, null),
            ["method"] = (Color.FromRgb(120, 80, 0), Color.FromRgb(220, 200, 120), null, null),
            ["keyword"] = (Color.FromRgb(0, 0, 255), Color.FromRgb(86, 156, 214), null, null),
            ["comment"] = (Color.FromRgb(0, 128, 0), Color.FromRgb(106, 153, 85), null, null),
            ["string"] = (Color.FromRgb(163, 21, 21), Color.FromRgb(214, 157, 133), null, null),
            ["number"] = (Color.FromRgb(128, 0, 128), Color.FromRgb(181, 206, 168), null, null),
            ["operator"] = (Color.FromRgb(100, 100, 100), Color.FromRgb(212, 212, 212), null, null),
            ["decorator"] = (Color.FromRgb(128, 128, 0), Color.FromRgb(220, 220, 100), null, null),
            ["macro"] = (Color.FromRgb(128, 0, 128), Color.FromRgb(197, 134, 192), null, null),
            ["modifier"] = (Color.FromRgb(0, 0, 255), Color.FromRgb(86, 156, 214), null, null),
        };
    }

    public void SetTokenTypes(IEnumerable<SemanticTokenType> types)
    {
        _reverseIndex.Clear();
        int idx = 0;
        foreach (var t in types)
            _reverseIndex[idx++] = t.ToString();
    }

    // Keep string overload for internal use
    public void SetTokenTypes(IEnumerable<string> types)
    {
        _reverseIndex.Clear();
        int idx = 0;
        foreach (var t in types)
            _reverseIndex[idx++] = t;
    }

    public IBrush GetBrush(int tokenType, int _)
    {
        if (!_reverseIndex.TryGetValue(tokenType, out var typeName))
            return _isDark ? DefaultDarkBrush : DefaultLightBrush;

        if (!_colors.TryGetValue(typeName, out var entry))
            return _isDark ? DefaultDarkBrush : DefaultLightBrush;

        if (_isDark)
        {
            entry.DarkBrush ??= new SolidColorBrush(entry.Dark);
            return entry.DarkBrush;
        }
        else
        {
            entry.LightBrush ??= new SolidColorBrush(entry.Light);
            return entry.LightBrush;
        }
    }

    public Color GetColor(int tokenType, int tokenModifiers)
    {
        if (!_reverseIndex.TryGetValue(tokenType, out var typeName))
            return _isDark ? DefaultDarkColor : DefaultLightColor;

        if (_colors.TryGetValue(typeName, out var pair))
            return _isDark ? pair.Dark : pair.Light;

        return _isDark ? DefaultDarkColor : DefaultLightColor;
    }

    private static Color DefaultLightColor => Color.FromRgb(0, 0, 0);
    private static Color DefaultDarkColor => Color.FromRgb(212, 212, 212);

    private static readonly SolidColorBrush DefaultLightBrush = new(DefaultLightColor);
    private static readonly SolidColorBrush DefaultDarkBrush = new(DefaultDarkColor);

    private void InvalidateBrushes()
    {
        foreach (var key in _colors.Keys.ToList())
        {
            var entry = _colors[key];
            _colors[key] = (entry.Light, entry.Dark, null, null);
        }
    }
}