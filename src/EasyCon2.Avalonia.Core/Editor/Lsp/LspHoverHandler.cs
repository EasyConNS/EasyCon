using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace EasyCon2.Avalonia.Core.Editor.Lsp;

public class LspHoverHandler : IDisposable
{
    private readonly TextEditor _editor;
    private readonly LspClientService _lspService;
    private readonly DispatcherTimer _hoverTimer;
    private Point _lastMousePosition;
    private bool _disposed;

    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(500);

    public LspHoverHandler(TextEditor editor, LspClientService lspService)
    {
        _editor = editor;
        _lspService = lspService;

        _hoverTimer = new DispatcherTimer { Interval = HoverDelay };
        _hoverTimer.Tick += OnHoverTimerTick;

        _editor.TextArea.PointerMoved += OnPointerMoved;
        _editor.TextArea.PointerExited += OnPointerExited;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _lastMousePosition = e.GetPosition(_editor.TextArea.TextView);
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _hoverTimer.Stop();
        ToolTip.SetTip(_editor.TextArea, null);
    }

    private async Task OnHoverTimerTick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();

        if (!_lspService.IsConnected || _lspService.DocumentManager.DocumentUri == null)
            return;

        var textViewPos = _editor.TextArea.TextView.GetPosition(_lastMousePosition);
        if (textViewPos == null) return;

        var position = new Position(textViewPos.Value.Line - 1, textViewPos.Value.Column - 1);

        var hover = await _lspService.RequestHoverAsync(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = _lspService.DocumentManager.DocumentUri
            },
            Position = position
        });

        if (hover?.Contents == null) return;

        var text = ExtractText(hover.Contents);
        if (string.IsNullOrWhiteSpace(text)) return;

        var tipContent = new TextBlock { Text = SimplifyMarkdown(text) };
        ToolTip.SetTip(_editor.TextArea, tipContent);
        ToolTip.SetShowDelay(_editor.TextArea, 0);
    }

    private static string ExtractText(MarkedStringsOrMarkupContent contents)
    {
        if (contents.HasMarkupContent && contents.MarkupContent != null)
            return contents.MarkupContent.Value ?? "";
        if (contents.HasMarkedStrings && contents.MarkedStrings != null)
            return string.Join("\n", contents.MarkedStrings.Select(
                ms => ms.Value ?? ms.Language ?? ""));
        return "";
    }

    private static string SimplifyMarkdown(string markdown)
    {
        return markdown
            .Replace("**", "")
            .Replace("`", "")
            .Replace("```", "")
            .Replace("___", "")
            .Trim();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hoverTimer.Stop();
        ToolTip.SetTip(_editor.TextArea, null);

        _editor.TextArea.PointerMoved -= OnPointerMoved;
        _editor.TextArea.PointerExited -= OnPointerExited;
    }
}