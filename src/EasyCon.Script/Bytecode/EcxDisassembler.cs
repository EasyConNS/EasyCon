using System.Text;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// 镜像反汇编器（调试/CI 黄金快照，docs/VM2.md §10.3）。
/// 单流化后直接读解码形态——顺带显示真实槽位（不再有影子流截断的失真）。
/// </summary>
internal static class EcxDisassembler
{
    public static string Disassemble(EcxImage image)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"; ECX1 image: {image.Functions.Count} funcs, {image.Consts.Count} consts, " +
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
            for (int i = 0; i < f.Instructions.Count; i++)
                sb.AppendLine($"  {i,4}: {FormatInstruction(image, f.Instructions[i])}");
        }
        return sb.ToString();
    }

    static string FormatInstruction(EcxImage image, in EcsInstruction ins)
    {
        int a = ins.A, b = ins.B, c = ins.C;
        uint ext = ins.Ext;
        string extText = ins.HasExt ? $" {FormatExt(ins.Op, ext, image)}" : "";
        string tail = ins.IsJump ? $" {ins.Jump:+0;-0}" : extText;

        return ins.Op switch
        {
            EcsOpcode.LoadI => $"{ins.Op} r{a}, {b}",
            EcsOpcode.LoadK => $"{ins.Op} r{a}, K[{b}]{ConstText(image, b)}",
            EcsOpcode.LoadG => $"{ins.Op} r{a}, G[{b}]{GlobalText(image, b)}",
            EcsOpcode.StoreG => $"{ins.Op} G[{b}], r{a}{GlobalText(image, b)}",
            EcsOpcode.NewArrE => $"{ins.Op} r{a}, type={b}",
            EcsOpcode.NewSt => $"{ins.Op} r{a}, struct[{b}]",
            EcsOpcode.Img => $"{ins.Op} r{a}, {ConstText(image, b)}",
            EcsOpcode.WaitI => $"{ins.Op} {ins.Ext}ms",
            EcsOpcode.KeyI => $"{ins.Op} key={a}, {ins.Ext}ms",
            EcsOpcode.Jmp => $"{ins.Op}{tail}",
            EcsOpcode.ForStep => $"{ins.Op} r{a}, limit=r{b}, dst=r{c}, exit={ins.Jump:+0;-0}",
            EcsOpcode.Jpt => $"{ins.Op} r{a}{tail}",
            EcsOpcode.Jpf => $"{ins.Op} r{a}{tail}",
            EcsOpcode.CmpJ => $"{ins.Op} r{a}, r{b}, kind={c}, {ins.Jump:+0;-0}",
            EcsOpcode.Call => $"{ins.Op} args=r{a}..+{b}, recv={(c < 0 ? "-" : $"r{c}")}, func[{ext}]{FuncText(image, ext)}",
            EcsOpcode.CallN => $"{ins.Op} args=r{a}..+{b}, recv={(c < 0 ? "-" : $"r{c}")}, {CallNTargetText(image, ext)}",
            EcsOpcode.Slice => $"{ins.Op} r{a}, r{b}, r{c}, end={(ext == 0xFFFFFFFF ? "-" : $"r{ext}")}",
            EcsOpcode.GetFI => $"{ins.Op} r{a}, r{b}.field[{c}], idx=r{ext}",
            EcsOpcode.PutFI => $"{ins.Op} r{b}.field[{c}] = r{a}, idx=r{ext}",
            EcsOpcode.StickP => $"{ins.Op} side={a}, ({b},{c}), {ext}ms",
            EcsOpcode.StickPv => $"{ins.Op} side={a}, dur=r{c}, xy=({ext & 0xFF},{(ext >> 16) & 0xFF})",
            _ => $"{ins.Op} r{a}, r{b}, r{c}{extText}",
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
