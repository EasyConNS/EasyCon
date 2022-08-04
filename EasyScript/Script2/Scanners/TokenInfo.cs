using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Compiler.Scanners;

[DebuggerDisplay("Index: {Tag.Index}  {Tag.ToString()}")]
public record TokenInfo
{
    public Token Tag { get; private set; }
    public Regex Definition{ get; private set; }

    public TokenInfo(Regex definition, Token tag)
    {
        Definition = definition;
        Tag = tag;
    }

    public bool IsMatch(string input)
    {
        return Definition.IsMatch(input);
    }

    public Match Match(string input, int beginning, int length)
    {
        return Definition.Match(input, beginning, length);
    }   
    public Match Match(string input, int startat)
    {
        return Definition.Match(input, startat);
    }
    public Match Match(string input)
    {
        return Definition.Match(input);
    }
}
