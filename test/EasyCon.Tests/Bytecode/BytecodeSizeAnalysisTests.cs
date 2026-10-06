using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Ssa;
using System.Text;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 产物体积剖析（docs/SingleStreamFormat.md §4 N5 → ECX1 平铺）：以例程「光速过帧」为基准，
/// 输出平铺镜像逐表字节构成，并核算布局与产物字节一致。
/// 尺寸基线见 baselines/sizes.md；容器化门槛 = 总字节数不劣于基线量级（N1②）。
/// </summary>
[TestFixture]
public class BytecodeSizeAnalysisTests
{
    [Test]
    public void Guangshu_Size_Breakdown()
    {
        var examplePath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "examples", "光速过帧v1.4精准版.txt");
        if (!File.Exists(examplePath))
            Assert.Ignore("例程文件不存在");
        var source = File.ReadAllText(examplePath);
        var sourceBytes = Encoding.UTF8.GetByteCount(source);

        var result = Compilation.CompileFile(examplePath, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty,
            string.Join("\n", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var image = result.Image!;

        var ecx = EcsContainer.WriteImage(image);
        var stripped = EcsContainer.WriteImage(image, stripDebug: true);
        var ecm = EcsContainer.WriteModule(result.Artifacts.Single(a => a.HasEval));

        // ---- ECX1 平铺逐表核算（36B 头 + 主模块名 + 自计数表序 + .text + 可选调试块）----
        var tables = WalkFlatImage(ecx);
        int accounted = 36 + tables.Sum(t => t.Bytes);
        int codeBytes = tables.Where(t => t.Name == ".text").Sum(t => t.Bytes);
        int instructionCount = image.Functions.Sum(f => f.Instructions.Count);

        var sb = new StringBuilder();
        sb.AppendLine($"源文件: {sourceBytes} B (UTF-8, {source.Length} 字符)");
        sb.AppendLine($"ECX1 总计: {ecx.Length} B (stripDebug {stripped.Length} B) (平铺核算一致: {accounted == ecx.Length})");
        foreach (var (name, bytes) in tables)
            sb.AppendLine($"  {name,-10} {bytes,5} B");
        sb.AppendLine($"  代码区计     {codeBytes,5} B  ({instructionCount} 条指令, v3 定长)");
        sb.AppendLine($"  资源需求    max_slots={image.MaxSlots}, max_depth={image.MaxDepth}");
        sb.AppendLine($"  静态极值    {EcsContainer.ComputeStaticExtremes(image.Functions, image.Consts, image.Structs)}");

        foreach (var f in image.Functions)
        {
            var bytes = InstructionCodec.Project(f.Instructions);
            sb.AppendLine($"  func \"{f.Name}\": {f.Instructions.Count} 条 = {bytes.Length} B (定长), slots={f.NSlots}, params={f.NParams}");
            var hist = new SortedDictionary<string, int>();
            foreach (var ins in f.Instructions)
            {
                var key = ins.Op + (ins.HasExt ? "(+4B ext)" : "");
                hist[key] = hist.GetValueOrDefault(key) + 1;
            }
            foreach (var kv in hist.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"      {kv.Key,-24} ×{kv.Value,-4}");
        }
        sb.AppendLine($"ECM (main 模块, 编译缓存, ECSC kind=module): {ecm.Length} B");

        TestContext.Out.Write(sb.ToString());

        Assert.That(ecx.Length, Is.LessThan(2048), "光速过帧镜像应小于 2KB");
        Assert.That(ecx.Length, Is.EqualTo(accounted), "ECX1 平铺逐表核算与产物字节一致");
        Assert.That(stripped.Length, Is.LessThanOrEqualTo(560),
            "MCU 发布镜像（stripDebug）总字节数不劣于冻结格式基线量级（N1②；ECSC 基线 532 B 含调试区，ECX1 平铺头 ~40 B 应显著更小）");
    }

    /// <summary>
    /// MCU 发布版剥离调试块（stripDebug）：平铺语义 = 去掉尾部调试块 + 头内 dbgSize 清零 +
    /// flags.D 清零 + CRC 重封，其余字节逐字节不变（EcmEcxFormat.md stripDebug 语义）。
    /// </summary>
    [Test]
    public void Guangshu_DebugStrip_CoreUnchangedAndSmaller()
    {
        var examplePath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "examples", "光速过帧v1.4精准版.txt");
        if (!File.Exists(examplePath))
            Assert.Ignore("例程文件不存在");
        var result = Compilation.CompileFile(examplePath, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty);
        var image = result.Image!;

        var full = EcsContainer.WriteImage(image);
        var stripped = EcsContainer.WriteImage(image, stripDebug: true);

        int dbgSize = (int)BitConverter.ToUInt32(full, 20);
        Assert.That(dbgSize, Is.GreaterThan(0), "缺省写入含调试块");
        Assert.That(full[12] & EcsContainer.HeaderFlagDebug, Is.Not.EqualTo(0), "flags.D 置位");

        var expected = new byte[full.Length - dbgSize];
        Array.Copy(full, expected, expected.Length);
        BitConverter.GetBytes(0u).CopyTo(expected, 20);                                 // dbgSize 清零
        expected[12] &= unchecked((byte)~EcsContainer.HeaderFlagDebug);                 // flags.D 清零
        EcsContainer.ResealCrc32(expected);

        Assert.That(stripped, Is.EqualTo(expected), "stripDebug = 去调试块 + dbgSize/flags.D 清零 + CRC 重封，其余逐字节不变");
        Assert.That(stripped.Length, Is.LessThan(full.Length), "MCU 发布版应更小");
        Assert.DoesNotThrow(() => EcsContainer.ReadImage(stripped), "剥离后镜像仍可加载");
    }

    /// <summary>v3 定长密度实测：4B/8B 指令分布与每条指令平均字节数（基线记录性指标，baselines/perf.md）。</summary>
    [Test]
    public void Fixed_Density_CorpusAndExamples()
    {
        int fourByte = 0, eightByte = 0, instructions = 0, totalBytes = 0;
        foreach (var path in CorpusSources())
        {
            var result = Compilation.CompileFile(path, new CompileOptions { UseDiskCache = false });
            if (result.Image == null)
                continue;
            foreach (var f in result.Image.Functions)
            {
                var bytes = InstructionCodec.Project(f.Instructions);
                totalBytes += bytes.Length;
                instructions += f.Instructions.Count;
                foreach (var ins in f.Instructions)
                {
                    if (EcsFormat.WordCount(ins.Op) == 1) fourByte++; else eightByte++;
                }
            }
        }
        TestContext.Out.WriteLine(
            $"v3 定长密度: {instructions} 条指令, {totalBytes} B 代码流, 平均 {totalBytes / Math.Max(1, instructions):F2} B/指令；" +
            $"4B 指令占 {100.0 * fourByte / Math.Max(1, fourByte + eightByte):F1}%（{fourByte}/{fourByte + eightByte}，8B={eightByte}）");
        Assert.That(instructions, Is.GreaterThan(0));
        Assert.That(totalBytes, Is.GreaterThan(0));
        // 定长下界：4B/条；含数据字指令 8B/条——密度记录进 baselines/perf.md
    }

    internal static string[] CorpusSources()
    {
        var corpusDir = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "test", "EasyCon.Tests", "Bytecode", "corpus");
        if (Directory.Exists(corpusDir))
            return Directory.GetFiles(corpusDir, "*.ecs");
        return [];
    }

    /// <summary>
    /// ECX1 平铺镜像逐表核算（规格：EcmEcxFormat.md；实现 = EcsContainer.WriteImage）。
    /// 布局：36B 头 + 主模块名 + 自计数表序（consts/structs/globals/natives/funcs）+ .text + 可选 dbg。
    /// </summary>
    internal static List<(string Name, int Bytes)> WalkFlatImage(byte[] b)
    {
        int pos;
        int U16() { int v = BitConverter.ToUInt16(b, pos); pos += 2; return v; }
        int U32() { int v = (int)BitConverter.ToUInt32(b, pos); pos += 4; return v; }
        int U8() { return b[pos++]; }
        int Utf8() { return 2 + U16(); }   // u16 len + 内容（pos 越过内容）

        var tables = new List<(string, int)>();
        pos = 36;
        int nameLen = b[13];
        tables.Add(("name", nameLen));
        pos += nameLen;

        int start = pos;
        int constCount = U32();
        for (int i = 0; i < constCount; i++)
        {
            var tag = b[pos];
            pos += 1;
            switch (tag)
            {
                case 3: case 4: pos += 4; break;            // EcsTag.Int/UInt = i32
                case 5: case 6: case 10: pos += 8; break;   // EcsTag.UInt64/Double/Ptr = 64 位
                case 7: { int units = U16(); pos += units * 2; break; }   // EcsTag.String
                default: throw new InvalidOperationException($"常量标签非法 {tag}");
            }
        }
        tables.Add(("consts", pos - start));

        start = pos;
        int structCount = U32();
        for (int i = 0; i < structCount; i++)
        {
            pos += Utf8();                       // 结构体名
            int nfields = U8();
            for (int f = 0; f < nfields; f++)
            {
                pos += Utf8();                   // 字段名
                pos += 3 + 2;                    // kind/type/elem u8×3 + ext u16
            }
        }
        tables.Add(("structs", pos - start));

        start = pos;
        int globalCount = U32();
        pos += globalCount * 2;                  // module_idx u8 + type u8
        tables.Add(("globals", pos - start));

        start = pos;
        int nativeCount = U32();
        for (int i = 0; i < nativeCount; i++)
            pos += Utf8();
        tables.Add(("natives", pos - start));

        start = pos;
        int funcCount = U32();
        if (funcCount != BitConverter.ToUInt16(b, 14))
            throw new InvalidOperationException("函数表计数与头部不一致");
        pos += funcCount * 6;                    // nslots u16 + code_off u32
        tables.Add(("funcs", pos - start));

        int codeSize = (int)BitConverter.ToUInt32(b, 16);
        tables.Add((".text", codeSize));
        pos += codeSize;

        int dbgSize = (int)BitConverter.ToUInt32(b, 20);
        if (dbgSize > 0)
        {
            tables.Add(("dbg", dbgSize));
            pos += dbgSize;
        }
        return tables;
    }
}
