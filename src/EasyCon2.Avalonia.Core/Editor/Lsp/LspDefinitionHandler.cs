using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace EasyCon2.Avalonia.Core.Editor.Lsp;

public class LspDefinitionHandler : IDisposable
{
    private readonly TextEditor _editor;
    private readonly LspClientService _lspService;
    private bool _disposed;

    public LspDefinitionHandler(TextEditor editor, LspClientService lspService)
    {
        _editor = editor;
        _lspService = lspService;

        _editor.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed,
            RoutingStrategies.Tunnel);
        _editor.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved,
            RoutingStrategies.Tunnel);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && _lspService.IsConnected)
            _editor.Cursor = new Cursor(StandardCursorType.Hand);
        else if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            _editor.Cursor = null;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = OnPointerPressedAsync(sender, e);
    }

    private async Task OnPointerPressedAsync(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || !_lspService.IsConnected ||
            _lspService.DocumentManager.DocumentUri == null)
            return;

        var pos = _editor.TextArea.TextView.GetPosition(
            e.GetPosition(_editor.TextArea.TextView));
        if (pos == null) return;

        int line = pos.Value.Line;
        int col = pos.Value.Column;

        var result = await _lspService.RequestDefinitionAsync(new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = _lspService.DocumentManager.DocumentUri
            },
            Position = new Position(line - 1, col - 1)
        });

        if (result == null) return;

        Location? targetLocation = null;

        foreach (var item in result)
        {
            if (item.IsLocation)
            {
                targetLocation = item.Location!;
                break;
            }
            if (item.IsLocationLink)
            {
                var link = item.LocationLink!;
                targetLocation = new Location
                {
                    Uri = link.TargetUri,
                    Range = link.TargetSelectionRange
                };
                break;
            }
        }

        if (targetLocation?.Range == null) return;

        int targetLine = (int)targetLocation.Range.Start.Line;
        int targetCol = (int)targetLocation.Range.Start.Character;

        var doc = _editor.Document;
        if (targetLine + 1 > doc.LineCount) return;

        var targetDocLine = doc.GetLineByNumber(targetLine + 1);
        int offset = Math.Min(targetDocLine.Offset + targetCol, targetDocLine.EndOffset);
        offset = Math.Max(offset, targetDocLine.Offset);

        _editor.CaretOffset = offset;
        _editor.ScrollToLine(targetLine + 1);
        _editor.TextArea.Caret.BringCaretToView();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _editor.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _editor.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _editor.Cursor = null;
    }
}