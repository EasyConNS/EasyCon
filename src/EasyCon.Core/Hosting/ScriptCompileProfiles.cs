#nullable enable
using EasyCon.Script;
using System.Collections.Immutable;

namespace EasyCon.Core.Hosting;

/// <summary>
/// 宿主编译档位（组合根的一部分）：把「选项组合 → 目标产物形态」的对应关系集中一处，
/// 避免 GUI / CLI 各自手写 <see cref="CompileOptions"/> 字面量而悄悄漂移。
///
/// <para>
/// 两档的产物能力有本质区别，不可互换：
/// <list type="bullet">
/// <item><see cref="Desktop"/>：允许 PC 专用宽槽位指令，只供桌面 <c>EcxInterpreter</c> 执行。
/// 产物含 <c>PcCode</c> 时 <c>EcxWriter.Write</c> / <c>EcmFormat.Write</c> 会**主动抛异常**拒绝序列化，
/// 因此该档产物不可下发 MCU、也不进 obj/ 缓存（缓存只服务可序列化产物）。</item>
/// <item><see cref="Portable"/>：冻结的 8 位槽位格式，产物可落 .ecx 交 C VM 执行，
/// 也是唯一启用 obj/ 内容寻址缓存的档位（见 docs/McuBytecodeDelivery.md）。</item>
/// </list>
/// </para>
/// </summary>
public static class ScriptCompileProfiles
{
    /// <summary>
    /// 桌面解释器档：PC 宽槽位 + 现编（不落盘）。
    /// </summary>
    /// <param name="extVars">外部变量名集合（识图标签名等），null = 无。</param>
    /// <param name="optimize">false 时跳过 SSA 优化（IR 观测用）。</param>
    public static CompileOptions Desktop(IEnumerable<string>? extVars = null, bool optimize = true) => new()
    {
        ExtVars = ToSet(extVars),
        UseDiskCache = false,
        UseProcessCache = false,
        EnablePcWideSlots = true,
        Optimize = optimize,
    };

    /// <summary>
    /// 便携/单片机档：冻结 8 位槽位 + obj/ 内容寻址缓存。
    /// 超出 255 槽位的函数在此档**编译期响亮失败**（而非静默截断），这是唯一可分发档位。
    /// </summary>
    /// <param name="extVars">外部变量名集合（识图标签名等），null = 无。</param>
    public static CompileOptions Portable(IEnumerable<string>? extVars = null) => new()
    {
        ExtVars = ToSet(extVars),
        UseDiskCache = true,
        UseProcessCache = true,
        EnablePcWideSlots = false,
    };

    static ImmutableHashSet<string>? ToSet(IEnumerable<string>? extVars)
        => extVars is null ? null : [.. extVars];
}