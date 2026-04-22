namespace EasyCon2.Avalonia.Core.Editor;

public static class ScriptEditorHost
{
    public static ScriptEditorControl CreateControl()
    {
        AvaloniaRuntime.EnsureInitialized();
        EcsHighlightingLoader.RegisterAll();
        var control = new ScriptEditorControl();
        control.SyntaxHighlighting = EcsHighlightingLoader.GetByName("ECScript");
        return control;
    }
}