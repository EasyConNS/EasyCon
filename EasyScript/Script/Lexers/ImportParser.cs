using EasyScript.Parsing.Statements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyScript.Parsing.Lexers;

internal class ImportParser : IStatementParser
{
    Statement? IStatementParser.Parse(ParserArgument args)
    {
        return ImportParse(args);
    }

    private static Statement ImportParse(ParserArgument args)
    {
        if (args.Text.Equals("import", StringComparison.OrdinalIgnoreCase))
        {
            return new Empty(args.Text);
        }

        var match = Regex.Match(args.Text, @"import\s+<([^<>]+)>", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string importPath = match.Groups[1].Value;
            if (!importPath.EndsWith(".txt"))
            {
                importPath += ".txt";
            }
            if (!File.Exists(importPath))
            {
                importPath = Path.Combine("Script", importPath);
            }
            if (File.Exists(importPath))
            {
                string importText = File.ReadAllText(importPath);
                return new Import(match.Groups[1].Value, importText);
            }
            else
            {
                throw new ParseException($"Import file '{importPath}' not found", 0);
            }
            
        }
        return null;
    }
}
