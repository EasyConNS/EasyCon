using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Compiler.Scanners;

public class Lexicon
{
    private readonly Lexer m_defaultState;
    private readonly List<TokenInfo> m_tokenList = new();

    public Lexicon()
    {
        m_defaultState = new Lexer(this);
    }

    public Lexer Lexer
    {
        get
        {
            return m_defaultState;
        }
    }

    public int TokenCount => m_tokenList.Count;

    internal TokenInfo AddToken(string definition, string description)
    {
        var contains = m_tokenList.Exists(r => r.Tag.Description == description);
        if (contains) throw new ArgumentException("duplicate token defined!");
        var reg = new Regex("^" + definition, RegexOptions.IgnoreCase);
        var tag = new Token(description, m_tokenList.Count);
        var token = new TokenInfo(reg, tag);
        m_tokenList.Add(token);

        return token;
    }

    public ReadOnlyCollection<TokenInfo> GetTokens()
    {
        return m_tokenList.AsReadOnly();
    }
}