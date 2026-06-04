using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Resolution;

/// <summary>
/// 负责文件加载：解析 import 声明、加载 lib 文件、检测循环导入。
/// </summary>
internal sealed class ImportResolver
{
    private readonly DiagnosticBag _diagnostics = new();

    /// <summary>
    /// 解析所有 import 并按顺序返回 SyntaxTree 列表。
    /// 顺序：stdlib → 显式 import → lib/ 自动加载 → main。
    /// 同时返回 alias → SyntaxTree 映射（仅主脚本的直接 aliased import）。
    /// </summary>
    public (ImmutableArray<SyntaxTree> Trees,
            ImmutableDictionary<string, SyntaxTree> AliasedTrees,
            DiagnosticBag Diagnostics) Resolve(SyntaxTree mainTree)
    {
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>();
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliasedTrees = ImmutableDictionary.CreateBuilder<string, SyntaxTree>(StringComparer.OrdinalIgnoreCase);

        // 预加载内嵌标准库（最先加载）
        trees.Add(StdLib.GetStdTree());
        trees.Add(StdLib.GetVisionTree());

        // 从 import 语句加载指定的 lib 文件（递归解析嵌套 import）
        ResolveImportsRecursive(mainTree, trees, loadedPaths, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // 收集主脚本的 aliased import → SyntaxTree 映射
        foreach (var member in mainTree.Root.Members)
        {
            if (member is ImportStmt { Alias: not null } import)
            {
                var libPath = Path.GetFullPath(import.FullFileName);
                var matching = trees.FirstOrDefault(t =>
                    !string.IsNullOrEmpty(t.Text.FileName) &&
                    Path.GetFullPath(t.Text.FileName).Equals(libPath, StringComparison.OrdinalIgnoreCase));
                if (matching != null)
                    aliasedTrees[import.Alias.Value] = matching;
            }
        }

        // 自动加载 lib 目录下的其余脚本（跳过已由 import 显式加载的文件）
        AutoLoadLibDirectory(mainTree, trees, loadedPaths);

        // 主脚本最后加载
        trees.Add(mainTree);

        return (trees.ToImmutable(), aliasedTrees.ToImmutable(), _diagnostics);
    }

    /// <summary>
    /// 递归解析 import 声明，支持嵌套导入和循环检测。
    /// </summary>
    private void ResolveImportsRecursive(
        SyntaxTree tree,
        ImmutableArray<SyntaxTree>.Builder trees,
        HashSet<string> loadedPaths,
        HashSet<string> visiting)
    {
        var treePath = tree.Text.FileName;
        if (!string.IsNullOrEmpty(treePath))
        {
            var fullPath = Path.GetFullPath(treePath);
            if (!visiting.Add(fullPath))
            {
                _diagnostics.ReportCircularImport(tree.Root.Members
                    .OfType<ImportStmt>()
                    .FirstOrDefault()?.Location ?? tree.Root.Members[0].Location,
                    fullPath);
                return;
            }
        }

        foreach (var member in tree.Root.Members)
        {
            if (member is not ImportStmt import)
                continue;

            var libPath = import.FullFileName;
            if (!File.Exists(libPath))
            {
                _diagnostics.ReportImportFileNotFound(import.Location, libPath);
                continue;
            }

            var fullPath = Path.GetFullPath(libPath);
            if (!loadedPaths.Add(fullPath))
                continue;

            var libTree = SyntaxTree.Load(fullPath, isLib: true);
            trees.Add(libTree);

            ResolveImportsRecursive(libTree, trees, loadedPaths, visiting);
        }

        if (!string.IsNullOrEmpty(treePath))
            visiting.Remove(Path.GetFullPath(treePath));
    }

    /// <summary>
    /// 自动加载 lib/ 目录下的 .ecs 文件，跳过已由显式 import 加载的文件。
    /// </summary>
    private static void AutoLoadLibDirectory(
        SyntaxTree mainTree,
        ImmutableArray<SyntaxTree>.Builder trees,
        HashSet<string> loadedPaths)
    {
        var fileName = mainTree.Text.FileName;
        if (string.IsNullOrEmpty(fileName))
            return;

        var dir = Path.GetDirectoryName(Path.GetFullPath(fileName));
        if (dir == null)
            return;

        var libDir = Path.Combine(dir, "lib");
        if (!Directory.Exists(libDir))
            return;

        foreach (var libFile in Directory.GetFiles(libDir, "*.ecs"))
        {
            var fullPath = Path.GetFullPath(libFile);
            if (loadedPaths.Add(fullPath))
                trees.Add(SyntaxTree.Load(fullPath, isLib: true));
        }
    }
}