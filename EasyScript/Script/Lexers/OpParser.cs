using EasyScript.Parsing.Statements;

namespace EasyScript.Parsing.Lexers;

internal class OpParser : IStatementParser
{
    static IEnumerable<IStatementParser> AsmParser()
    {
        var types = from domainAssembly in AppDomain.CurrentDomain.GetAssemblies()
                     from assemblyType in domainAssembly.GetTypes()
                     where assemblyType.IsSubclassOf(typeof(BinaryOp)) |
                        assemblyType.IsSubclassOf(typeof(UnaryOp)) |
                        assemblyType.IsSubclassOf(typeof(UnaryOpEx))
                     where assemblyType.GetField("Parser") != null
                     select assemblyType.GetField("Parser").GetValue(null) as IStatementParser;
        return types;
    }

    Statement? IStatementParser.ParseWildcard(ParserArgument args)
    {
        Statement st = null;
        foreach (var p in AsmParser())
        {
            st = p.ParseWildcard(args);
            if(st != null)return st;
        }
        return st;
    }
}
