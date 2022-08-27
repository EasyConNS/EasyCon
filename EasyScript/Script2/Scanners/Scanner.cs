using System.Globalization;
using System.Text;

namespace Compiler.Scanners;

public class Scanner
{
    private Lexicon Lexicon { get;init; }
    private readonly ICollection<UnicodeCategory> lettersCategories;
    private List<int> m_skipIndexes = new();
    
    private readonly StringBuilder output = new();

    internal Scanner(Lexicon lexicon, ICollection<UnicodeCategory> lettersCategories)
    {
        Lexicon = lexicon;
        lettersCategories = lettersCategories;
        EndOfStreamTokenIndex = lexicon.TokenCount;
    }

    public int EndOfStreamTokenIndex { get; init; }

    public void SetSkipTokens(params int[] ignoreIndexes)
    {
        for (int i = 0; i < ignoreIndexes.Length; i++)
        {
            m_skipIndexes.Add(ignoreIndexes[i]);
        }
    }

    public IEnumerable<Lexeme> Parse(string text)
    {
        var linebreaker = (from lxm in Lexicon.GetTokens()
        where lxm.Tag.Description == "LINE_BREAK"
        select lxm).First();

        if(linebreaker == null)
        {
            throw new ArgumentException("LINE_BREAK NOT FOUND");
        }

        int row = 0;
        foreach(var line in linebreaker.Split(text))
        {
            System.Diagnostics.Debug.WriteLine($"parsing:{line}");
            row += 1;
            int position = 0;
            int col = 0;

            while(true)
            {
                if(position >= line.Length)
                {
                    break;
                }
                output.Clear();
                int m_lastState = -1;
                System.Diagnostics.Debug.WriteLine($"matching:{line[position..]}");

                foreach (var rule in Lexicon.GetTokens())
                {
                    var m = rule.Match(line[position..]);
                    if (m.Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"matched rule:{rule.Tag}");
                        
                        if(m.Value.Length > output.Length)
                        {
                            output.Clear();
                            output.Append(m.Value);
                            m_lastState = rule.Tag.Index;
                        }
                    }
                }
                if (m_lastState != -1)
                {
                    var s = output.ToString();
                    System.Diagnostics.Debug.WriteLine($"token:{s}");
                    if(!m_skipIndexes.Contains(m_lastState))
                        yield return new Lexeme(this, m_lastState, new SourceSpan(position, col, row), s);
                    position += s.Length;
                    col += s.EnumerateRunes().Count();
                }
                else if (!lettersCategories.Contains(char.GetUnicodeCategory(line.AsSpan()[position])))
                {
                    throw new ArgumentException($"error char `{line.AsSpan()[position]}` in line:{row},col:{col}");
                }
                else
                {
                    break;
                }
            }
            yield return new Lexeme(this, linebreaker.Tag.Index, new SourceSpan(-1, -1, row), "");
        }
        yield return new Lexeme(this, EndOfStreamTokenIndex, new SourceSpan(-1, -1, row), "");
    }
}