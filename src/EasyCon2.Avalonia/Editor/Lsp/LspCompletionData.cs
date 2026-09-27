using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace EasyCon2.Avalonia.Core.Editor.Lsp;

internal class LspCompletionData : ICompletionData
{
    private readonly CompletionItem _item;

    public LspCompletionData(CompletionItem item)
    {
        _item = item;
        Text = item.InsertText ?? item.Label;
    }

    public IImage? Image => null;

    public string Text { get; }

    public object Content => string.IsNullOrEmpty(_item.Detail)
        ? _item.Label
        : $"{_item.Label} ({_item.Detail})";

    public object Description
    {
        get
        {
            var desc = _item.Documentation?.MarkupContent?.Value
                       ?? _item.Documentation?.String
                       ?? _item.Detail
                       ?? "";
            return string.IsNullOrEmpty(desc) ? _item.Label : desc;
        }
    }

    public double Priority
    {
        get
        {
            if (!string.IsNullOrEmpty(_item.SortText))
                return 0.9;
            return 0.5;
        }
    }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}