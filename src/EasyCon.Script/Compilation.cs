using EasyCon.Script.Binding;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace EasyCon.Script;

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

        // 自动加载 lib 目录下的脚本（带缓存）
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

    public EvaluationResult Evaluate(IOutputAdapter output, ICGamePad? pad, IImageAdapter img,
        ImmutableDictionary<string, Func<int>> externalGetters,
        CancellationToken token)
    {
        var program = GetProgram([.. externalGetters.Select(v => v.Key)]);
        if (program.Diagnostics.HasErrors())
            return new EvaluationResult(program.Diagnostics, Value.Void);
        using var evaluator = new Evaluator(program, externalGetters ?? [], token)
        {
            GamePad = pad,
            Output = output,
            ImageAdapter = img,
        };
        var value = evaluator.Evaluate();

        return new EvaluationResult(program.Diagnostics, value);
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