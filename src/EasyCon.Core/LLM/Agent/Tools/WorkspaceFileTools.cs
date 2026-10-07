using EasyCon.Core.LLM.Tools;
using System.Text.Json;

namespace EasyCon.Core.LLM.Agent.Tools;

/// <summary>
/// 工作区文件工具集：以脚本项目目录为根的通用文件读写能力。
/// 所有路径都限制在根目录内（拒绝 <c>..</c> 逃逸）；写类操作先把原文件
/// 快照到 <c>.easycon/history/</c>（带时间戳），误改可手工回滚。
/// 与宿主的编辑区无关，GUI/CLI/无头环境行为一致。
/// </summary>
public static class WorkspaceFileTools
{
    /// <summary>单次读取的字符上限，超出截断并附尾注。</summary>
    public const int MaxReadChars = 40_000;

    /// <summary>单次写入的字符上限。</summary>
    public const int MaxWriteChars = 200_000;

    /// <summary>把工作区文件工具注册到工具注册中心。</summary>
    /// <param name="registry">工具注册中心。</param>
    /// <param name="workspaceRootProvider">返回工作区根目录（未打开脚本项目时为 null）。</param>
    public static void RegisterAll(ToolRegistry registry, Func<string?> workspaceRootProvider)
    {
        registry.Register(new GlobFilesTool(workspaceRootProvider));
        registry.Register(new ReadFileTool(workspaceRootProvider));
        registry.Register(new WriteFileTool(workspaceRootProvider));
        registry.Register(new EditFileTool(workspaceRootProvider));
    }

    /// <summary>把相对路径安全解析到根目录内；逃逸或非法路径返回 null。</summary>
    public static string? ResolveSafe(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;
        var candidate = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(candidate))
            return null;
        var combined = Path.GetFullPath(Path.Combine(root, candidate));
        var normalizedRoot = Path.GetFullPath(root);
        if (!combined.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(combined, normalizedRoot, StringComparison.Ordinal))
            return null;
        return combined;
    }

    /// <summary>写入/覆盖前把原文件快照到 <c>.easycon/history/</c>。</summary>
    public static void SnapshotOriginal(string root, string absolutePath)
    {
        if (!File.Exists(absolutePath))
            return;
        var historyDir = Path.Combine(root, ".easycon", "history");
        Directory.CreateDirectory(historyDir);
        var name = Path.GetFileName(absolutePath);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        File.Copy(absolutePath, Path.Combine(historyDir, $"{stamp}_{name}"), overwrite: true);
    }

    /// <summary>工作区未就绪（未打开脚本项目）的标准错误结果。</summary>
    public static ToolResult NoWorkspace() =>
        ToolResult.Error("[错误] 未打开脚本项目，文件工具不可用。请先打开一个脚本文件。");
}

/// <summary>glob_files：按通配符模式列出工作区内的文件。</summary>
public class GlobFilesTool : IAiTool
{
    private readonly Func<string?> _rootProvider;

    public GlobFilesTool(Func<string?> rootProvider) => _rootProvider = rootProvider;

    public string Name => "glob_files";

    public string Description =>
        "在工作区内按通配符模式查找文件（如 \"**/*.ecs\"、\"recipes/*.json\"）。返回相对路径列表。";

    public ToolConcurrency Concurrency => ToolConcurrency.Parallel;

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["pattern"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "glob 模式，相对工作区根目录。支持 ** 递归。"
            }
        },
        Required = ["pattern"]
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var root = _rootProvider();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return Task.FromResult(WorkspaceFileTools.NoWorkspace());

        var pattern = args.TryGetValue("pattern", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()!
            : "";
        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult(ToolResult.Error("[错误] 缺少必填参数 pattern"));

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = pattern.Contains("**", StringComparison.Ordinal),
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            };
            var searchPattern = pattern.Replace("**/", "");
            var rootName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
            var matches = Directory.EnumerateFiles(root, searchPattern, options)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.easycon{Path.DirectorySeparatorChar}"))
                .Select(f => Path.GetRelativePath(root, f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .Take(200)
                .ToList();
            var lines = new List<string> { $"共 {matches.Count} 个匹配文件：" };
            lines.AddRange(matches);
            return Task.FromResult(ToolResult.Ok(string.Join("\n", lines)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"[错误] glob 执行失败: {ex.Message}"));
        }
    }
}

/// <summary>read_file：读取工作区内一个 UTF-8 文本文件。</summary>
public class ReadFileTool : IAiTool
{
    private readonly Func<string?> _rootProvider;

    public ReadFileTool(Func<string?> rootProvider) => _rootProvider = rootProvider;

    public string Name => "read_file";

    public string Description =>
        "读取工作区内一个文本文件的内容（UTF-8）。超过 40000 字符会被截断。";

    public ToolConcurrency Concurrency => ToolConcurrency.Parallel;

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["path"] = new JsonSchemaProperty { Type = "string", Description = "相对工作区根目录的文件路径。" }
        },
        Required = ["path"]
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var root = _rootProvider();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return Task.FromResult(WorkspaceFileTools.NoWorkspace());

        var path = args.TryGetValue("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()!
            : "";
        var absolute = WorkspaceFileTools.ResolveSafe(root, path);
        if (absolute is null)
            return Task.FromResult(ToolResult.Error($"[错误] 非法路径: {path}（必须是工作区内的相对路径）"));
        if (!File.Exists(absolute))
            return Task.FromResult(ToolResult.Error($"[错误] 文件不存在: {path}"));

        try
        {
            var content = File.ReadAllText(absolute);
            if (content.Length > WorkspaceFileTools.MaxReadChars)
                content = string.Concat(content.AsSpan(0, WorkspaceFileTools.MaxReadChars),
                    $"\n\n[文件过长已截断：原始 {content.Length} 字符，仅保留前 {WorkspaceFileTools.MaxReadChars} 字符。]");
            return Task.FromResult(ToolResult.Ok(content));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"[错误] 读取失败: {ex.Message}"));
        }
    }
}

/// <summary>write_file：写入或覆盖工作区内的文本文件（写前自动快照）。</summary>
public class WriteFileTool : IAiTool
{
    private readonly Func<string?> _rootProvider;

    public WriteFileTool(Func<string?> rootProvider) => _rootProvider = rootProvider;

    public string Name => "write_file";

    public string Description =>
        "把完整内容写入工作区内的文本文件（覆盖）。原文件会自动快照到 .easycon/history/ 以便回滚。";

    public ToolConcurrency Concurrency => ToolConcurrency.Parallel;

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["path"] = new JsonSchemaProperty { Type = "string", Description = "相对工作区根目录的文件路径。" },
            ["content"] = new JsonSchemaProperty { Type = "string", Description = "要写入的完整内容。" }
        },
        Required = ["path", "content"]
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var root = _rootProvider();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return Task.FromResult(WorkspaceFileTools.NoWorkspace());

        var path = args.TryGetValue("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()!
            : "";
        var absolute = WorkspaceFileTools.ResolveSafe(root, path);
        if (absolute is null)
            return Task.FromResult(ToolResult.Error($"[错误] 非法路径: {path}（必须是工作区内的相对路径）"));

        var content = args.TryGetValue("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? ""
            : "";
        if (content.Length > WorkspaceFileTools.MaxWriteChars)
            return Task.FromResult(ToolResult.Error(
                $"[错误] 内容过长（{content.Length} 字符，上限 {WorkspaceFileTools.MaxWriteChars}）。请分次写入或缩减内容。"));

        try
        {
            WorkspaceFileTools.SnapshotOriginal(root, absolute);
            var dir = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(absolute, content);
            var action = File.Exists(absolute) ? "已覆盖（原文件已快照）" : "已创建";
            return Task.FromResult(ToolResult.Ok($"[成功] {path} {action}，共 {content.Length} 字符。"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"[错误] 写入失败: {ex.Message}"));
        }
    }
}

/// <summary>edit_file：在工作区文件内做精确文本替换（写前自动快照）。</summary>
public class EditFileTool : IAiTool
{
    private readonly Func<string?> _rootProvider;

    public EditFileTool(Func<string?> rootProvider) => _rootProvider = rootProvider;

    public string Name => "edit_file";

    public string Description =>
        "在工作区文件内查找一段精确文本并替换（old_string 必须唯一，除非指定 replace_all）。原文件会自动快照。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["path"] = new JsonSchemaProperty { Type = "string", Description = "相对工作区根目录的文件路径。" },
            ["old_string"] = new JsonSchemaProperty { Type = "string", Description = "要查找的精确文本。" },
            ["new_string"] = new JsonSchemaProperty { Type = "string", Description = "替换为的文本。" },
            ["replace_all"] = new JsonSchemaProperty { Type = "boolean", Description = "替换所有匹配（默认只允许唯一匹配）。" }
        },
        Required = ["path", "old_string", "new_string"]
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var root = _rootProvider();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return Task.FromResult(WorkspaceFileTools.NoWorkspace());

        var path = args.TryGetValue("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()!
            : "";
        var absolute = WorkspaceFileTools.ResolveSafe(root, path);
        if (absolute is null)
            return Task.FromResult(ToolResult.Error($"[错误] 非法路径: {path}（必须是工作区内的相对路径）"));
        if (!File.Exists(absolute))
            return Task.FromResult(ToolResult.Error($"[错误] 文件不存在: {path}"));

        var oldString = args.TryGetValue("old_string", out var o) && o.ValueKind == JsonValueKind.String
            ? o.GetString() ?? ""
            : "";
        var newString = args.TryGetValue("new_string", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? ""
            : "";
        var replaceAll = args.TryGetValue("replace_all", out var r) && r.ValueKind == JsonValueKind.True;
        if (oldString.Length == 0)
            return Task.FromResult(ToolResult.Error("[错误] old_string 不能为空"));

        try
        {
            var content = File.ReadAllText(absolute);
            var count = CountOccurrences(content, oldString);
            if (count == 0)
                return Task.FromResult(ToolResult.Error("[错误] 未找到 old_string，文件未修改。"));
            if (count > 1 && !replaceAll)
                return Task.FromResult(ToolResult.Error(
                    $"[错误] old_string 出现 {count} 次，不唯一。请扩大上下文使其唯一，或设置 replace_all=true。"));

            WorkspaceFileTools.SnapshotOriginal(root, absolute);
            var updated = replaceAll ? content.Replace(oldString, newString) : ReplaceFirst(content, oldString, newString);
            File.WriteAllText(absolute, updated);
            return Task.FromResult(ToolResult.Ok($"[成功] {path} 已替换 {count} 处。"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"[错误] 编辑失败: {ex.Message}"));
        }
    }

    private static int CountOccurrences(string content, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string ReplaceFirst(string content, string needle, string replacement)
    {
        var index = content.IndexOf(needle, StringComparison.Ordinal);
        return string.Concat(content.AsSpan(0, index), replacement, content.AsSpan(index + needle.Length));
    }
}