using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Tests.Support;

namespace EasyCon.Tests;

/// <summary>
/// 全局提升（SsaGlobalPromotion）回归测试。
///
/// Bug 复现：IF 分支内以另一全局的裸 LoadGlobal 作为 store 值（如 break 写法 $i = $N，
/// 生成 StoreGlobal($i, LoadGlobal($N))）时，PromoteFunctionLocals 预计算的块出口定值
/// 快照持有旧 Arg0；$N 的 LoadGlobal 被提升删除后，合流块的 φ 臂仍引用已删除值，
/// 触发 "phi input references missing vN:LoadGlobal" 校验失败。
/// 修复：exitDef 存 store 指令本身，Arg0 惰性解引用。
/// </summary>
[TestFixture]
public sealed class SsaGlobalPromotionRegressionTests
{
    private const string BreakViaGlobalScript = @"
$N = 5
$i = 0
$seen = 0
WHILE $i < $N
    IF $i == 2
        $i = $N
    ENDIF
    $seen = $seen + 1
    $i = $i + 1
END
RETURN $seen * 10 + $i
";

    /// <summary>
    /// 循环内 IF 分支 $i = $N（另一全局的裸 LoadGlobal 作为 store 值）后编译不再报
    /// SSA 校验失败，且执行语义正确：i=0,1,2 三轮，第 2 轮 break（$i 先置 5 再自增 6），
    /// 返回 3*10+6 = 36。
    /// </summary>
    [Test]
    public void GlobalPromotion_StoreOfGlobalFromLoadGlobal_CompilesAndRuns()
    {
        var result = Compilation.CompileSource(BreakViaGlobalScript, new CompileOptions
        {
            Optimize = true,
            UseDiskCache = false,
        });

        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
            "编译诊断不应有错误: " + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        Assert.That(result.Image, Is.Not.Null);

        var output = new MockOutputAdapter();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var value = EcxVm.Run(result.Image!, EcsTestHost.Capabilities(output), cts.Token,
            [], result.NativeSymbols);
        Assert.That(value.AsInt(), Is.EqualTo(36));
    }
}