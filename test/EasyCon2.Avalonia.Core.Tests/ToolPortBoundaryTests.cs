using EasyCon.Core.LLM.Agent.Tools;
using EasyCon2.Avalonia.AiAgent.Tools;
using EasyCon2.Avalonia.Services;
using System.Reflection;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// 工具端口边界检验器（runtime knows capabilities, not integrations）：
/// 1. Core 程序集内的 IAiTool 实现，构造函数只允许依赖 Core 词汇（能力端口、
///    LLM 基础设施、基元、委托供给）——宿主集成类型出现在签名里即失败；
/// 2. 任何程序集的 IAiTool 实现都不得依赖 IToolCallService 组合视图——
///    工具必须依赖窄端口，防止 god 接口回潮。
/// </summary>
[TestFixture]
public class ToolPortBoundaryTests
{
    private static IEnumerable<Type> ToolTypes()
    {
        var core = typeof(EvalEcsTool).Assembly;
        var gui = typeof(DefaultTools).Assembly;
        foreach (var t in core.GetTypes().Concat(gui.GetTypes()))
            if (typeof(IAiTool).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
                yield return t;
    }

    /// <summary>Core 工具允许的依赖词汇：基元/枚举/字符串、委托（Func 供给与回调端口）、Core 自有类型。</summary>
    private static bool IsCoreVocabulary(Type t) =>
        t.IsPrimitive
        || t.IsEnum
        || t == typeof(string)
        || t == typeof(object)
        || typeof(Delegate).IsAssignableFrom(t)
        || (t.Namespace?.StartsWith("EasyCon.Core", StringComparison.Ordinal) ?? false);

    [Test]
    public void CoreTools_ConstructorDependsOnlyOnCoreVocabulary()
    {
        var violations = new List<string>();
        foreach (var tool in ToolTypes().Where(t => t.Assembly == typeof(EvalEcsTool).Assembly))
        {
            foreach (var ctor in tool.GetConstructors())
                foreach (var param in ctor.GetParameters())
                    if (!IsCoreVocabulary(param.ParameterType))
                        violations.Add($"{tool.Name}({param.ParameterType.Name} {param.Name})");
        }

        Assert.That(violations, Is.Empty,
            "Core 工具构造函数出现了 Core 词汇表之外的依赖（宿主集成类型）：\n" + string.Join("\n", violations));
    }

    [Test]
    public void NoTool_DependsOnTheToolCallServiceFacade()
    {
        var violations = new List<string>();
        foreach (var tool in ToolTypes())
        {
            foreach (var ctor in tool.GetConstructors())
                foreach (var param in ctor.GetParameters())
                    if (param.ParameterType == typeof(IToolCallService))
                        violations.Add($"{tool.Name}({param.ParameterType.Name} {param.Name})");
        }

        Assert.That(violations, Is.Empty,
            "工具依赖了 IToolCallService 组合视图（应改依赖窄端口）：\n" + string.Join("\n", violations));
    }
}