using System.Text;
using System.Text.RegularExpressions;

namespace Compiler.Scanners;

public class Lexer
{
    private List<TokenInfo> rules = new List<TokenInfo>();

    public readonly Token lineToken = new("-NEWLINE-", -1);

    private readonly Regex ignores = new("[\u0020\u0009\u000B\u000C]");// whitespace
    private readonly Regex linebreak = new(@"\r\n"+"|[\u000D\u000A\u0085\u2028\u2029]");
    private readonly StringBuilder output = new();

    public ICollection<TokenInfo> GetTokens()
    {
        return rules;
    }

    public Token DefineToken(string definition, string description)
    {
        var contains = rules.Exists(r=>r.Tag.Description == description);
        if (contains) throw new ArgumentException("duplicate token defined!");
        var reg = new Regex("^" + definition, RegexOptions.IgnoreCase);
        var tag = new Token(description, rules.Count);
        var token = new TokenInfo(reg, tag);
        rules.Add(token);
        return tag;
    }

    public IEnumerable<Lexeme> Parse(string text)
    {
        var lines = linebreak.Split(text);
        int row = 0;
        foreach(var line in lines)
        {
            System.Diagnostics.Debug.WriteLine($"parsing:{line}");
            row += 1;
            int position = 0;
            int col = 0;

            while(true)
            {
                while(position < line.Length && ignores.Match(line[position].ToString()).Success)
                {
                    position += 1;
                    col += 1; 
                }
                if(position >= line.Length)
                {
                    break;
                }
                output.Clear();
                Token? token = null;
                System.Diagnostics.Debug.WriteLine($"matching:{line[position..]}");

                foreach (var rule in rules)
                {
                    var m = rule.Match(line[position..]);
                    if (m.Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"matched rule:{rule.Tag}");
                        
                        if(m.Value.Length > output.Length)
                        {
                            output.Clear();
                            output.Append(m.Value);
                            token = rule.Tag;
                        }
                    }
                }
                if (token != null)
                {
                    var s = output.ToString();
                    System.Diagnostics.Debug.WriteLine($"token:{s}");
                    yield return new Lexeme(s, token, this, position, col, row);
                    position += s.Length;
                    col += s.EnumerateRunes().Count();
                }
                else
                {
                    break;
                }
            }
            yield return new Lexeme("NEW_LINE", lineToken, this, -1, -1, row);
        }  
    }
}
