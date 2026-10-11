using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 回归（循环携带局部变量的 φ 合并/池化槽污染，20a089c 引入）：
/// FOR 循环体内 IF/ELSE 双臂各写一次的局部变量（$prev），下一迭代读回的是污染值——
/// 依赖「同值连续帧去重」的逻辑失效，重复值全部落位。
/// <para>受害者：examples/pokemon-stats.ecs 的 DecodeRow（PP-OCRv5 rec 输出的 CTC 折叠），
/// NetPpocrRealModelTests.PokemonStats_RecAndCompute 因之翻红（EXP 19531 → 195531/1955331）。</para>
/// <para>触发形状：≥3 个用户 FUNC + 循环内 FUNC 调用 + 双臂写同一局部 + 嵌套 IF；
/// 帧序列 1,blank,9,5,5,3,3,1 取自 PP-OCRv5 对 ppocr2.png EXP 行的 argmax dump
/// （'1'=16164 blank=0 '9'=16172 '5'=16168 '3'=16166），正确折叠 = 19531。
/// 20a089c 实测输出 1955331；且行为随同脚本其他 FUNC 的存在翻转（编译上下文相关），
/// 同构造单独可过、合并即错——槽位分配污染的典型特征。</para>
/// <para>修复（归还侧 CFG 后支配守卫 + 取用侧支配守卫）落地后本用例须保持绿；
/// 20a089c 引入缺陷时它以 1955331 红，可作红→绿二分锚点（ECS_NO_ZERO_PHI /
/// ECS_DISABLE_CONST_HOIST 均不影响复现——缺陷在池化结算本身，不在两个扩展）。</para>
/// </summary>
[TestFixture]
public class LoopCarriedLocalRegressionTests
{
    /// <summary>DecodeRow 同构控制流：帧序列硬编码，不依赖模型/截图等外部资产。</summary>
    const string Source = """
        FUNC FrameAt($i): INT
            IF $i == 0
                RETURN 16164
            ENDIF
            IF $i == 1
                RETURN 0
            ENDIF
            IF $i == 2
                RETURN 16172
            ENDIF
            IF $i == 3 OR $i == 4
                RETURN 16168
            ENDIF
            IF $i == 5 OR $i == 6
                RETURN 16166
            ENDIF
            RETURN 16164
        ENDFUNC

        FUNC DigitOf($c): INT
            IF $c == 16164
                RETURN 1
            ENDIF
            IF $c == 16172
                RETURN 9
            ENDIF
            IF $c == 16168
                RETURN 5
            ENDIF
            IF $c == 16166
                RETURN 3
            ENDIF
            RETURN -1
        ENDFUNC

        FUNC Decode(): INT
            $val = 0
            $prev = -1
            FOR $i = 0 TO 7
                $c = FrameAt($i)
                IF $c == 0
                    $prev = -1
                ELSE
                    IF $c != $prev
                        $d = DigitOf($c)
                        IF $d >= 0
                            $val = $val * 10 + $d
                        ENDIF
                    ENDIF
                    $prev = $c
                ENDIF
            END
            RETURN $val
        ENDFUNC

        $r = Decode()
        PRINT $r
        """;

    [Test]
    public void LoopCarriedLocal_WrittenInBothIfElseArms_SurvivesAcrossIterations()
    {
        var result = Compilation.CompileSource(Source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(),
            Is.Empty, "编译诊断：" + string.Join("\n", result.Diagnostics));

        var io = new RecordingIo();
        EcxVm.Run(result.Image!, EcsTestHost.Capabilities(io, new RecordingPad()),
            new CancellationTokenSource().Token, [], result.NativeSymbols);

        // 连续重复帧（5,5 与 3,3）必须折叠：19531；未折叠则输出 1955331
        Assert.That(io.Lines, Is.EqualTo(new[] { "19531" }),
            "循环携带局部 $prev 跨迭代丢失 → 连续重复帧未折叠（CTC 去重失效）");
    }
}