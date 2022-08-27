using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Compiler.Scanners;

public class Lexicon
{
    private static readonly ICollection<UnicodeCategory> lettersCategories = new HashSet<UnicodeCategory>()
    {
        UnicodeCategory.LetterNumber,
        UnicodeCategory.LowercaseLetter,
        UnicodeCategory.ModifierLetter,
        UnicodeCategory.OtherLetter,
        UnicodeCategory.TitlecaseLetter,
        UnicodeCategory.UppercaseLetter
    };

    private readonly Lexer m_defaultState;
    private readonly List<TokenInfo> m_tokenList = new();

    public Lexicon()
    {
        m_defaultState = new Lexer(this, 0);
    }

    public Lexer Lexer => m_defaultState;
    public int TokenCount => m_tokenList.Count;

    internal TokenInfo AddToken(string definition, string description)
    {
        description = description == string.Empty ? definition : description;
        var contains = m_tokenList.Exists(r => r.Tag.Description == description);
        if (contains) throw new ArgumentException("duplicate token defined!");
        var reg = new Regex("^" + definition, RegexOptions.IgnoreCase);
        var tag = new Token(description, m_tokenList.Count);
        var token = new TokenInfo(reg, tag);
        m_tokenList.Add(token);

        return token;
    }

    public ReadOnlyCollection<TokenInfo> GetTokens() => m_tokenList.AsReadOnly();

    public Scanner CreateScanner()
    {
        return new Scanner(this, lettersCategories);
    }
}