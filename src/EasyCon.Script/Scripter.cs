using EasyCon.Script2.Syntax;
using EasyScript.Parsing;
using EasyScript.Statements;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace EasyScript;

public class Scripter
{
    readonly Dictionary<string, ExternalVariable> ExtVars = [];

    ImmutableArray<Statement> _statements = [];

    Dictionary<string, FunctionStmt> _funcTables = [];

    public bool HasKeyAction {
        get
        {
            return _statements.OfType<KeyAction>().Any();
        }
    }

    public void Parse(string code, IEnumerable<ExternalVariable> extVars)
    {
        ExtVars.Clear();
        foreach (var ev in extVars)
            ExtVars[ev.Name] = ev;
        _statements = new Parser(ExtVars).Parse(code).ToImmutableArray();
        _statements.OfType<FunctionStmt>().ToList().ForEach(f => { _funcTables[f.Label] = f; });
    }

    public static IEnumerable<string> GetTokens(string code, string pre)
    {
        var tokens = SyntaxTree.ParseTokens(code);
        return tokens.Select(t => t.Value).Distinct().Where(s => s.StartsWith(pre));
    }

    public void Run(IOutputAdapter output, ICGamePad pad)
    {
        var _processor = new Processor(_funcTables)
        {
            Output = output,
            GamePad = pad,
        };
        while (_processor.PC < _statements.Count())
        {
            var cmd = _statements[_processor.PC];
            _processor.PC++;
            cmd.Exec(_processor);
            Thread.Sleep(1); // opt cpu time
        }
    }

    public string ToCode()
    {
        using (var writer = new StringWriter())
        using(var printer = new IndentedTextWriter(writer, "  "))
        {
            _statements.ToList().ForEach(u => u.WriteTo(printer));
            return writer.ToString();
        }
    }

    public string ToggleComment(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input ?? string.Empty;

        // 找到第一个非空白字符的位置
        int firstNonWhitespaceIndex = -1;
        for (int i = 0; i < input.Length; i++)
        {
            if (!char.IsWhiteSpace(input[i]))
            {
                firstNonWhitespaceIndex = i;
                break;
            }
        }

        // 如果全是空白字符，返回原字符串
        if (firstNonWhitespaceIndex == -1)
            return input;

        // 检查第一个非空白字符是否是 #
        bool startsWithHash = input[firstNonWhitespaceIndex] == '#';

        if (startsWithHash)
        {
            // 删除第一个 # 字符
            return input.Remove(firstNonWhitespaceIndex, 1);
        }
        else
        {
            // 在第一个非空白字符前插入 #
            return input.Insert(firstNonWhitespaceIndex, "#");
        }
    }

    public byte[] Assemble(bool auto = true)
    {
        return new Assembly.Assembler().Assemble(_statements, auto);
    }

    public void Reset()
    {
        _statements = [];
        _funcTables = [];
        ExtVars.Clear();
    }
}
