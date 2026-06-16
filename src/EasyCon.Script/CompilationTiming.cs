using System.Diagnostics;
using System.Text;

namespace EasyCon.Script;

/// <summary>
/// 编译管线各步骤执行时间记录。
/// </summary>
public sealed class CompilationTiming
{
    public TimeSpan FileLoad { get; set; }
    public TimeSpan LexingAndParsing { get; set; }
    public TimeSpan StdLibLoad { get; set; }
    public TimeSpan ImportResolve { get; set; }
    public TimeSpan AutoLoadLib { get; set; }
    public TimeSpan DeclarationCollect { get; set; }
    public TimeSpan Binding { get; set; }
    public TimeSpan SsaBuild { get; set; }
    public TimeSpan SsaOptimize { get; set; }

    /// <summary>各子阶段耗时（仅在 SSA 优化阶段内部记录）</summary>
    public SsaOptimizeTiming SsaOptimizeDetails { get; set; } = new();

    public TimeSpan Total =>
        FileLoad + LexingAndParsing + StdLibLoad + ImportResolve +
        AutoLoadLib + DeclarationCollect + Binding + SsaBuild + SsaOptimize;

    public string ToReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("===== 编译管线耗时分析 =====");
        var total = Total.TotalMilliseconds;
        PrintLine(sb, "文件加载 (File.ReadAllText)", FileLoad, total);
        PrintLine(sb, "词法分析+语法分析 (Lex+Parse)", LexingAndParsing, total);
        PrintLine(sb, "标准库加载 (StdLib)", StdLibLoad, total);
        PrintLine(sb, "Import 解析", ImportResolve, total);
        PrintLine(sb, "lib/ 目录自动加载", AutoLoadLib, total);
        PrintLine(sb, "声明收集 (DeclCollector)", DeclarationCollect, total);
        PrintLine(sb, "语义绑定 (Binding)", Binding, total);
        PrintLine(sb, "SSA 生成 (SsaBuild)", SsaBuild, total);
        PrintLine(sb, "SSA 优化 (SsaOptimize)", SsaOptimize, total);

        // SSA 优化子阶段
        var opt = SsaOptimizeDetails;
        if (opt.RemoveUnreachable1 != TimeSpan.Zero || opt.InlineTrivial != TimeSpan.Zero)
        {
            sb.AppendLine("  ----- SSA 优化子阶段 -----");
            var optTotal = SsaOptimize.TotalMilliseconds;
            PrintLine(sb, "    移除不可达函数 (1)", opt.RemoveUnreachable1, optTotal, indent: true);
            PrintLine(sb, "    内联 trivial 函数", opt.InlineTrivial, optTotal, indent: true);
            PrintLine(sb, "    函数内优化", opt.IntraFunctionOpt, optTotal, indent: true);
            PrintLine(sb, "    内联 intrinsic 包装", opt.InlineIntrinsic, optTotal, indent: true);
            PrintLine(sb, "    移除不可达函数 (2)", opt.RemoveUnreachable2, optTotal, indent: true);
        }

        sb.AppendLine($"  总计: {total:F2} ms");
        sb.AppendLine("============================");
        return sb.ToString();
    }

    private static void PrintLine(StringBuilder sb, string label, TimeSpan elapsed, double totalMs, bool indent = false)
    {
        var ms = elapsed.TotalMilliseconds;
        var pct = totalMs > 0 ? ms / totalMs * 100 : 0;
        var bar = new string('█', (int)(pct / 2));
        var prefix = indent ? "" : "  ";
        sb.AppendLine($"{prefix}{label,-40} {ms,8:F2} ms  ({pct,5:F1}%) {bar}");
    }
}

public sealed class SsaOptimizeTiming
{
    public TimeSpan RemoveUnreachable1 { get; set; }
    public TimeSpan InlineTrivial { get; set; }
    public TimeSpan IntraFunctionOpt { get; set; }
    public TimeSpan InlineIntrinsic { get; set; }
    public TimeSpan RemoveUnreachable2 { get; set; }
}