using System.Text;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// ECX 镜像反汇编器（调试/CI 黄金快照，docs/VM2.md §10.3）。
/// </summary>
public static class EcxDisassembler
{
    public static string Disassemble(EcxImage image)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"; ECX image: {image.Functions.Count} funcs, {image.Consts.Count} consts, " +
                      $"{image.Structs.Count} structs, {image.Globals.Count} globals, {image.Natives.Count} natives, " +
                      $"entry={image.Entry} keyAction={image.KeyAction} needIL={image.NeedIL}");
        sb.AppendLine($"; modules: {string.Join(", ", image.Modules)}");

        if (image.Natives.Count > 0)
            sb.AppendLine($"; natives: {string.Join(", ", image.Natives.Select(n => n.Name))}");

        foreach (var s in image.Structs)
        {
            sb.Append($"struct {s.Name} (slots={s.SlotCount}) {{ ");
            sb.Append(string.Join("; ", s.Fields.Select(f =>
                f.Kind == EcsFieldKind.FixedArray ? $"{f.Name}:{f.Type}[{f.Count}]" : $"{f.Name}:{f.Type}")));
            sb.AppendLine(" }");
        }

        foreach (var f in image.Functions)
        {
            sb.AppendLine();
            sb.AppendLine($"func [{image.Functions.IndexOf(f)}] {f.Name} (module={f.Module}, params={f.NParams}, slots={f.NSlots}, hasret={f.HasReturn}):");
            for (int i = 0; i < f.Code.Count;)
            {
                var op = (EcsOpcode)(f.Code[i] & 0xFF);
                sb.AppendLine($"  {i,4}: {FormatInstruction(image, f.Code, i, out int words)}");
                i += words;
            }
        }
        return sb.ToString();
    }

    static string FormatInstruction(EcxImage image, List<uint> code, int i, out int words)
    {
        var word = code[i];
        var op = (EcsOpcode)(word & 0xFF);
        int a = (int)((word >> 8) & 0xFF);
        int b = (int)((word >> 16) & 0xFF);
        int c = (int)((word >> 24) & 0xFF);
        uint ext = 0;
        bool hasExt = EcsFormat.Get(op) == EcsInsFormat.Ext;
        if (hasExt && i + 1 < code.Count)
            ext = code[i + 1];
        words = EcsFormat.WordCount(op);

        string extText = hasExt ? $" {FormatExt(op, ext, image)}" : "";

        return op switch
        {
            EcsOpcode.LoadI => $"{op} r{a}, {Sign16(word >> 16)}",
            EcsOpcode.LoadK => $"{op} r{a}, K[{b | (c << 8)}]{ConstText(image, b | (c << 8))}",
            EcsOpcode.LoadG => $"{op} r{a}, G[{b | (c << 8)}]{GlobalText(image, b | (c << 8))}",
            EcsOpcode.StoreG => $"{op} G[{b | (c << 8)}], r{a}{GlobalText(image, b | (c << 8))}",
            EcsOpcode.NewArrE => $"{op} r{a}, type={b | (c << 8)}",
            EcsOpcode.NewSt => $"{op} r{a}, struct[{b | (c << 8)}]",
            EcsOpcode.Img => $"{op} r{a}, {ConstText(image, b | (c << 8))}",
            EcsOpcode.WaitI => $"{op} {b | (c << 8)}ms",
            EcsOpcode.KeyI => $"{op} key={a}, {b | (c << 8)}ms",
            EcsOpcode.Jmp => $"{op} {Sign24(word >> 8):+0;-0}",
            EcsOpcode.Jpt => $"{op} r{a}, {Sign16(word >> 16):+0;-0}",
            EcsOpcode.Jpf => $"{op} r{a}, {Sign16(word >> 16):+0;-0}",
            EcsOpcode.Call => $"{op} args=r{a}..+{b}, recv={(c == 255 ? "-" : $"r{c}")}, func[{ext}]{FuncText(image, ext)}",
            EcsOpcode.CallN => $"{op} args=r{a}..+{b}, recv={(c == 255 ? "-" : $"r{c}")}, {CallNTargetText(image, ext)}",
            EcsOpcode.Slice => $"{op} r{a}, r{b}, r{c}, end={(ext == 0xFFFFFFFF ? "-" : $"r{ext}")}",
            EcsOpcode.GetFI => $"{op} r{a}, r{b}.field[{c}], idx=r{ext}",
            EcsOpcode.PutFI => $"{op} r{b}.field[{c}] = r{a}, idx=r{ext}",
            EcsOpcode.StickP => $"{op} side={a}, ({b},{c}), {ext}ms",
            EcsOpcode.StickPv => $"{op} side={a}, dur=r{c}, xy=({ext & 0xFF},{(ext >> 16) & 0xFF})",
            _ => $"{op} r{a}, r{b}, r{c}",
        };
    }

    static string FormatExt(EcsOpcode op, uint ext, EcxImage image) => op switch
    {
        EcsOpcode.Call => $"func[{ext}]{FuncText(image, ext)}",
        EcsOpcode.CallN => CallNTargetText(image, ext),
        EcsOpcode.Slice => ext == 0xFFFFFFFF ? "end=-" : $"end=r{ext}",
        EcsOpcode.GetFI => $"idx=r{ext}",
        EcsOpcode.PutFI => $"idx=r{ext}",
        EcsOpcode.StickP => $"{ext}ms",
        EcsOpcode.StickPv => $"xy=({ext & 0xFF},{(ext >> 16) & 0xFF})",
        _ => $"0x{ext:X}",
    };

    static int Sign16(uint v) => (int)(ushort)v << 16 >> 16;
    static int Sign24(uint v) => (int)(v & 0xFFFFFF) << 8 >> 8;

    static string ConstText(EcxImage image, int idx)
        => idx < image.Consts.Count ? $" ; {image.Consts[idx]}" : " ; ?const";

    static string GlobalText(EcxImage image, int idx)
        => idx < image.Globals.Count ? $" ; {image.Globals[idx].Name}" : " ; ?global";

    static string FuncText(EcxImage image, uint fid)
        => fid < (uint)image.Functions.Count ? $" ; {image.Functions[(int)fid].Name}" : "";

    static string NativeText(EcxImage image, uint nid)
        => nid < (uint)image.Natives.Count ? $" ; {image.Natives[(int)nid].Name}" : "";

    /// <summary>CallN 目标：旗标置位 = syscall 编号（渲染规范名）；否则原生名表索引。</summary>
    static string CallNTargetText(EcxImage image, uint ext)
    {
        if ((ext & EcsSyscall.CallFlag) == 0)
            return $"native[{ext}]{NativeText(image, ext)}";
        var id = ext & 0x7FFFFFFFu;
        var name = id < (uint)EcsSyscall.Names.Length ? EcsSyscall.Names[id] : "?syscall";
        return $"syscall[{id}] ; {name}";
    }
}