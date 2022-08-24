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

    public Token DefineToken(string definition, string description)
    {
        var tokenInfo = Lexicon.AddToken(definition, description);
        tokens.Add(tokenInfo.Tag);
        return tokenInfo.Tag;
    }

    public Token DefineToken(string definition)
    {
        var tokenInfo = Lexicon.AddToken(definition, "");
        tokens.Add(tokenInfo.Tag);
        return tokenInfo.Tag;
    }
}
