#nullable enable
using EasyCon.Script;
using System.Collections.Immutable;

namespace EasyCon.Core.Hosting;

/// <summary>
/// 宿主编译档位（组合根的一部分）：把「选项组合 → 编译行为」的对应关系集中一处，
/// 避免 GUI / CLI 各自手写 <see cref="CompileOptions"/> 字面量而悄悄漂移。
///
/// <para>
/// PC 与 MCU 共用语言语义和完整操作数指令表示，但不共用编码容量限制。
/// Interactive 生成 PC 内存执行计划；Distributable 生成可序列化的 ECX。
/// 两种目标进入不同缓存键，PC 扩展模块不写入 ECM，避免混入 MCU 分发链。
/// </para>
/// <list type="bullet">
/// <item><see cref="Interactive"/>：PC 现编，不落盘；可编码的小模块仍可使用进程缓存。</item>
/// <item><see cref="Distributable"/>：obj/ 内容寻址缓存——compile/分发产物路径，重复编译零成本。</item>
/// </list>
/// </summary>
public static class ScriptCompileProfiles
{
    /// <summary>
    /// 交互现编档：不落盘（进程缓存兜底）。
    /// </summary>
    /// <param name="extVars">外部变量名集合（识图标签名等），null = 无。</param>
    /// <param name="optimize">false 时跳过 SSA 优化（IR 观测用）。</param>
    public static CompileOptions Interactive(IEnumerable<string>? extVars = null, bool optimize = true) => new()
    {
        Target = ScriptTarget.Desktop,
        ExtVars = ToSet(extVars),
        UseDiskCache = false,
        UseProcessCache = true,
        Optimize = optimize,
    };

    /// <summary>
    /// 分发产物档：obj/ 内容寻址缓存；产物可落 .ecx 交 C VM 执行。
    /// </summary>
    /// <param name="extVars">外部变量名集合（识图标签名等），null = 无。</param>
    public static CompileOptions Distributable(IEnumerable<string>? extVars = null) => new()
    {
        Target = ScriptTarget.Portable,
        ExtVars = ToSet(extVars),
        UseDiskCache = true,
        UseProcessCache = true,
    };

    static ImmutableHashSet<string>? ToSet(IEnumerable<string>? extVars)
        => extVars is null ? null : [.. extVars];
}