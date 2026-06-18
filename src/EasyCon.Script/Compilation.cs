using EasyCon.Script.Binding;
using EasyCon.Script.Resolution;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace EasyCon.Script;

public class ScriptException(string message, int address = 0) : Exception(message)
{
    public int Address { get; private set; } = address;
}

/// <summary>
/// 编译结果：包含诊断信息和优化后的 SSA IR。
/// 当存在编译错误时 Program 为 null。
/// </summary>
public sealed class CompileResult
{
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public SsaProgram? Program { get; }
    public bool KeyAction { get; }
    public bool NeedIL { get; }
    public CompilationTiming? Timing { get; }

    internal CompileResult(ImmutableArray<Diagnostic> diagnostics, SsaProgram? program, bool keyAction, bool needIL, CompilationTiming? timing = null)
    {
        Diagnostics = diagnostics;
        Program = program;
        KeyAction = keyAction;
        NeedIL = needIL;
        Timing = timing;
    }
}

public sealed class Compilation
{
    private readonly CompilationTiming _timing;

    private Compilation(ResolutionResult result, CompilationTiming timing)
    {
        Result = result;
        SyntaxTrees = result.Trees;
        _timing = timing;
    }

    /// <summary>Resolution 阶段的完整产出。</summary>
    internal ResolutionResult Result { get; }

    public ImmutableArray<SyntaxTree> SyntaxTrees { get; }

    public static Compilation Create(SyntaxTree mainTree, CompilationTiming? timing = null)
    {
        timing ??= new CompilationTiming();
        var resolver = new Resolver();
        var result = resolver.Resolve(mainTree, timing);
        return new Compilation(result, timing);
    }

    /// <summary>
    /// 完整编译管线：Bind → SSA 生成 → SSA 优化。
    /// 编译错误时返回 Program=null 的 CompileResult。
    /// </summary>
    public CompileResult Compile(ImmutableHashSet<string>? extVars)
    {
        var sw = Stopwatch.StartNew();
        var bound = Binder.BindProgram(Result, extVars);
        _timing.Binding = sw.Elapsed;
        var keyAction = bound.KeyAction;
        // 编译失败时回退到 BoundProgram 的乐观假设；成功路径下用 SSA 调用图分析的精确结果覆盖
        var needIL = bound.NeedIL;

        if (bound.Diagnostics.HasErrors())
            return new CompileResult(bound.Diagnostics, null, keyAction, needIL, _timing);

        // Bound → SSA → Optimize
        sw.Restart();
        var ssaProgram = SsaProgramBuilder.Build(bound);
        _timing.SsaBuild = sw.Elapsed;

        sw.Restart();
        SsaOptimizer.Optimize(ssaProgram, _timing.SsaOptimizeDetails);
        _timing.SsaOptimize = sw.Elapsed;

        // SSA 阶段已经做了调用图可达性分析，这里采用精确结果
        // KeyAction 同样采用 SSA 精确结果（覆盖嵌套按键），而非顶层扫描的 bound.KeyAction
        return new CompileResult(ssaProgram.Diagnostics, ssaProgram, ssaProgram.KeyAction, ssaProgram.NeedIL, _timing);
    }

    public string FormatCode()
    {
        var mainTree = SyntaxTrees.FirstOrDefault(t => !t.IsLib) ?? SyntaxTrees[0];
        using var writer = new StringWriter();
        using var printer = new IndentedTextWriter(writer, "    ");
        foreach (var statement in mainTree.Root.Members)
        {
            statement.WriteTo(printer);
        }
        return writer.ToString().Trim();
    }

    /// <summary>
    /// 编译并以人类可读格式输出 SSA IR。
    /// </summary>
    /// <param name="extVars">外部变量名集合</param>
    /// <param name="beforeOptimize">true 时输出优化前的原始 SSA</param>
    public string DumpIr(ImmutableHashSet<string>? extVars, bool beforeOptimize = false)
    {
        var bound = Binder.BindProgram(Result, extVars);
        if (bound.Diagnostics.HasErrors())
            return string.Join("\n", bound.Diagnostics.Select(d => $"error: {d.Message}"));

        var ssaProgram = SsaProgramBuilder.Build(bound);
        if (!beforeOptimize)
            SsaOptimizer.Optimize(ssaProgram);

        return SsaPrinter.Dump(ssaProgram);
    }

    /// <summary>
    /// 构建 SSA IR 并返回（默认经优化）。<paramref name="optimize"/>=false 时只返回构造阶段
    /// （Braun 算法）产出的原始 IR，跳过优化器——供测试稳定观测构造阶段插入的 phi，
    /// 避免优化器（常量折叠等）把待观测的结构消掉。编译错误时返回 null。
    /// </summary>
    public SsaProgram? BuildSsa(ImmutableHashSet<string>? extVars, bool optimize = true)
    {
        var bound = Binder.BindProgram(Result, extVars);
        if (bound.Diagnostics.HasErrors())
            return null;
        var ssaProgram = SsaProgramBuilder.Build(bound);
        if (optimize)
            SsaOptimizer.Optimize(ssaProgram);
        return ssaProgram;
    }
}