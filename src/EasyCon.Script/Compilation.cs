using EasyCon.Script.Binding;
using EasyCon.Script.Binding.Ssa;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Immutable;

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

    internal CompileResult(ImmutableArray<Diagnostic> diagnostics, SsaProgram? program, bool keyAction, bool needIL)
    {
        Diagnostics = diagnostics;
        Program = program;
        KeyAction = keyAction;
        NeedIL = needIL;
    }
}

public sealed class Compilation
{
    private Compilation(ImmutableArray<SyntaxTree> syntaxTrees)
    {
        SyntaxTrees = syntaxTrees;
    }

    public ImmutableArray<SyntaxTree> SyntaxTrees { get; }

    // lib 文件缓存：key = 文件路径，value = (最后修改时间, 解析结果)
    private static readonly ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, SyntaxTree Tree)> _libCache = new();

    public static Compilation Create(SyntaxTree mainTree)
    {
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>();
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 预加载内嵌标准库（最先加载，确保 Phase 1 binding）
        trees.Add(StdLib.GetStdTree());
        trees.Add(StdLib.GetVisionTree());

        // 从 import 语句加载指定的 lib 文件
        foreach (var member in mainTree.Root.Members)
        {
            if (member is ImportStmt import)
            {
                var libPath = Path.GetFullPath(Path.Combine(import.InitPath, import.Lib));
                if (File.Exists(libPath) && loadedPaths.Add(libPath))
                    trees.Add(LoadLibWithCache(libPath));
            }
        }

        // 自动加载 lib 目录下的其余脚本（带缓存）
        var fileName = mainTree.Text.FileName;
        if (!string.IsNullOrEmpty(fileName))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(fileName));
            if (dir != null)
            {
                var libDir = Path.Combine(dir, "lib");
                if (Directory.Exists(libDir))
                {
                    foreach (var libFile in Directory.GetFiles(libDir, "*.ecs"))
                    {
                        if (loadedPaths.Add(Path.GetFullPath(libFile)))
                            trees.Add(LoadLibWithCache(libFile));
                    }
                }
            }
        }

        trees.Add(mainTree);
        return new Compilation(trees.ToImmutable());
    }

    private static SyntaxTree LoadLibWithCache(string filePath)
    {
        var lastWrite = File.GetLastWriteTimeUtc(filePath);
        if (_libCache.TryGetValue(filePath, out var cached) && cached.LastWriteTimeUtc == lastWrite)
            return cached.Tree;

        var tree = SyntaxTree.Load(filePath, isLib: true);
        _libCache[filePath] = (lastWrite, tree);
        return tree;
    }

    /// <summary>
    /// 清除 lib 缓存（文件被外部修改后可调用）
    /// </summary>
    public static void ClearLibCache() => _libCache.Clear();

    /// <summary>
    /// 完整编译管线：Bind → SSA 生成 → SSA 优化。
    /// 编译错误时返回 Program=null 的 CompileResult。
    /// </summary>
    public CompileResult Compile(ImmutableHashSet<string>? extVars)
    {
        var bound = Binder.BindProgram(SyntaxTrees, extVars);
        var keyAction = bound.KeyAction;
        var needIL = bound.NeedIL;

        if (bound.Diagnostics.HasErrors())
            return new CompileResult(bound.Diagnostics, null, keyAction, needIL);

        // Bound → SSA → Optimize
        var ssaProgram = SsaProgramBuilder.Build(bound);
        SsaOptimizer.Optimize(ssaProgram);

        return new CompileResult(ssaProgram.Diagnostics, ssaProgram, keyAction, needIL);
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
}