using EasyScript.Parsing.Lexers;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;

namespace EasyScript.Parsing;

interface IStatementParser
{
    Statement? ParseWildcard(ParserArgument args);

}

static class ParserManager
{
    static List<IStatementParser> WildcardParsers = new();
    static ParserManager()
    {
        var types = (from domainAssembly in AppDomain.CurrentDomain.GetAssemblies()
                     from assemblyType in domainAssembly.GetTypes()
                     where assemblyType.IsTypePlugin(typeof(IStatementParser))
                     select assemblyType).ToArray();
        foreach (var t in types)
        {
            IStatementParser? activate;
            try { activate = (IStatementParser?)Activator.CreateInstance(t); }
            catch (Exception) { continue; }
            if (activate != null)
                WildcardParsers.Add(activate);
        }
    }
    public static Statement? Parse(ParserArgument args)
    {
        return KeywordLexer.
            Parse(args) ??
            WildcardParsers.Select(parser =>
                parser.ParseWildcard(args))
                .FirstOrDefault(statement => statement != null);
    }
}

record ParserArgument
{
    public string Text { get; init; } = string.Empty;
    public Formatter Formatter { get; init; }
    public string Comment { get; init; } = string.Empty;

    
    public ImmutableList<string> Arguments;

    public int Address;
}
