#nullable enable
using EasyCon.Script;
using System.Collections.Immutable;

namespace EasyCon.Core.Hosting;

/// <summary>
/// GUI 和 CLI 共用的内存编译选项。执行与离线导出消费同一份完整操作数指令计划。
/// </summary>
public static class ScriptCompileProfiles
{
    /// <summary>
    /// 编译计划保留在进程内，可复用模块缓存。
    /// </summary>
    /// <param name="extVars">外部变量名集合（识图标签名等），null = 无。</param>
    /// <param name="optimize">false 时跳过 SSA 优化（IR 观测用）。</param>
    public static CompileOptions Interactive(IEnumerable<string>? extVars = null, bool optimize = true) => new()
    {
        ExtVars = ToSet(extVars),
        UseDiskCache = false,
        UseProcessCache = true,
        Optimize = optimize,
    };

    static ImmutableHashSet<string>? ToSet(IEnumerable<string>? extVars)
        => extVars is null ? null : [.. extVars];
}