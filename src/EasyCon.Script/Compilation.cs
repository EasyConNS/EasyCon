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

public sealed class EvaluationResult(ImmutableArray<Diagnostic> diagnostics, Value value)
{
    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
    public Value Result { get; } = value;
}

public sealed class Compilation
{
    public bool KeyAction { get; private set; } = false;
    public bool NeedIL { get; private set; } = false;

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

    private BoundProgram GetProgram(ImmutableHashSet<string>? extVars)
    {
        return Binder.BindProgram(SyntaxTrees, extVars);
    }

    public ImmutableArray<Diagnostic> Compile(ImmutableHashSet<string>? extVars)
    {
        var program = GetProgram(extVars);
        KeyAction = program.KeyAction;
        NeedIL = program.NeedIL;
        return program.Diagnostics;
    }

    public EvaluationResult Evaluate(IOutputAdapter output, ICGamePad? pad, OcrDelegate? ocr,
        FrameDelegate? frameProvider, RoiDelegate? roiProvider, LabelMatchDelegate? labelMatch,
        ImmutableHashSet<string>? labelNames,
        CancellationToken token)
    {
        var program = GetProgram(labelNames ?? []);
        if (program.Diagnostics.HasErrors())
            return new EvaluationResult(program.Diagnostics, Value.Void);

        // Bound → SSA
        var ssaProgram = SsaProgramBuilder.Build(program);
        SsaOptimizer.Optimize(ssaProgram);

        using var evaluator = new SsaEvaluator(ssaProgram, token)
        {
            GamePad = pad,
            Output = output,
            Ocr = ocr,
            Frame = frameProvider,
            Roi = roiProvider,
            LabelMatch = labelMatch,
        };
        var value = evaluator.Evaluate();

        return new EvaluationResult(ssaProgram.Diagnostics, value);
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