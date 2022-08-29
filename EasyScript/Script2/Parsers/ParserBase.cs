using System.Collections.Generic;
using Compiler.Scanners;

namespace Compiler.Parsers;

public abstract class ParserBase
{
    private bool m_isInitialized = false;
    private Lexicon m_lexicon;
    private List<Token> m_triviaTokens = new();

    public bool IsInitialized => m_isInitialized;

    protected abstract void OnDefineLexer(Lexicon lexicon, ICollection<Token> triviaTokens);

    public void Initialize()
    {
        if (!m_isInitialized)
        {
            OnInitialize();
        }
    }

    private void OnInitialize()
    {
        m_lexicon = new Lexicon();

        OnDefineLexer(m_lexicon, m_triviaTokens);

        m_isInitialized = true;
    }

    public IEnumerable<Lexeme> Parse(string source)
    {
        if (!m_isInitialized)
        {
            OnInitialize();
        }

        var scanner = m_lexicon.CreateScanner();
        scanner.SetSkipTokens(m_triviaTokens.Select(z=> z.Index).ToArray());
        return scanner.Parse(source);
    }
}