using System.IO;

namespace EasyCon.Core.LLM.Skills;

/// <summary>
/// 已加载的 Skill，封装元数据与懒加载的指令/资源。
/// 实现 Claude Agent Skills 的三级渐进式披露：
/// <list type="number">
///   <item>Level 1 - 元数据（Name + Description）：始终在上下文</item>
///   <item>Level 2 - Body（SKILL.md 正文）：首次访问时读取</item>
///   <item>Level 3 - references/ 等资源：按需显式拉取</item>
/// </list>
/// 支持两种来源：
/// <list type="bullet">
///   <item>文件系统技能：通过 <see cref="DirectoryPath"/> 懒加载</item>
///   <item>内置技能：通过构造函数直接注入内容，不走文件 IO</item>
/// </list>
/// </summary>
public sealed class Skill
{
    public SkillManifest Manifest { get; init; } = new();

    /// <summary>技能所在目录的绝对路径（文件系统技能）。内置技能为空。</summary>
    public string DirectoryPath { get; init; } = "";

    // ── 内置技能内容（不走文件 IO）──────────────────────────

    private readonly string? _contentBody;
    private readonly IReadOnlyDictionary<string, string>? _contentReferences;

    /// <summary>创建内置技能（内容直接注入，不走文件系统）。</summary>
    public Skill(SkillManifest manifest, string body, IReadOnlyDictionary<string, string>? references = null)
    {
        Manifest = manifest;
        _contentBody = body;
        _contentReferences = references;
    }

    // 保留无参构造函数供文件系统技能和集合初始化器使用
    /// <summary>创建文件系统技能（使用 <see cref="DirectoryPath"/> 懒加载）。</summary>
    public Skill() { }

    // ── Level 2：完整指令，懒加载 ──────────────────────────

    private string? _body;
    private readonly object _bodyLock = new();

    /// <summary>
    /// SKILL.md 正文（去掉 frontmatter 后的 Markdown）。
    /// 内置技能直接返回注入内容；文件系统技能首次访问时从文件读取并缓存。
    /// </summary>
    public string Body
    {
        get
        {
            if (_contentBody is not null) return _contentBody;
            lock (_bodyLock)
            {
                _body ??= SkillLoader.ReadBody(DirectoryPath);
                return _body;
            }
        }
    }

    /// <summary>重置懒加载缓存（用于文件变更后重新加载）。内置技能无操作。</summary>
    public void InvalidateCache()
    {
        if (_contentBody is not null) return;
        lock (_bodyLock) { _body = null; }
    }

    // ── Level 3：资源访问 ──────────────────────────────────

    /// <summary>
    /// 读取 references/ 下的参考文档。找不到时返回 null。
    /// </summary>
    public string? ReadReference(string relativePath)
    {
        if (_contentReferences is not null)
            return _contentReferences.TryGetValue(relativePath, out var content) ? content : null;
        return SkillLoader.TryReadResource(DirectoryPath, "references", relativePath);
    }

    /// <summary>
    /// 读取 assets/ 下的资源文件路径（不读入内存，供工具直接使用）。
    /// 内置技能无文件系统路径，始终返回 null。
    /// </summary>
    public string? ResolveAsset(string relativePath)
    {
        if (_contentReferences is not null) return null;
        return SkillLoader.TryResolveResource(DirectoryPath, "assets", relativePath);
    }

    /// <summary>
    /// 列举 references/ 目录下的文件相对路径。目录不存在时返回空。
    /// </summary>
    public IReadOnlyList<string> ListReferences()
    {
        if (_contentReferences is not null)
            return _contentReferences.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        return SkillLoader.ListResources(DirectoryPath, "references");
    }

    /// <summary>
    /// 是否应在给定上下文下激活。
    /// 激活条件（满足任一）：
    /// 1. <see cref="SkillManifest.AlwaysActive"/> 为 true
    /// 2. 任意 <see cref="SkillManifest.TriggerTools"/> 出现在 recentTools 中
    /// 3. description 中的触发词命中 userMessage（由触发评估器实现，此处仅做工具匹配）
    /// </summary>
    public bool IsTriggeredByTools(IReadOnlyCollection<string> recentTools)
    {
        if (Manifest.AlwaysActive) return true;
        if (Manifest.TriggerTools.Count == 0) return false;
        foreach (var t in Manifest.TriggerTools)
            if (recentTools.Contains(t)) return true;
        return false;
    }

    public override string ToString() => $"Skill({Manifest.Name})";
}