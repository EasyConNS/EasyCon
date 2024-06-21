namespace Compiler.Scanners;

public record LexemeValue
{
    public LexemeValue(string content, SourceSpan span)
    {
        CodeContract.RequiresArgumentNotNull(span, "span");

        Content = content;
        Span = span;
    }

    public string Content { get; init; }
    public SourceSpan Span { get; init; }

    public override string ToString()
    {
        return Content;
    }
}