#nullable enable
using EasyCon.Script;
using System.Collections.Immutable;

namespace EasyCon.Core.Hosting;

/// <summary>
/// 宿主编译档位（组合根的一部分）：把「选项组合 → 编译行为」的对应关系集中一处，
/// 避免 GUI / CLI 各自手写 <see cref="CompileOptions"/> 字面量而悄悄漂移。
///
/// <para>
/// 单流化后（docs/SingleStreamFormat.md §2（R-5 档位塌缩））：所有产物都是同一种
/// 同一产物形态（ECX1 平铺），**没有宽窄之分**——原 Desktop/Portable 的格式维度随 <c>EnablePcWideSlots</c>
/// 一起删除，varint 基流让 >255 槽位的函数在全部目标下可编译、可序列化、可缓存。
/// 「能不能装进设备」是资源问题，由烧录前的容量预检判定（McuTargetProfile），
/// 不再由编译档位表达。剩余的两档只是**缓存策略**（与产物内容无关，不进缓存键）：
/// </para>
/// <list type="bullet">
/// <item><see cref="Interactive"/>：现编（不落盘，进程级缓存兜底）——run/编辑器等高频重编路径。</item>
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
        ExtVars = ToSet(extVars),
        UseDiskCache = true,
        UseProcessCache = true,
    };

    static ImmutableHashSet<string>? ToSet(IEnumerable<string>? extVars)
        => extVars is null ? null : [.. extVars];
}
