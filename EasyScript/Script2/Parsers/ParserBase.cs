using Compiler.Scanners;

namespace Compiler.Parsers;

public abstract class ParserBase<T>
{
    private bool m_isInitialized = false;

    private Lexer m_lexer;

    private void OnInitialize()
    {
        m_lexer = new();

        OnDefineLexer(m_lexer);

        var production = OnDefineParser();

        if (production == null)
        {
            throw new InvalidOperationException("Root producton is not defined");
        }

        m_isInitialized = true;
    }

    protected abstract void OnDefineLexer(Lexer lexer);

    protected abstract ProductionBase<T> OnDefineParser();

    public IEnumerable<Lexeme> Parse(string source)
    {
        return Parse(source, CancellationToken.None);
    }

    public IEnumerable<Lexeme> Parse(string source, CancellationToken ctoken)
    {
        if (!m_isInitialized)
        {
            OnInitialize();
        }

        return m_lexer.Parse(source);
    }
}