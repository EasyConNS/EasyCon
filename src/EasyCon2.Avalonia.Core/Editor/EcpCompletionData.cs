using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace EasyCon2.Avalonia.Core.Editor;

internal class EcpCompletionData : ICompletionData
{
    public EcpCompletionData(string text) { Text = text; }

    public IImage Image => null;

    public string Text { get; }

    public object Content => Text;

    public object Description => $"描述：{Text}";

    public double Priority => 0.9;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}