using EasyCon.Core.Config;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EasyCon.Core.LLM.Skills;

/// <summary>
/// Skill 文件系统加载器：扫描目录、解析 SKILL.md frontmatter、缓存 body。
///
/// 搜索路径优先级（后者覆盖前者）：
///   1. 用户技能（&lt;ConfigDir&gt;/skills，热插拔）
///   2. 项目级技能（&lt;ProjectDir&gt;/skills，随游戏项目走）
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
    /// <param name="roots">按优先级升序排列的根目录（先用户、后项目）。</param>
    public static List<Skill> LoadFromPaths(IEnumerable<string> roots)
    {
        var byName = new Dictionary<string, Skill>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                Console.WriteLine($"[SkillLoader] 跳过路径(不存在): {root}");
                continue;
            }

            var dirs = EnumerateSkillDirs(root).ToList();
            Console.WriteLine($"[SkillLoader] 扫描 {root}: 找到 {dirs.Count} 个技能子目录");
            foreach (var skillDir in dirs)
            {
                var skill = TryParse(skillDir);
                if (skill is null)
                {
                    var reason = GetParseFailureReason(skillDir);
                    Console.WriteLine($"[SkillLoader]   跳过({reason}): {skillDir}");
                    continue;
                }
                byName[skill.Manifest.Name] = skill;
                Console.WriteLine($"[SkillLoader]   加载: {skill.Manifest.Name} ← {skillDir}");
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
    /// 默认搜索路径（仅用户）。项目级路径由调用方额外传入。
    /// </summary>
    public static IEnumerable<string> GetDefaultSearchPaths()
    {
        yield return AppPaths.UserSkillsDir;
    }

    /// <summary>
    /// 完整搜索路径（含可选项目级）。优先级升序：用户 → 项目，
    /// 后者覆盖前者（项目技能可定制化行为）。
    /// 项目级技能直接放在项目根的 <c>skills/</c> 子目录下。
    /// </summary>
    /// <param name="projectDirectory">项目根目录，为空则忽略项目级路径。</param>
    public static IEnumerable<string> GetSearchPaths(string? projectDirectory)
    {
        foreach (var p in GetDefaultSearchPaths()) yield return p;
        if (!string.IsNullOrEmpty(projectDirectory))
            yield return Path.Combine(projectDirectory, "skills");
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
            var hasSkillMd = File.Exists(Path.Combine(dir, SkillFileName));
            Console.WriteLine($"[SkillLoader]   遍历: {dir}  SKILL.md={(hasSkillMd ? "有" : "无")}");
            if (hasSkillMd)
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

    /// <summary>诊断用：返回解析失败的具体原因。</summary>
    private static string GetParseFailureReason(string skillDir)
    {
        var skillPath = Path.Combine(skillDir, SkillFileName);
        if (!File.Exists(skillPath)) return "SKILL.md 不存在";

        string text;
        try { text = File.ReadAllText(skillPath); }
        catch (Exception ex) { return $"读取 SKILL.md 失败: {ex.Message}"; }

        var (front, _) = SplitFrontmatter(text);
        if (string.IsNullOrWhiteSpace(front)) return "缺少 YAML frontmatter (--- 包裹)";

        // 先尝试 YamlDotNet
        try
        {
            var manifest = _yaml.Deserialize<SkillManifest?>(front);
            if (manifest is not null && !string.IsNullOrWhiteSpace(manifest.Name))
                return "未知";
        }
        catch { }

        // 回退到手动解析
        var manual = TryParseFrontmatterManual(front);
        if (manual is null || string.IsNullOrWhiteSpace(manual.Name))
            return "frontmatter 缺少 name 字段";
        return "未知";
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

    // ── frontmatter 解析（手动逐行解析，兼容含冒号的值）────────

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
            // YamlDotNet 解析失败，回退到手动逐行解析（兼容含冒号的值）
            return TryParseFrontmatterManual(front);
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

    /// <summary>
    /// 手动逐行解析 frontmatter。按第一个 ": " 分割 key/value，
    /// 兼容 value 中包含冒号的情况（如 "description: AI 翻译 @author: stormzhang"）。
    /// </summary>
    private static SkillManifest? TryParseFrontmatterManual(string front)
    {
        var manifest = new SkillManifest();
        var lines = front.Split('\n');
        string? currentKey = null;
        var currentValue = new System.Text.StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // 空行 / 纯注释
            if (string.IsNullOrWhiteSpace(line) || trimmed.StartsWith('#'))
            {
                // 空行结束上一个多行值
                if (currentKey is not null)
                {
                    ApplyKeyValue(manifest, currentKey, currentValue.ToString().TrimEnd());
                    currentKey = null;
                    currentValue.Clear();
                }
                continue;
            }

            // 列表项（- xxx）
            if (trimmed.StartsWith("- "))
            {
                if (currentKey is not null)
                {
                    // 先提交上一个 key 的当前值
                    ApplyKeyValue(manifest, currentKey, currentValue.ToString().TrimEnd());
                    currentKey = null;
                    currentValue.Clear();
                }
                // 列表项作为独立值处理，key 重复追加
                continue;
            }

            // 缩进续行（属于上一个 key 的多行值）
            if (line.Length > 0 && line[0] == ' ' && currentKey is not null)
            {
                currentValue.Append('\n').Append(trimmed);
                continue;
            }

            // 新 key: 先提交上一个
            if (currentKey is not null)
            {
                ApplyKeyValue(manifest, currentKey, currentValue.ToString().TrimEnd());
                currentValue.Clear();
            }

            // 按第一个 ": " 分割
            var colonIdx = line.IndexOf(": ", StringComparison.Ordinal);
            if (colonIdx < 0)
            {
                currentKey = null;
                continue;
            }

            currentKey = line[..colonIdx].Trim();
            var val = line[(colonIdx + 2)..].Trim();

            // 空值（下一行开始多行内容）
            if (string.IsNullOrEmpty(val))
            {
                currentValue.Clear();
            }
            else
            {
                // 列表值 [a, b, c]
                if (val.StartsWith('[') && val.EndsWith(']'))
                {
                    ApplyKeyValue(manifest, currentKey, val);
                    currentKey = null;
                }
                else
                {
                    currentValue.Append(val);
                }
            }
        }

        // 提交最后一个 key
        if (currentKey is not null)
            ApplyKeyValue(manifest, currentKey, currentValue.ToString().TrimEnd());

        return string.IsNullOrWhiteSpace(manifest.Name) ? null : manifest;
    }

    private static void ApplyKeyValue(SkillManifest manifest, string key, string value)
    {
        switch (key)
        {
            case "name":
                manifest.Name = value;
                break;
            case "description":
                manifest.Description = value;
                break;
            case "trigger_tools":
                manifest.TriggerTools = ParseStringList(value);
                break;
            case "always_active":
                manifest.AlwaysActive = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "priority":
                if (int.TryParse(value, out var p)) manifest.Priority = p;
                break;
            case "context":
                manifest.Context = value;
                break;
            case "allowed_tools":
                manifest.AllowedTools = ParseStringList(value);
                break;
        }
    }

    /// <summary>解析字符串列表：支持 [a, b] 和 a, b 两种格式。</summary>
    private static HashSet<string> ParseStringList(string value)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var v = value.Trim('[', ']', ' ');
        if (string.IsNullOrEmpty(v)) return result;
        foreach (var item in v.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = item.Trim().Trim('"', '\'');
            if (!string.IsNullOrEmpty(trimmed))
                result.Add(trimmed);
        }
        return result;
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