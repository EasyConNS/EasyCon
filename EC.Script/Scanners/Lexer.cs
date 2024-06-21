using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Compiler.Scanners;

public class Lexer
{
    private readonly List<Token> tokens = new();
    public Lexicon Lexicon { get;init; }
    public int Index { get; init; }

    public Lexer(Lexicon lexicon, int index)
    {
        Lexicon = lexicon;
        Index = index;
    }

    public Token DefineToken(RegExpression definition, string description)
    {
        var tokenInfo = Lexicon.AddToken(definition, description);
        tokens.Add(tokenInfo.Tag);
        return tokenInfo.Tag;
    }

    public Token DefineToken(RegExpression definition)
    {
        var tokenInfo = Lexicon.AddToken(definition, "");
        tokens.Add(tokenInfo.Tag);
        return tokenInfo.Tag;
    }
}
