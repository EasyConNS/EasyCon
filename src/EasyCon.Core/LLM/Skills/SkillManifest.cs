namespace EasyCon.Core.LLM.Skills;

/// <summary>
/// Skill 元数据，对应 SKILL.md 的 YAML frontmatter。
/// 这是渐进式披露的 Level 1：始终在上下文中（仅 name + description），
/// 模型据此判断是否需要加载完整指令。
/// </summary>
public sealed class SkillManifest
{
    /// <summary>技能唯一标识，lowercase-hyphen。</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 触发核心：包含"做什么 + 何时用 + 触发词"。
    /// 模型根据此文本判断是否激活技能。应尽量全面描述触发场景。
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 关联的工具名列表。当这些工具被调用时，技能自动激活。
    /// 可选，留空表示仅靠 description 语义匹配触发。
    /// </summary>
    public HashSet<string> TriggerTools { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 是否常驻（始终激活），如默认角色 prompt。
    /// 默认 false，表示按需激活。
    /// </summary>
    public bool AlwaysActive { get; set; }

    /// <summary>
    /// 同名/同触发场景下的排序权重，数值越小优先级越高。默认 100。
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// 执行上下文。"fork" 表示独立子 Agent 执行；空或其它值表示 inline（注入主 prompt）。
    /// </summary>
    public string Context { get; set; } = "";

    /// <summary>
    /// 子 Agent 模式下允许使用的工具集合。null 或空表示允许全部工具。
    /// </summary>
    public HashSet<string> AllowedTools { get; set; } = new(StringComparer.Ordinal);
}