using System.Diagnostics;

namespace Compiler.Scanners;

[DebuggerDisplay("Token:{Tag} {Value.ToString()} {Span}")]
public record Lexeme
{
    private Scanner m_scanner  { get; init; }
    private int m_stateIndex { get; init; }
    public LexemeValue Value { get; init; }

    public Lexeme(Scanner scanner, int state, SourceSpan span, string content)
    {
        m_scanner = scanner;
        m_stateIndex = state;
        Value = new LexemeValue(content, span);
    }

    public SourceSpan Span => Value.Span;

    public bool IsEndOfStream => m_stateIndex == m_scanner.EndOfStreamTokenIndex;
}