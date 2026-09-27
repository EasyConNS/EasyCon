namespace EasyCon2.Avalonia.Core.Localization;

/// <summary>
/// 本地化桥：Core 层 VM 通过本桥取译文而不引用任何 UI 框架类型。
/// 宿主启动时把具体实现接到 Resolver（App 侧 L10n.T，基于 Avalonia 资源字典）；
/// 未接宿主（单元测试）时原样返回键名，便于发现遗漏。
/// </summary>
public static class L10nBridge
{
    /// <summary>宿主注入的译文解析器；null = 未接宿主。</summary>
    public static Func<string, string>? Resolver;

    public static string T(string key) => Resolver?.Invoke(key) ?? key;
}