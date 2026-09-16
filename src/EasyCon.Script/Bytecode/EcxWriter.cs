using System.Text;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// ECX 镜像序列化——严格按 docs/EcmEcxFormat.md §2 冻结规范：
/// 0x24 头部（含 max_slots/max_depth）、核心表无名字（名字进调试区）、
/// 代码区 4 字节对齐、entry 收尾。小端。
/// v2（uvm32 式纯净镜像）：核心全局表项 = 模块 idx:u8 + 类型码:u8 定长 2 字节，
/// 全局名与函数名同住调试区，受 flags.D / stripDebug 门控——MCU 发布镜像零名字重量。
/// </summary>
public static class EcxWriter
{
    public const uint Magic = 0x32435845;   // "ECX2"
    public const ushort FormatVersion = 2;

    /// <summary>
    /// 序列化 ECX 镜像。<paramref name="stripDebug"/> = true 时剥离调试区（函数名表），
    /// flags.D=0、debug_count=0（EcmEcxFormat.md §2.2/§2.9），供 MCU 发布版减小镜像。
    /// </summary>
    public static byte[] Write(EcxImage image, bool stripDebug = false)
    {
        // 槽位在 ECX 冻结格式里是 u8（max_slots、FuncDef.nslots）：超出即无法表示。
        // 不做静默截断——按设计，编译产物超出 MCU 容量应响亮失败（编译产物过大天然无法执行）。
        if (image.MaxSlots > 255)
            throw new BytecodeException(new[] { new BytecodeDiagnostic(
                $"镜像 max_slots {image.MaxSlots} 超出 ECX 冻结格式上限 255（产物过大，单片机无法执行）", null, 0) });
        foreach (var f in image.Functions)
            if (f.NSlots > 255)
                throw new BytecodeException(new[] { new BytecodeDiagnostic(
                    $"函数 {f.Name} 帧槽位 {f.NSlots} 超出 ECX 冻结格式上限 255", f.Name, 0) });

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // ---- 头部（0x24 字节）----
        w.Write(Magic);
        w.Write(FormatVersion);
        ushort flags = 0;
        if (!stripDebug && image.Functions.Count > 0) flags |= 0x1;   // D：调试区（函数名表）
        if (image.KeyAction) flags |= 0x2;             // K
        if (image.NeedIL) flags |= 0x4;                // I
        w.Write(flags);
        w.Write((byte)image.MaxSlots);
        w.Write((byte)Math.Min(image.MaxDepth, 255));
        w.Write((ushort)(image.Features & 0xFFFF));     // 特征需求掩码（原保留位，VM2.md §9.1）
        w.Write(image.Consts.Count);
        w.Write(image.Structs.Count);
        w.Write(image.Globals.Count);
        w.Write(image.Natives.Count);
        w.Write(image.Functions.Count);
        w.Write(stripDebug ? 0 : image.Functions.Count + image.Globals.Count); // debug_count（§2.2：D=0 时为 0；v2 = 函数名 + 全局名）

        // ---- 常量池 ----
        foreach (var c in image.Consts)
            WriteConst(w, c);

        // ---- 类型表 ----
        foreach (var s in image.Structs)
        {
            WriteUtf8(w, s.Name);
            w.Write((byte)s.Fields.Length);
            foreach (var f in s.Fields)
            {
                WriteUtf8(w, f.Name);
                w.Write((byte)f.Kind);
                w.Write((byte)f.Type);
                w.Write((byte)f.ElementType);
                w.Write((ushort)(f.Kind == EcsFieldKind.NestedStruct ? f.NestedSid : f.Count));   // ext 槽：FixedArray=元素数；NestedStruct=嵌套 sid（内存表示存 NestedSid）
            }
        }

        // ---- 全局表（v2：核心表项无名字，定长 2 字节；名字在调试区）----
        foreach (var g in image.Globals)
        {
            w.Write((byte)image.Modules.IndexOf(g.Module));
            w.Write((byte)g.Type);
        }

        // ---- 原生名表 ----
        foreach (var n in image.Natives)
            WriteUtf8(w, n.Name);

        // ---- 函数表（11 字节定长，无名字；code_off 单位=指令字，相对代码区起始）----
        uint codeOffset = 0;
        foreach (var f in image.Functions)
        {
            w.Write((byte)f.NParams);
            w.Write((byte)f.NSlots);
            w.Write(f.HasReturn ? (byte)1 : (byte)0);
            w.Write(codeOffset);
            w.Write((uint)f.Code.Count);
            codeOffset += (uint)f.Code.Count;
        }

        // ---- 代码区（4 字节对齐——代码区之前均为 4 字节倍数内字段，天然对齐）----
        foreach (var f in image.Functions)
            foreach (var word in f.Code)
                w.Write(word);

        // ---- 调试区（v2：函数名表 + 全局名表，按各自表序；stripDebug 时整区不存在）----
        if (!stripDebug)
        {
            foreach (var f in image.Functions)
                WriteUtf8(w, f.Name);
            foreach (var g in image.Globals)
                WriteUtf8(w, g.Name);
        }

        // ---- entry 收尾 ----
        w.Write(image.Entry);
        w.Flush();
        return ms.ToArray();
    }

    static void WriteConst(BinaryWriter w, EcsConst c)
    {
        w.Write(c.Tag);
        switch (c.Tag)
        {
            case EcsTag.Int:
            case EcsTag.UInt:
                w.Write((int)c.Int64);
                break;
            case EcsTag.UInt64:
            case EcsTag.Ptr:
                w.Write(c.Int64);
                break;
            case EcsTag.Double:
                w.Write(c.Float64);
                break;
            case EcsTag.String:
                {
                    var units = Encoding.Unicode.GetBytes(c.Str ?? "");
                    w.Write((ushort)(units.Length / 2));
                    w.Write(units);
                    break;
                }
            default:
                throw new BytecodeException(new[] { new BytecodeDiagnostic($"不支持的常量标签 {c.Tag}", null, 0) });
        }
    }

    static void WriteUtf8(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length > 0xFFFF)
            throw new BytecodeException(new[] { new BytecodeDiagnostic($"标识符过长: {s}", null, 0) });
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }
}