using EasyScript.Parsing.Statements;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing.Lexers;

internal static class ImportParser
{
    public static void Init()
    {
        KeywordLexer.Register("import", ImportParse);
    }

    private static Statement ImportParse(ParserArgument args)
    {

        var match = Regex.Match(args.Text, @"import\s+<([^<>]+)>", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string importPath = match.Groups[1].Value;
            if (!File.Exists(importPath))
            {
                importPath = Path.Combine("Script", importPath);
            }
            if (File.Exists(importPath))
            {
                string importText = File.ReadAllText(importPath, System.Text.Encoding.UTF8);
                return new Import(match.Groups[1].Value, importText);
            }
            else
            {
                throw new ParseException($"未找到文件 '{importPath}'", args.Address);
            }
            
        }
        else
        {
            throw new ParseException("无效的导入", args.Address);
        }
    }
}
