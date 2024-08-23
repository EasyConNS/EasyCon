namespace ECScript.Text;

public sealed class TextLine
{
    public string Text { get; init; } = string.Empty;
    public string Comment { get; init; } = string.Empty;

    public uint Line { get; init; } = 0;
}