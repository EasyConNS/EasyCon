using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Compiler.Scanners;

[DebuggerDisplay("Index: {Tag.Index}  {Tag.ToString()}")]
public record TokenInfo
{
    public Token Tag { get; init; }
    public RegExpression Definition{ get; init; }

    public TokenInfo(RegExpression definition, Token tag)
    {
        Definition = definition;
        Tag = tag;
    }

    public Match Match(string input)
    {
        return Definition.Match(input);
    }

    public string[] Split(string input)
    {
        return Definition.Split(input);
    }
}
