using Compiler.Parsers.Generator;
using Compiler.Scanners;

namespace Compiler.Parsers;

public abstract class ParserBase<T>
{
    private bool m_isInitialized = false;

    private ProductionInfoManager m_productionInfoManager;

    private Lexicon m_lexicon;

    private void OnInitialize()
    {
        m_lexicon = new();

        OnDefineLexer(m_lexicon);

        var production = OnDefineParser();

        if (production == null)
        {
            throw new InvalidOperationException("Root producton is not defined");
        }
        m_productionInfoManager = new ProductionInfoManager(production);

        m_isInitialized = true;
    }

    protected abstract void OnDefineLexer(Lexicon lexicon);

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
        foreach(var p in  m_productionInfoManager.Productions)
        {
            // TODO
            //var result = p.Accept(null, t);
            Console.WriteLine($"{p}");
        }

        foreach(var t in m_lexicon.Lexer.Parse(source))
        {
            
            yield return t;
        }
    }
}