using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Core;

public sealed class Scripter
{
    private IRunner runner = new EasyRunner();

    private Dictionary<string, Func<int>> _extVar = [];

    public bool HasKeyAction => runner.HasKeyAction;

    public Diagnostic[] Parse(string code, string fileName, Dictionary<string, Func<int>> externalGetters)
    {
        _extVar = externalGetters;
        var extVarNames = externalGetters.Select(v => v.Key).ToImmutableHashSet();
        ImmutableArray<Script.Diagnostic> diag = [];
        if (fileName == null)
            diag = runner.Init(code, extVarNames);
        else
            diag = runner.Load(fileName, extVarNames);
        return [.. diag];
    }

    public static IEnumerable<string> GetTokens(string code, string pre)
    {
        var tokens = SyntaxTree.ParseTokens(code);
        return tokens.Select(t => t.Value).Distinct().Where(s => s.StartsWith(pre));
    }

    public void Run(IOutputAdapter output, ICGamePad pad, CancellationToken token)
    {
        runner.Run(output, pad, _extVar, token);
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
            // 在第一个非空白字符前插入 "# " 注释此行
            return input.Insert(firstNonWhitespaceIndex, "# ");
        }
        else
        {
            // 删除第一个 # 字符，以及其后的0~1个空格
            int removeCount = 1;
            if (firstNonWhitespaceIndex + 1 < input.Length && input[firstNonWhitespaceIndex + 1] == ' ')
                removeCount = 2;
            return input.Remove(firstNonWhitespaceIndex, removeCount);
        }
    }
    public byte[] Assemble(bool auto = true)
    {
        return runner.Assemble(auto);
    }

    public void Reset()
    {
        runner = new EasyRunner();
    }
}