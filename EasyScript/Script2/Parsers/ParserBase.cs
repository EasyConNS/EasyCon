using Compiler.Parsers.Generator;
using Compiler.Scanners;

namespace Compiler.Parsers;

public abstract class ParserBase<T>
{
    private bool m_isInitialized = false;

    private CompilationErrorManager m_errorManager;
    private ProductionInfoManager m_productionInfoManager;

    private readonly List<Token> m_ignoreTokens = new();

    protected ParserBase(CompilationErrorManager errorManager)
    {
        CodeContract.RequiresArgumentNotNull(errorManager, "errorManager");

        m_errorManager = errorManager;
    }

    private readonly Lexicon m_lexicon = new();

    protected abstract void OnDefineLexer(Lexicon lexicon, ICollection<Token> triviaTokens);

    protected abstract ProductionBase<T> OnDefineParser();

    private void OnInitialize()
    {
        OnDefineLexer(m_lexicon, m_ignoreTokens);

        var production = OnDefineParser();

        if (production == null)
        {
            throw new InvalidOperationException("Root producton is not defined");
        }
        m_productionInfoManager = new ProductionInfoManager(production);

        OnDefineParserErrors(m_errorManager);

        m_isInitialized = true;
    }

    protected virtual Scanner CreateScanner()
    {
        return m_lexicon.CreateScanner();
    }

    protected virtual void OnDefineParserErrors(CompilationErrorManager errorManager)
    {
        // TODO
        //errorManager.DefineError();
    }

    public IEnumerable<Lexeme> Parse(string source)
    {
        return Parse(source, CancellationToken.None);
    }

    public IEnumerable<Lexeme> Parse(string source, CancellationToken ctoken)
    {
        CodeContract.RequiresArgumentNotNull(source, "source");

        if (!m_isInitialized)
        {
            OnInitialize();
        }

        var scanner = CreateScanner();
        scanner.SetSkipTokens(m_ignoreTokens.Select(t => t.Index).ToArray());

        foreach(var p in  m_productionInfoManager.Productions)
        {
            // TODO
            if (!p.IsTerminal)
                Console.WriteLine($"{p}");
        }

        foreach(var t in scanner.Parse(source))
        {
            yield return t;
        }
    }
}