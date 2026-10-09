using EasyCon.Script.Binding;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using System.Diagnostics;

namespace EasyCon.Script.Modules;

/// <summary>模块图节点（图构建与编译管线的内部载体；docs/ModuleImportRules.md）。</summary>
internal sealed class ModuleNode
{
    public required string Name;
    public required string Source;
    /// <summary>语法树（惰性）：仅缓存未命中/主模块才 parse——缓存命中路径只读 .eci/.ecm
    /// 接口区（图构建经 Lexer 令牌扫描 IMPORT，不对依赖全量 parse；接口预提取走 .eci）。</summary>
    public SyntaxTree? Tree;
    public required string Path;
    /// <summary>IMPORT 解析基准 = 本文件所在目录（R2；内嵌模块为 null）。</summary>
    public string? ImportBase;
    /// <summary>显式 IMPORT 依赖（环检测作用域；编译层级/别名解析的唯一来源）。</summary>
    public readonly List<string> Dependencies = new();
    /// <summary>自动依赖：同目录互见 + main 的 lib/ 自动导入（R1/R3）；
    /// 不参与环检测——同目录互见天然双向，接口先行预提取保证循环可见可编译。</summary>
    public readonly List<string> AutoDependencies = new();
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
/// 模块依赖图构建（docs/ModuleImportRules.md）：
/// R1 同目录互见（目录即包，mutual 自动边）；R7 入口所在目录豁免（并列主脚本互不打扰）；
/// R2 显式 IMPORT 基准 = 导入文件所在目录；
/// R3 main 隐式导入 lib/*.ecs（不传递）；R4 跨目录必须显式导入；R5 模块名 = 相对主脚本目录的
/// 规范化路径（POSIX 分隔符、去扩展名）。显式 IMPORT 闭包 DFS + 环检测；自动边定点收敛
/// （新注册节点再扫目录与其显式导入，直至闭包稳定）。自动/显式边分列表维护：
/// 合成器注册序 = std → vision → 自动（名序，同包遮蔽导入）→ 显式（语句序）。
/// 依赖模块只做 Lexer 令牌级导入扫描（禁止裸正则）；全量 parse 推迟到管线接口预提取/缓存未命中。
/// 致命错误经 <see cref="DiagnosticBag"/> 上报；非致命提示透传。
/// </summary>
internal static class ModuleGraphBuilder
{
    internal sealed class Graph
    {
        public required Dictionary<string, ModuleNode> Nodes;
        /// <summary>std → vision → 用户模块（显式边层级 + 同层名序）→ main；存在致命图错误时仍为可编译前缀（调用方先查诊断）。</summary>
        public required List<ModuleNode> CompileOrder;
    }

    public static Graph Build(SyntaxTree mainTree, string? scriptDir,
        CompilationTiming timing, DiagnosticBag diagnostics, bool legacySyntax = true)
    {
        var sw = Stopwatch.StartNew();
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
            Source = mainTree.Text.ToString(),
            Tree = mainTree,
            Path = mainPath,
            ImportBase = scriptDir,
        };
        nodes[ProjectCompiler.MainModule] = mainNode;
        if (mainPath.Length > 0)
            byPath[mainPath] = ProjectCompiler.MainModule;

        // ---- 显式 IMPORT 闭包（DFS；环检测只作用于显式边）----
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expandQueue = new Queue<ModuleNode>();
        expandQueue.Enqueue(mainNode);

        void CollectExplicit(ModuleNode node)
        {
            if (!visiting.Add(node.Name))
            {
                diagnostics.ReportCircularImport(NodeLocation(node), node.Path);
                return;
            }
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
                var depNode = RegisterFileNode(importPath, scriptDir, nodes, byPath, importLocation, diagnostics);
                if (depNode == null)
                    continue;
                expandQueue.Enqueue(depNode);
                if (!node.Dependencies.Contains(depNode.Name))
                    node.Dependencies.Add(depNode.Name);
            }
            visiting.Remove(node.Name);
        }

        // ---- 定点收敛：显式闭包 + 目录自动注册（同目录互见 / lib/ 自动导入）相互触发，
        //      直到不再产生新节点。自动边双向追加、不做环检测（接口先行预提取保证可编译）。
        //      入口脚本所在目录豁免同目录互见（R7）：项目根并列多个主脚本互不打扰，
        //      根目录的显式导入模块同样不与根目录其他脚本互见。----
        var dirScanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mainDirectory = mainNode.Path.Length > 0 ? Path.GetDirectoryName(mainNode.Path) : null;
        while (expandQueue.Count > 0)
        {
            var node = expandQueue.Dequeue();
            if (node.Path.Length > 0 && !dirScanned.Add(node.Path))
                continue;
            CollectExplicit(node);

            // R3：main 隐式导入 lib/*.ecs（不传递；lib 内互见由同目录规则自然成立）
            if (node == mainNode && scriptDir != null)
                RegisterDirAutos(Path.Combine(scriptDir, "lib"), scriptDir, nodes, byPath,
                    mainNode, mainNode.AutoDependencies, expandQueue, diagnostics);

            // R1：同目录互见（目录即包）；R7：入口所在目录豁免
            if (node.Path.Length > 0)
            {
                var dir = Path.GetDirectoryName(node.Path);
                if (dir != null && dir != mainDirectory)
                    RegisterDirAutos(dir, scriptDir, nodes, byPath,
                        node, node.AutoDependencies, expandQueue, diagnostics);
            }
        }

        // ---- 合并依赖与编译序：std/vision 头插 → 自动（名序，同包遮蔽显式导入）→ 显式（发现序）。
        //      层级只由显式边约束（Kahn；自动边不约束——接口先行已保证就绪）：
        //      跨目录显式链的 &lt;init&gt; 拓扑序得以保持（依赖者后于被依赖者），
        //      同目录互见环（自动边）由同层名序破环（docs/ModuleImportRules.md §2）。----
        var userNames = nodes.Keys
            .Where(n => n != ProjectCompiler.MainModule
                && n != ProjectCompiler.StdModule && n != ProjectCompiler.VisionModule)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var levelMemo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int LevelOf(string name)
        {
            if (levelMemo.TryGetValue(name, out var level))
                return level;
            levelMemo[name] = 0;   // 环防御：显式环已报错，此处按第 0 层截断
            var node = nodes[name];
            level = 0;
            foreach (var dep in node.Dependencies)
            {
                if (dep == ProjectCompiler.StdModule || dep == ProjectCompiler.VisionModule)
                    continue;
                if (nodes.ContainsKey(dep))
                    level = Math.Max(level, LevelOf(dep) + 1);
            }
            levelMemo[name] = level;
            return level;
        }
        foreach (var name in userNames)
            _ = LevelOf(name);

        var compileOrder = new List<ModuleNode>
        {
            nodes[ProjectCompiler.StdModule],
            nodes[ProjectCompiler.VisionModule],
        };
        foreach (var name in userNames.OrderBy(n => levelMemo[n]).ThenBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var node = nodes[name];
            node.Dependencies.Insert(0, ProjectCompiler.VisionModule);
            node.Dependencies.Insert(0, ProjectCompiler.StdModule);
            node.Dependencies.InsertRange(2, SortedAutos(node));
            compileOrder.Add(node);
        }
        mainNode.Dependencies.Insert(0, ProjectCompiler.VisionModule);
        mainNode.Dependencies.Insert(0, ProjectCompiler.StdModule);
        mainNode.Dependencies.InsertRange(2, SortedAutos(mainNode));
        compileOrder.Add(mainNode);

        return new Graph { Nodes = nodes, CompileOrder = compileOrder };
    }

    /// <summary>自动依赖排序视图；已同时出现在显式依赖里的项去重（显式别名优先）。</summary>
    static List<string> SortedAutos(ModuleNode node)
        => node.AutoDependencies
            .Where(d => !node.Dependencies.Contains(d))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>注册文件模块节点；主目录不可用时模块名退化为文件名（内存单文件场景无自动注册）。</summary>
    static ModuleNode? RegisterFileNode(string importPath, string? scriptDir,
        Dictionary<string, ModuleNode> nodes, Dictionary<string, string> byPath,
        TextLocation location, DiagnosticBag diagnostics)
    {
        var moduleName = ModuleNameFor(importPath, scriptDir);
        if (nodes.ContainsKey(moduleName))
        {
            diagnostics.ReportModuleNameConflict(location, moduleName, importPath);
            return null;
        }
        var depSource = File.ReadAllText(importPath);
        var depNode = new ModuleNode
        {
            Name = moduleName,
            Source = depSource,
            Tree = null,
            Path = importPath,
            ImportBase = Path.GetDirectoryName(importPath),
        };
        nodes[moduleName] = depNode;
        byPath[importPath] = moduleName;
        return depNode;
    }

    /// <summary>目录自动注册（R1/R3）：扫描 dir 下全部 .ecs，未注册者注册为新节点并加自动边
    /// autoOwner → 新节点；已注册者补齐反向自动边（同目录互见的 mutual 语义）。</summary>
    static void RegisterDirAutos(string dir, string? scriptDir,
        Dictionary<string, ModuleNode> nodes, Dictionary<string, string> byPath,
        ModuleNode autoOwner, List<string> autoDeps, Queue<ModuleNode> expandQueue,
        DiagnosticBag diagnostics)
    {
        if (!Directory.Exists(dir))
            return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.ecs").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var full = Path.GetFullPath(file);
            if (byPath.TryGetValue(full, out var existing))
            {
                if (existing != autoOwner.Name
                    && !autoDeps.Contains(existing)
                    && !autoOwner.Dependencies.Contains(existing))   // 显式依赖已有：去重（别名优先）
                    autoDeps.Add(existing);   // 反向边：目录内互见
                continue;
            }
            var node = RegisterFileNode(full, scriptDir, nodes, byPath,
                ModuleLocations.Default(null), diagnostics);
            if (node == null)
                continue;
            expandQueue.Enqueue(node);
            if (!autoDeps.Contains(node.Name))
                autoDeps.Add(node.Name);
        }
    }

    /// <summary>R5：模块名 = 相对主脚本目录的规范化路径（POSIX 分隔符、去扩展名；
    /// lib/net/http.ecs → "lib/net/http"）。主目录不可用时退化为文件名。</summary>
    static string ModuleNameFor(string importPath, string? scriptDir)
    {
        if (scriptDir == null)
            return Path.GetFileNameWithoutExtension(importPath);
        var rel = Path.GetRelativePath(scriptDir, importPath);
        return Path.ChangeExtension(rel, null)?.Replace('\\', '/') ?? "";
    }

    // ---- 轻量导入扫描 ----

    /// <summary>节点的导入引用列表：已 parse 走 AST；未 parse 走 Lexer 令牌扫描
    /// （IMPORT + STRING 令牌对，跳过换行/空白/注释 trivia——杜绝裸正则对注释/字符串的误判）。
    /// 解析基准 = 节点 ImportBase（R2 = 导入文件所在目录）。</summary>
    static List<(string FullPath, TextLocation Location)> ScannedImports(ModuleNode node, bool legacySyntax)
    {
        if (node.Tree is { } tree)
            return tree.Root.Members.OfType<ImportStmt>()
                .Select(i => (Path.GetFullPath(i.FullFileName), i.Location))
                .ToList();

        var sourceText = SourceText.From(node.Source, node.Path.Length > 0 ? node.Path : node.Name + ".ecs");
        var tokens = SyntaxTree.ParseTokens(sourceText, legacySyntax, node.ImportBase);
        var initPath = node.ImportBase;
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
            var fullPath = initPath != null
                ? Path.GetFullPath(Path.Combine(initPath, lib))
                : lib;
            result.Add((fullPath, tokens[j].Location));
        }
        return result;
    }

    /// <summary>无树节点的诊断落点（环检测防御分支）：模块文件名 + 零跨度。</summary>
    static TextLocation NodeLocation(ModuleNode node)
        => node.Tree is { } tree
            ? ModuleLocations.FirstImport(tree)
            : new TextLocation(SourceText.From("", node.Path), new SourceSpan(0, 0));
}