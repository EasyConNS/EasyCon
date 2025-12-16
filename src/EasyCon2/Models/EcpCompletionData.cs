using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System.Windows.Media;

namespace EasyCon2.Models;

internal class EcpCompletionData : ICompletionData
{
    public EcpCompletionData(string text) { Text = text; }

    public ImageSource Image => null;

    public string Text { get; private set; }

    public object Content { get { return this.Text; } }

    public object Description { get { return $"描述：{Text}"; } }

    public double Priority => 0.9;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, this.Text);
    }
}
