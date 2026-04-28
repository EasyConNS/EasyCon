using EasyCon2.Avalonia.Core.Editor.Lsp;

namespace EasyCon2.Avalonia.Core.Editor;

public static class ScriptEditorHost
{
    public static ScriptEditorControl CreateControl()
    {
        AvaloniaRuntime.EnsureInitialized();
        EcsHighlightingLoader.RegisterAll();

        var lspService = new LspClientService();
        var control = new ScriptEditorControl();
        control.SyntaxHighlighting = EcsHighlightingLoader.GetByName("ECScript");
        control.AttachLsp(lspService);

        _ = lspService.InitializeAsync("untitled.ecs");

        return control;
    }
}
