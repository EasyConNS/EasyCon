using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Ssa;
using System.Text;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 产物体积剖析（docs/EcmEcxFormat.md §4）：以例程「光速过帧」为基准，
/// 输出 ECX 逐节字节构成与指令直方图。
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

        var ecx = EcxWriter.Write(image);
        var ecm = EcmFormat.Write(result.Artifacts.Single(a => a.HasEval));

        // ---- 逐节字节（与 EcxWriter 布局一致）----
        int header = 0x24;
        int constPool = image.Consts.Sum(SizeOf);
        int structTable = image.Structs.Sum(s =>
            2 + Encoding.UTF8.GetByteCount(s.Name) + 1 +
            s.Fields.Sum(f => 2 + Encoding.UTF8.GetByteCount(f.Name) + 1 + 1 + 1 + 2));
        int globalTable = image.Globals.Sum(g => 2 + Encoding.UTF8.GetByteCount(g.Name) + 1 + 1);
        int nativeTable = image.Natives.Sum(n => 2 + Encoding.UTF8.GetByteCount(n.Name));
        int funcTable = 11 * image.Functions.Count;
        int codeBytes = 4 * image.Functions.Sum(f => f.Code.Count);
        int debug = image.Functions.Sum(f => 2 + Encoding.UTF8.GetByteCount(f.Name));
        int entry = 4;
        int accounted = header + constPool + structTable + globalTable + nativeTable + funcTable + codeBytes + debug + entry;

        var sb = new StringBuilder();
        sb.AppendLine($"源文件: {sourceBytes} B (UTF-8, {source.Length} 字符)");
        sb.AppendLine($"ECX 总计: {ecx.Length} B (断言与核算一致: {accounted == ecx.Length})");
        sb.AppendLine($"  头部        {header,5} B");
        sb.AppendLine($"  常量池      {constPool,5} B  ({image.Consts.Count} 条)");
        foreach (var c in image.Consts)
            sb.AppendLine($"      tag={c.Tag} {SizeOf(c),3} B  {(c.Tag == EcsTag.String ? "\"" + c.Str + "\"" : c.Int64.ToString())}");
        sb.AppendLine($"  类型表      {structTable,5} B  ({image.Structs.Count} 个)");
        sb.AppendLine($"  全局表      {globalTable,5} B  ({image.Globals.Count} 个: {string.Join(", ", image.Globals.Select(g => g.Name))})");
        sb.AppendLine($"  原生名表    {nativeTable,5} B  ({string.Join(", ", image.Natives.Select(n => n.Name))})");
        sb.AppendLine($"  函数表      {funcTable,5} B  ({image.Functions.Count} × 11 B)");
        sb.AppendLine($"  代码区      {codeBytes,5} B  ({image.Functions.Sum(f => f.Code.Count)} 字指令)");
        sb.AppendLine($"  调试区      {debug,5} B  (函数名表)");
        sb.AppendLine($"  entry       {entry,5} B");
        sb.AppendLine($"  资源需求    max_slots={image.MaxSlots}, max_depth={image.MaxDepth}");
        sb.AppendLine($"  静态 RAM    帧区={image.MaxDepth * image.MaxSlots * 16} B + 全局={image.Globals.Count * 16} B");

        foreach (var f in image.Functions)
        {
            sb.AppendLine($"  func \"{f.Name}\": {f.Code.Count} 字 = {f.Code.Count * 4} B, slots={f.NSlots}, params={f.NParams}");
            var hist = new SortedDictionary<string, int>();
            for (int i = 0; i < f.Code.Count;)
            {
                var op = (EcsOpcode)(f.Code[i] & 0xFF);
                hist[op + (EcsFormat.Get(op) == EcsInsFormat.Ext ? "(8B)" : "")] =
                    hist.GetValueOrDefault(op + (EcsFormat.Get(op) == EcsInsFormat.Ext ? "(8B)" : "")) + 1;
                i += EcsFormat.WordCount(op);
            }
            foreach (var kv in hist.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"      {kv.Key,-16} ×{kv.Value,-4} = {kv.Value * 4} B");
        }
        sb.AppendLine($"ECM (main 模块, 编译缓存): {ecm.Length} B");

        TestContext.Out.Write(sb.ToString());

        Assert.That(ecx.Length, Is.LessThan(2048), "光速过帧镜像应小于 2KB");
        Assert.That(ecx.Length, Is.EqualTo(accounted), "逐节核算与产物字节一致");
    }

    /// <summary>
    /// MCU 发布版剥离调试区（EcxWriter stripDebug）：flags.D=0、debug_count=0，
    /// 核心表与代码区逐字节不变，体积节省 = 函数名表（EcmEcxFormat.md §2.9）。
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

        var full = EcxWriter.Write(image);
        var stripped = EcxWriter.Write(image, stripDebug: true);

        // 体积差 = 调试名表（len 前缀 UTF-8 函数名）
        int debugBytes = image.Functions.Sum(f => 2 + Encoding.UTF8.GetByteCount(f.Name));
        Assert.That(stripped.Length, Is.EqualTo(full.Length - debugBytes), "剥离应恰好省去函数名表");
        Assert.That(stripped.Length, Is.LessThan(full.Length), "MCU 发布版应更小");

        // 头部：D 位清零、debug_count=0，其余一致
        ushort fullFlags = BitConverter.ToUInt16(full, 0x06);
        ushort stripFlags = BitConverter.ToUInt16(stripped, 0x06);
        Assert.That(stripFlags & 0x1, Is.EqualTo(0), "D 位应清零");
        Assert.That(stripFlags & ~0x1, Is.EqualTo(fullFlags & ~0x1), "K/I 标志不变");
        Assert.That(BitConverter.ToUInt32(stripped, 0x20), Is.EqualTo(0), "debug_count 应为 0");

        // 核心节（头部之后 → 代码区末尾）逐字节一致；entry 收尾一致
        int coreLen = full.Length - 0x24 - debugBytes - 4;
        Assert.That(stripped[0x24..(0x24 + coreLen)], Is.EqualTo(full[0x24..(0x24 + coreLen)]), "核心节应逐字节一致");
        Assert.That(stripped[^4..], Is.EqualTo(full[^4..]), "entry 收尾应一致");
    }

    static int SizeOf(EcsConst c) => c.Tag switch
    {
        EcsTag.Int or EcsTag.UInt => 1 + 4,
        EcsTag.UInt64 or EcsTag.Ptr => 1 + 8,
        EcsTag.Double => 1 + 8,
        EcsTag.String => 1 + 2 + Encoding.Unicode.GetByteCount(c.Str ?? "") + 0,
        _ => 1,
    };
}