using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using System.Reflection;
using System.Xml;

namespace EasyCon2.Avalonia.Core.Editor;

public static class EcsHighlightingLoader
{
    private static bool _registered;

    public static void RegisterAll()
    {
        if (_registered) return;

        var asm = Assembly.GetExecutingAssembly();

        RegisterFromResource(asm, "EasyCon2.Avalonia.Core.Resources.Scripts.ecp.xshd",
            "ECScript", [".txt", ".ecs"]);
        RegisterFromResource(asm, "EasyCon2.Avalonia.Core.Resources.Scripts.lua.xshd",
            "Lua", [".lua"]);
        RegisterFromResource(asm, "EasyCon2.Avalonia.Core.Resources.Scripts.Python-Mode.xshd",
            "Python", [".py"]);
        RegisterFromResource(asm, "EasyCon2.Avalonia.Core.Resources.Scripts.ecp-dark.xshd",
            "ECScript-Dark", []);

        _registered = true;
    }

    public static IHighlightingDefinition? GetByExtension(string extension)
    {
        RegisterAll();
        return HighlightingManager.Instance.GetDefinitionByExtension(extension);
    }

    public static IHighlightingDefinition? GetByName(string name)
    {
        RegisterAll();
        return HighlightingManager.Instance.GetDefinition(name);
    }

    private static void RegisterFromResource(Assembly asm, string resourceName,
        string name, string[] extensions)
    {
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null) return;
        using var reader = XmlReader.Create(stream);
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        HighlightingManager.Instance.RegisterHighlighting(name, extensions, definition);
    }
}