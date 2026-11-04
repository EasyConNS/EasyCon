using EasyCon.Script;
using EasyCon.Script.Bytecode;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 静态极值进产物（docs/ZeroAllocVm.md §4.1 / P3b）：ECSC manifest（模块产物）与 ECX1 平铺头
/// （镜像产物，u16×3@24）记录 max_literal_string_units / max_literal_array_elems / max_struct_slots
/// 三个「关于程序自身的事实」——只记录不判定（R-3：容量属宿主档案，预检属 McuBytecodeDelivery）。
/// 预检规则（§8）：max_struct_slots×16+24 ≤ obj_block_bytes 等，由烧录前判定消费。
/// </summary>
[TestFixture]
public class StaticExtremesTests
{
    [Test]
    public void Header_Extremes_MeasureProgramFacts()
    {
        const string source = """
            STRUCT P
                $a:INT
                $b:INT
                $c:INT
                $d:STRING
                $e:INT
                $f:INT
            END
            _long = "0123456789ABCDEF"
            $arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
            $p = P{}
            $s = _long
            $z = $arr[0] + $p.a
            PRINT $z
            PRINT $s
            """;

        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false, UseProcessCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var image = result.Image!;
        var artifact = result.Artifacts.Single(a => a.HasEval);

        var expected = EcsContainer.ComputeStaticExtremes(image.Functions, image.Consts, image.Structs);
        Assert.That(expected.MaxLiteralStringUnits, Is.GreaterThanOrEqualTo(16), "字面量串极值 ≥ _long 的 16 units");
        Assert.That(expected.MaxLiteralArrayElems, Is.GreaterThanOrEqualTo(10), "字面量数组极值 ≥ 10 元素");
        Assert.That(expected.MaxStructSlots, Is.GreaterThanOrEqualTo(6), "结构体槽数极值 ≥ P 的 6 槽");

        // 极值记录与内存计算一致（写侧读侧对账；模块产物 = ECM1 平铺头，极值 u16×3@24 与镜像同位）
        var bytes = EcsContainer.WriteModule(artifact);
        var (maxStr, maxArr, maxSt) = ReadModuleHeaderExtremes(bytes);
        Assert.That(maxStr, Is.EqualTo((ushort)Math.Min(expected.MaxLiteralStringUnits, 0xFFFF)));
        Assert.That(maxArr, Is.EqualTo((ushort)Math.Min(expected.MaxLiteralArrayElems, 0xFFFF)));
        Assert.That(maxSt, Is.EqualTo((ushort)Math.Min(expected.MaxStructSlots, 0xFFFF)));
    }

    [Test]
    public void Extremes_PresentInImageKind_Too()
    {
        var result = Compilation.CompileSource("$x = \"hello\"\nPRINT $x\n", new CompileOptions { UseDiskCache = false, UseProcessCache = false });
        var bytes = EcsContainer.WriteImage(result.Image!);
        var (maxStr, _, _) = ReadImageHeaderExtremes(bytes);
        Assert.That(maxStr, Is.GreaterThanOrEqualTo(5), "镜像平铺头同样携带静态极值");
    }

    /// <summary>ECX1 平铺头（36B）：magic u32@0 … 极值 u16×3@24（maxStr/maxArr/maxSt）+ rsvd u16@30 + crc u32@32。</summary>
    static (int MaxStr, int MaxArr, int MaxSt) ReadImageHeaderExtremes(byte[] bytes)
    {
        Assert.That(BitConverter.ToUInt32(bytes, 0), Is.EqualTo(EcsContainer.FlatMagic), "ECX1 平铺镜像");
        return (
            BitConverter.ToUInt16(bytes, 24),
            BitConverter.ToUInt16(bytes, 26),
            BitConverter.ToUInt16(bytes, 28));
    }

    /// <summary>ECM1 平铺头（36B）：极值 u16×3@24 与 ECX1 同位。</summary>
    static (int MaxStr, int MaxArr, int MaxSt) ReadModuleHeaderExtremes(byte[] bytes)
    {
        Assert.That(BitConverter.ToUInt32(bytes, 0), Is.EqualTo(EcsContainer.ModuleFlatMagic), "ECM1 平铺模块缓存");
        return (
            BitConverter.ToUInt16(bytes, 24),
            BitConverter.ToUInt16(bytes, 26),
            BitConverter.ToUInt16(bytes, 28));
    }
}