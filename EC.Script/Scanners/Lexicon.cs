using System.Collections.ObjectModel;
using System.Globalization;

namespace Compiler.Scanners;

public class Lexicon
{
    private static readonly ICollection<UnicodeCategory> lettersCategories =
    [
        UnicodeCategory.LetterNumber,
        UnicodeCategory.LowercaseLetter,
        UnicodeCategory.ModifierLetter,
        UnicodeCategory.OtherLetter,
        UnicodeCategory.TitlecaseLetter,
        UnicodeCategory.UppercaseLetter
    ];

    private readonly Lexer m_defaultState;
    private readonly List<TokenInfo> m_tokenList = [];

    public Lexicon()
    {
        m_defaultState = new Lexer(this, 0);
    }

    public Lexer Lexer => m_defaultState;
    public int TokenCount => m_tokenList.Count;

    internal TokenInfo AddToken(RegExpression definition, string description)
    {
        description = description == string.Empty ? definition.ToString() : description;
        var contains = m_tokenList.Exists(r => r.Tag.Description == description);
        if (contains) throw new ArgumentException("duplicate token defined!");
        var tag = new Token(description, m_tokenList.Count);
        var token = new TokenInfo(definition, tag);
        m_tokenList.Add(token);

        return token;
    }

    public ReadOnlyCollection<TokenInfo> GetTokens() => m_tokenList.AsReadOnly();

    public Scanner CreateScanner()
    {
        return new Scanner(this, lettersCategories);
    }
}