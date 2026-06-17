namespace EasyCon.Core.LLM.Skills;

/// <summary>
/// 内置技能的触发关键词表。
/// 用于 description 语义匹配的 fallback：当工具未触发时，根据用户消息关键词激活。
/// 新增内置技能时在此登记其关键词（小写）。自定义技能可通过 SKILL.md 的
/// description 字段由 PromptAssembler 注入提示，不依赖此表。
/// </summary>
internal static class SkillTriggerKeywords
{
    /// <summary>按技能名返回触发关键词数组。未登记的技能返回空数组。</summary>
    public static string[] For(string skillName) => skillName switch
    {
        "ecscript-authoring" =>
        [
            "脚本", "script", "ecs", "语法", "代码", "编写", "函数", "变量", "循环", "编译", "格式化"
        ],
        "device-control" =>
        [
            "运行", "执行", "自动", "跑脚本", "运行脚本", "设备", "连接", "单片机", "手柄"
        ],
        "vision-analysis" =>
        [
            "画面", "屏幕", "截图", "看看", "识别", "看到", "图像", "视觉", "观察"
        ],
        "core" => Array.Empty<string>(), // always_active，不靠关键词
        _ => Array.Empty<string>()
    };
}
