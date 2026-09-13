using EasyCon.Script.Binding;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using System.Diagnostics;

namespace EasyCon.Script.Modules;

/// <summary>模块图节点（图构建与编译管线的内部载体；docs/ModuleSystem.md §3.3）。</summary>
internal sealed class ModuleNode
{
    public required string Name;
    public required string Source;
    /// <summary>语法树（惰性）：仅缓存未命中/主模块才 parse——缓存命中路径只读 .ecm 接口区
    /// （图构建经 Lexer 令牌扫描 IMPORT，不对依赖全量 parse）。</summary>
    public SyntaxTree? Tree;
    public required string Path;
    /// <summary>依赖节点名（图边；std/vision 隐式依赖注入在前）。</summary>
    public readonly List<string> Dependencies = new();
    public ModuleArtifact Artifact = null!;
    public ModuleInterface Interface = null!;
}

/// <summary>诊断落点 helper（轻量扫描期 / .err 重放 / 异常包装共用）。</summary>
internal static class ModuleLocations
{
    /// <summary>无源码位置可依的诊断落点（.err 重放/异常包装/轻量扫描期）：零跨度 + 模块文件名。</summary>
    public static TextLocation Default(SyntaxTree? tree)
        => new(SourceText.From("", tree?.Text.FileName ?? ""), new SourceSpan(0, 0));

    public static TextLocation FirstImport(SyntaxTree tree)
        => tree.Root.Members.OfType<ImportStmt>().FirstOrDefault()?.Location
           ?? tree.Root.Members.FirstOrDefault()?.Location
           ?? Default(tree);
}

/// <summary>
/// 模块依赖图构建（docs/ModuleSystem.md §3.3/§5.3 的图构建半区）：
/// main 树注册 + IMPORT 递归展开 + 环检测（DFS 当前路径栈）+ lib/ 自动加载（v1
/// ImportResolver 顺序语义：显式 import 之后、main 之前）→ 隐式 std/vision 依赖注入 →
/// 拓扑序编译序列（被依赖者在前，main 最后）。依赖模块只做 Lexer 令牌级导入扫描
/// （禁止裸正则），全量 parse 推迟到缓存未命中之后（编译管线的 EnsureParsed）。
/// 致命错误经 <see cref="DiagnosticBag"/> 上报（HasErrors 由调用方判定）；非致命提示透传。
/// </summary>
internal static class ModuleGraphBuilder
{
    internal sealed class Graph
    {
        public required Dictionary<string, ModuleNode> Nodes;
        /// <summary>std → vision → 拓扑序用户模块 → main；存在致命图错误时仍为可编译前缀（调用方先查诊断）。</summary>
        public required List<ModuleNode> CompileOrder;
    }

    public static Graph Build(SyntaxTree mainTree, string? scriptDir, bool allowLibAutoLoad,
        CompilationTiming timing, DiagnosticBag diagnostics, bool legacySyntax = true)
    {
        var sw = Stopwatch.StartNew();
        var mainSource = mainTree.Text.ToString();
        var mainPath = mainTree.Text.FileName;

        // ---- 节点注册：std/vision 内嵌源码 + main ----
        var nodes = new Dictionary<string, ModuleNode>(StringComparer.OrdinalIgnoreCase);
        var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ModuleNode AddEmbedded(string name, string source)
        {
            var node = new ModuleNode { Name = name, Source = source, Tree = null, Path = "" };
            nodes[name] = node;
            return node;
        }

        AddEmbedded(ProjectCompiler.StdModule, StdLib.StdSource);
        AddEmbedded(ProjectCompiler.VisionModule, StdLib.VisionSource);
        timing.StdLibLoad += sw.Elapsed;

        var mainNode = new ModuleNode
        {
            Name = ProjectCompiler.MainModule,
            Source = mainSource,
            Tree = mainTree,
            Path = mainPath,
        };
        nodes[ProjectCompiler.MainModule] = mainNode;
        byPath[mainPath] = ProjectCompiler.MainModule;

        // ---- IMPORT 递归展开（DFS 后序 = 拓扑序）----
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        var order = new List<string>();

        void Collect(ModuleNode node)
        {
            if (!visiting.Add(node.Name))
            {
                diagnostics.ReportCircularImport(NodeLocation(node), node.Path);
                return;
            }
            stack.Add(node.Name);
            foreach (var (importPath, importLocation) in ScannedImports(node, legacySyntax))
            {
                if (!File.Exists(importPath))
                {
                    diagnostics.ReportImportFileNotFound(importLocation, importPath);
                    continue;
                }
                if (byPath.TryGetValue(importPath, out var depName))
                {
                    if (visiting.Contains(depName))
                        diagnostics.ReportCircularImport(importLocation, importPath);
                    else if (!node.Dependencies.Contains(depName))
                        node.Dependencies.Add(depName);
                    continue;
                }
                var moduleName = Path.GetFileNameWithoutExtension(importPath);
                if (nodes.ContainsKey(moduleName))
                {
                    diagnostics.ReportModuleNameConflict(importLocation, moduleName, importPath);
                    continue;
                }
                var swRead = Stopwatch.StartNew();
                var depSource = File.ReadAllText(importPath);
                var depNode = new ModuleNode
                {
                    Name = moduleName,
                    Source = depSource,
                    Tree = null,
                    Path = importPath,
                };
                nodes[moduleName] = depNode;
                byPath[importPath] = moduleName;
                timing.FileLoad += swRead.Elapsed;
                Collect(depNode);
                if (!node.Dependencies.Contains(depNode.Name))
                    node.Dependencies.Add(depNode.Name);
            }
            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(node.Name);
            order.Add(node.Name);
        }

        sw.Restart();
        Collect(mainNode);
        timing.ImportResolve += sw.Elapsed;

        // ---- lib/ 目录自动加载（v1 ImportResolver 顺序语义：显式 import 之后、main 之前）----
        if (allowLibAutoLoad && scriptDir != null)
        {
            sw.Restart();
            var libDir = Path.Combine(scriptDir, "lib");
            if (Directory.Exists(libDir))
            {
                foreach (var file in Directory.GetFiles(libDir, "*.ecs").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var fullPath = Path.GetFullPath(file);
                    if (byPath.ContainsKey(fullPath))
                        continue;   // 已由显式 import 加载（自动加载去重，v1 同语义）
                    var moduleName = Path.GetFileNameWithoutExtension(fullPath);
                    if (nodes.ContainsKey(moduleName))
                    {
                        diagnostics.ReportModuleNameConflict(ModuleLocations.FirstImport(mainTree), moduleName, fullPath);
                        continue;
                    }
                    var swRead = Stopwatch.StartNew();
                    var libNode = new ModuleNode
                    {
                        Name = moduleName,
                        Source = File.ReadAllText(fullPath),
                        Tree = null,
                        Path = fullPath,
                    };
                    nodes[moduleName] = libNode;
                    byPath[fullPath] = moduleName;
                    timing.FileLoad += swRead.Elapsed;
                    Collect(libNode);   // lib 可再 import 其他模块（嵌套导入）
                    if (!mainNode.Dependencies.Contains(moduleName))
                        mainNode.Dependencies.Add(moduleName);
                }
            }
            timing.AutoLoadLib += sw.Elapsed;
        }

        // ---- 隐式依赖：std/vision 先于一切用户模块；main 恒最后 ----
        var compileOrder = new List<ModuleNode>
        {
            nodes[ProjectCompiler.StdModule],
            nodes[ProjectCompiler.VisionModule],
        };
        foreach (var name in order)
        {
            if (name == ProjectCompiler.MainModule)
                continue;
            compileOrder.Add(nodes[name]);
            var deps = nodes[name].Dependencies;
            deps.Insert(0, ProjectCompiler.VisionModule);
            deps.Insert(0, ProjectCompiler.StdModule);
        }
        compileOrder.Add(mainNode);
        mainNode.Dependencies.Insert(0, ProjectCompiler.VisionModule);
        mainNode.Dependencies.Insert(0, ProjectCompiler.StdModule);

        return new Graph { Nodes = nodes, CompileOrder = compileOrder };
    }

    // ---- 轻量导入扫描 ----

    /// <summary>节点的导入引用列表：已 parse 走 AST；未 parse 走 Lexer 令牌扫描
    /// （IMPORT + STRING 令牌对，跳过换行/空白/注释 trivia——杜绝裸正则对注释/字符串的误判）。</summary>
    static List<(string FullPath, TextLocation Location)> ScannedImports(ModuleNode node, bool legacySyntax)
    {
        if (node.Tree is { } tree)
            return tree.Root.Members.OfType<ImportStmt>()
                .Select(i => (Path.GetFullPath(i.FullFileName), i.Location))
                .ToList();

        var sourceText = SourceText.From(node.Source, node.Path.Length > 0 ? node.Path : node.Name + ".ecs");
        var tokens = SyntaxTree.ParseTokens(sourceText, legacySyntax);
        // 导入路径解析与 Parser.ParseImport 一致：InitPath = <文件目录>/lib/
        var initPath = Path.Combine(Path.GetDirectoryName(node.Path) ?? "", "lib");
        var result = new List<(string, TextLocation)>();
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Type != TokenType.IMPORT)
                continue;
            int j = i + 1;
            while (j < tokens.Length && tokens[j].Type is TokenType.NEWLINE or TokenType.WhitespaceTrivia or TokenType.COMMENT)
                j++;
            if (j >= tokens.Length || tokens[j].Type != TokenType.STRING)
                continue;   // 残缺 import：全量 parse 时由 Parser 报错（缓存命中路径此前也必然合法）
            var lib = tokens[j].STRTrimQ();
            result.Add((Path.GetFullPath(Path.Combine(initPath, lib)), tokens[j].Location));
        }
        return result;
    }

    /// <summary>无树节点的诊断落点（环检测防御分支）：模块文件名 + 零跨度。</summary>
    static TextLocation NodeLocation(ModuleNode node)
        => node.Tree is { } tree
            ? ModuleLocations.FirstImport(tree)
            : new TextLocation(SourceText.From("", node.Path), new SourceSpan(0, 0));
}