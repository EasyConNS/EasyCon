using System.Text.RegularExpressions;

namespace EC.Script.Tests;

[TestFixture]
public class Tests
{
    [SetUp]
    public void Setup()
    {
        string[] pats = { @"[\d\p{L}_]+", @"\s*", @"\d+(\.\d*)?","==", "if", "endif" };
        var p = pats.Select((r,i)=> $"(?<PATTERN{i}>{r})" );
        var pattn = string.Join("|",p);
        var rg= new Regex(pattn);

        Console.WriteLine(string.Join(",", rg.GetGroupNames()));

        var mc = rg.Matches(@"if    我!! ! 3 endif");

        foreach (Match m in mc)
        {
            if (m.Success)
            {
                Group g = null;
                foreach (Group _g in m.Groups)
                {
                    if (_g.Success)
                    {
                        g = _g;
                    }
                }
                Console.WriteLine($"gg{g.Index}, {g.Name}, {g.Value}");
            }
            else
            {
                Console.WriteLine($"mm{m.Index}, {m.Name}, {m.Value}");

            }
            Console.WriteLine(new string('=', 5));
        }
    }

    [Test]
    public void Test1()
    {
        Assert.Pass();
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void IsPrime_ValuesLessThan2_ReturnFalse(int value)
    {

        Assert.That(false, Is.False, $"{value} should not be prime");
    }
}