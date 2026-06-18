namespace EasyCon.Core.LLM.Skills;

/// <summary>
/// Skill 注册中心，类比 <c>ToolRegistry</c>。
/// 管理已加载技能，提供按名称查找、激活评估与索引导出。
/// </summary>
public sealed class SkillRegistry
{
    private readonly Dictionary<string, Skill> _byName = new(StringComparer.Ordinal);
    private readonly List<Skill> _ordered = new();

    /// <summary>注册一个技能。同名覆盖（保留插入顺序）。</summary>
    public SkillRegistry Register(Skill skill)
    {
        _byName[skill.Manifest.Name] = skill;
        RebuildOrdered();
        return this;
    }

    /// <summary>按名称注销。不存在时静默忽略。</summary>
    public void Unregister(string name)
    {
        if (_byName.Remove(name)) RebuildOrdered();
    }

    /// <summary>是否注册了指定名称的技能。</summary>
    public bool Contains(string name) => _byName.ContainsKey(name);

    /// <summary>按名称获取技能。</summary>
    public Skill? Get(string name) =>
        _byName.TryGetValue(name, out var skill) ? skill : null;

    /// <summary>所有已注册技能（按 Priority 升序）。</summary>
    public IReadOnlyList<Skill> All => _ordered;

    /// <summary>常驻技能（<see cref="SkillManifest.AlwaysActive"/> 为 true）。</summary>
    public IEnumerable<Skill> AlwaysActive => _ordered.Where(s => s.Manifest.AlwaysActive);

    /// <summary>清空所有注册。</summary>
    public void Clear()
    {
        _byName.Clear();
        _ordered.Clear();
    }

    /// <summary>
    /// 根据上下文评估应激活的技能。
    /// 激活条件（OR）：
    /// 1. 始终激活（<see cref="SkillManifest.AlwaysActive"/>）
    /// 2. <see cref="SkillManifest.TriggerTools"/> 命中本轮已用工具
    /// 3. description 关键词命中最近用户消息
    /// </summary>
    public IEnumerable<Skill> EvaluateActive(SkillTriggerContext ctx)
    {
        foreach (var skill in _ordered)
        {
            if (skill.Manifest.AlwaysActive) { yield return skill; continue; }
            if (IsTriggeredByTools(skill, ctx.RecentTools)) { yield return skill; continue; }
            if (IsTriggeredByDescription(skill, ctx.UserMessage)) yield return skill;
        }
    }

    private static bool IsTriggeredByTools(Skill skill, IReadOnlyCollection<string> recentTools)
    {
        if (skill.Manifest.TriggerTools.Count == 0) return false;
        foreach (var t in skill.Manifest.TriggerTools)
            if (recentTools.Contains(t)) return true;
        return false;
    }

    private static bool IsTriggeredByDescription(Skill skill, string userMessage)
    {
        var triggers = SkillTriggerKeywords.For(skill.Manifest.Name);
        if (triggers.Length == 0) return false;
        var msg = (userMessage ?? "").ToLowerInvariant();
        foreach (var k in triggers)
            if (msg.Contains(k, StringComparison.Ordinal)) return true;
        return false;
    }

    private void RebuildOrdered()
    {
        _ordered.Clear();
        _ordered.AddRange(_byName.Values
            .OrderBy(s => s.Manifest.Priority)
            .ThenBy(s => s.Manifest.Name, StringComparer.Ordinal));
    }
}