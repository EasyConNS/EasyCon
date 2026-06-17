using System.IO;
using EasyCon.Core.Config;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EasyCon.Core.LLM.Skills;

/// <summary>
/// Skill 文件系统加载器：扫描目录、解析 SKILL.md frontmatter、缓存 body。
///
/// 搜索路径优先级（后者覆盖前者）：
///   1. 内置技能（随程序分发，&lt;AppDir&gt;/skills）
///   2. 用户技能（&lt;ConfigDir&gt;/skills，热插拔）
///   3. 项目级技能（&lt;ProjectDir&gt;/skills，随游戏项目走）
///
/// frontmatter 用 YamlDotNet 解析（支持完整 YAML 语法：标量/列表/块标量/引号）。
/// </summary>
public static class SkillLoader
{
    private const string SkillFileName = "SKILL.md";
    private const string FrontmatterDelim = "---";

    // 复用同一个反序列化器实例（线程安全，避免重复构建开销）
    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance) // snake_case → PascalCase
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// 从多个根目录加载所有 Skill，同名者按 paths 顺序后者覆盖前者。
    /// </summary>
    /// <param name="roots">按优先级升序排列的根目录（先内置、后用户、后项目）。</param>
    public static List<Skill> LoadFromPaths(IEnumerable<string> roots)
    {
        var byName = new Dictionary<string, Skill>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            foreach (var skillDir in EnumerateSkillDirs(root))
            {
                var skill = TryParse(skillDir);
                if (skill is null) continue;
                byName[skill.Manifest.Name] = skill; // 后者覆盖
            }
        }

        return byName.Values
            .OrderBy(s => s.Manifest.Priority)
            .ThenBy(s => s.Manifest.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 便捷重载：加载到指定注册中心。
    /// </summary>
    public static void LoadToRegistry(SkillRegistry registry, IEnumerable<string> roots)
    {
        foreach (var skill in LoadFromPaths(roots))
            registry.Register(skill);
    }

    /// <summary>
    /// 默认搜索路径（内置 + 用户）。项目级路径由调用方额外传入。
    /// </summary>
    public static IEnumerable<string> GetDefaultSearchPaths()
    {
        yield return AppPaths.UserSkillsDir;
        yield return GetBundledSkillsDir();
    }

    /// <summary>
    /// 完整搜索路径（含可选项目级）。优先级升序：内置 → 用户 → 项目，
    /// 后者覆盖前者（项目技能可定制化内置行为）。
    /// 项目级技能直接放在项目根的 <c>skills/</c> 子目录下。
    /// </summary>
    /// <param name="projectDirectory">项目根目录，为空则忽略项目级路径。</param>
    public static IEnumerable<string> GetSearchPaths(string? projectDirectory)
    {
        foreach (var p in GetDefaultSearchPaths()) yield return p;
        if (!string.IsNullOrEmpty(projectDirectory))
            yield return Path.Combine(projectDirectory, "skills");
    }

    /// <summary>内置技能目录：基于程序集所在目录下的 skills 子目录。</summary>
    public static string GetBundledSkillsDir()
    {
        var asmDir = AppContext.BaseDirectory;
        return Path.Combine(asmDir, "skills");
    }

    // ── 目录扫描 ────────────────────────────────────────────

    private static IEnumerable<string> EnumerateSkillDirs(string root)
    {
        IEnumerable<string> subDirs;
        try { subDirs = Directory.EnumerateDirectories(root); }
        catch (UnauthorizedAccessException) { yield break; }
        catch (DirectoryNotFoundException) { yield break; }

        foreach (var dir in subDirs)
        {
            if (File.Exists(Path.Combine(dir, SkillFileName)))
                yield return dir;
        }
    }

    // ── SKILL.md 解析 ───────────────────────────────────────

    /// <summary>
    /// 解析单个技能目录。SKILL.md 不存在或 frontmatter 缺 name 时返回 null。
    /// </summary>
    public static Skill? TryParse(string skillDir)
    {
        var skillPath = Path.Combine(skillDir, SkillFileName);
        if (!File.Exists(skillPath)) return null;

        var text = File.ReadAllText(skillPath);
        var manifest = TryParseFrontmatter(text);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name)) return null;

        return new Skill
        {
            Manifest = manifest,
            DirectoryPath = Path.GetFullPath(skillDir)
        };
    }

    /// <summary>读取 SKILL.md 正文（去除 frontmatter）。</summary>
    public static string ReadBody(string skillDir)
    {
        var skillPath = Path.Combine(skillDir, SkillFileName);
        if (!File.Exists(skillPath)) return "";
        var text = File.ReadAllText(skillPath);
        return StripFrontmatter(text);
    }

    /// <summary>读取子目录下的资源文件，返回内容。不存在返回 null。</summary>
    public static string? TryReadResource(string skillDir, string subDir, string relativePath)
    {
        var full = SafeCombine(skillDir, subDir, relativePath);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    /// <summary>解析子目录下资源文件路径（不读入内存）。不存在返回 null。</summary>
    public static string? TryResolveResource(string skillDir, string subDir, string relativePath)
    {
        var full = SafeCombine(skillDir, subDir, relativePath);
        return File.Exists(full) ? full : null;
    }

    /// <summary>列举子目录下所有文件的相对路径。目录不存在返回空。</summary>
    public static IReadOnlyList<string> ListResources(string skillDir, string subDir)
    {
        var full = Path.Combine(skillDir, subDir);
        if (!Directory.Exists(full)) return Array.Empty<string>();
        try
        {
            return Directory
                .EnumerateFiles(full, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(full, p).Replace('\\', '/'))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
        }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    // 防止 relativePath 逃逸出技能目录（如 ../../../etc/passwd）
    private static string SafeCombine(string skillDir, string subDir, string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(skillDir, subDir));
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootSlash = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootSlash, StringComparison.Ordinal) ? full : root;
    }

    // ── frontmatter 解析（YamlDotNet）──────────────────────

    /// <summary>
    /// 用 YamlDotNet 解析 frontmatter。解析失败返回 null（调用方据此跳过该技能）。
    /// </summary>
    internal static SkillManifest? TryParseFrontmatter(string text)
    {
        var (front, _) = SplitFrontmatter(text);
        if (string.IsNullOrWhiteSpace(front)) return null;

        try
        {
            return _yaml.Deserialize<SkillManifest?>(front);
        }
        catch
        {
            // frontmatter 格式错误 → 视为无技能，调用方跳过
            return null;
        }
    }

    /// <summary>
    /// 保留旧入口（部分内部调用与测试使用）。解析失败抛出异常。
    /// </summary>
    internal static SkillManifest ParseFrontmatter(string text)
    {
        var (front, _) = SplitFrontmatter(text);
        if (string.IsNullOrWhiteSpace(front)) return new SkillManifest();
        return _yaml.Deserialize<SkillManifest>(front);
    }

    private static (string front, string body) SplitFrontmatter(string text)
    {
        if (!text.StartsWith(FrontmatterDelim, StringComparison.Ordinal))
            return ("", text);

        // 跳过首行 ---
        var rest = text[FrontmatterDelim.Length..];
        var end = rest.IndexOf("\n" + FrontmatterDelim, StringComparison.Ordinal);
        if (end < 0) return ("", text);

        var front = rest[..end];
        var body = rest[(end + FrontmatterDelim.Length + 1)..];
        return (front, body);
    }

    private static string StripFrontmatter(string text)
    {
        var (_, body) = SplitFrontmatter(text);
        return body.TrimStart('\r', '\n');
    }
}
