using EasyCon.Script.Runner;
using EasyCon.Script2.Syntax;
using EasyScript;

namespace EasyCon.Script;

public sealed class Scripter
{
    private IRunner runner = new EasyRunner();

    public bool HasKeyAction => runner.HasKeyAction();

    public void Parse(string code, IEnumerable<ExternalVariable> extVars)
    {
        runner.Init(code, extVars);
    }

    public static IEnumerable<string> GetTokens(string code, string pre)
    {
        var tokens = SyntaxTree.ParseTokens(code);
        return tokens.Select(t => t.Value).Distinct().Where(s => s.StartsWith(pre));
    }

    public void Run(CancellationToken token, IOutputAdapter output, ICGamePad pad)
    {
        runner.Run(output, pad, token);
    }

    public string ToCode()
    {
        return runner.ToCode();
    }

    public static bool CanComment(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

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
            return false;

        // 检查第一个非空白字符是否是 #
        return input[firstNonWhitespaceIndex] != '#';
    }

    public static string ToggleComment(string input, bool comm)
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

        if (comm)
        {
            // 在第一个非空白字符前插入 #注释此行
            return input.Insert(firstNonWhitespaceIndex, "#");
        }
        else
        {
            // 删除第一个 # 字符，取消注释
            return input.Remove(firstNonWhitespaceIndex, 1);
        }
    }

    public byte[] Assemble(bool auto = true)
    {
        //return new Assembly.Assembler().Assemble(_statements, auto);
        return [];
    }

    public void Reset()
    {
        runner = new EasyRunner();
    }
}

public class ScriptException(string message, int address) : Exception(message)
{
    public int Address { get; private set; } = address;
}