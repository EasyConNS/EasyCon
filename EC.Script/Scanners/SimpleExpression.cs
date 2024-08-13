using System.Text.RegularExpressions;

namespace Compiler.Scanners;

public class SimpleExpression : RegExpression
{
    public SimpleExpression(string definition)
    {
        m_definition = definition;
        m_regex = new(m_definition);
    }
    private Regex m_regex{ get; init; }
    private string m_definition{ get; init; }

    public override string ToString()
    {
        return m_definition;
    }

    public override Match Match(string input)
    {
        return m_regex.Match(input);
    }

    public override string[] Split(string input)
    {
        return m_regex.Split(input);
    }
}