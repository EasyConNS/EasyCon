using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script.Bytecode;

/// <summary>常量池条目。payload 按标签解释；字符串以 UTF-16 code unit 存取。</summary>
public sealed class EcsConst
{
    public byte Tag;
    public long Int64;
    public double Float64;
    public string? Str;

    public static EcsConst FromInt(long v) => new() { Tag = EcsTag.Int, Int64 = v };
    public static EcsConst FromUInt(uint v) => new() { Tag = EcsTag.UInt, Int64 = v };
    public static EcsConst FromUInt64(ulong v) => new() { Tag = EcsTag.UInt64, Int64 = unchecked((long)v) };
    public static EcsConst FromDouble(double v) => new() { Tag = EcsTag.Double, Float64 = v };
    public static EcsConst FromPtr(long v) => new() { Tag = EcsTag.Ptr, Int64 = v };
    public static EcsConst FromString(string s) => new() { Tag = EcsTag.String, Str = s };

    public override string ToString() => Tag switch
    {
        EcsTag.String => $"\"{Str}\"",
        EcsTag.Double => Float64.ToString(),
        _ => Int64.ToString(),
    };
}

/// <summary>结构体字段布局。</summary>
public sealed class EcsFieldLayout
{
    public required string Name;
    public required EcsFieldKind Kind;
    public required EcsTypeCode Type;      // Scalar/Dynamic：元素或本体类型
    public EcsTypeCode ElementType;        // FixedArray：元素类型
    public int Count;                      // FixedArray：元素个数
    /// <summary>NestedStruct：嵌套类型的类型表索引。</summary>
    public int NestedSid;
    /// <summary>本字段在结构体值槽区的起始偏移（布局时计算）。</summary>
    public int SlotOffset;
}

/// <summary>结构体类型定义（共享类型表条目）。</summary>
public sealed class EcsStructLayout
{
    public required string Name;
    public required ImmutableArray<EcsFieldLayout> Fields;
    /// <summary>值槽总数（Scalar/Dynamic 占 1，FixedArray 占 Count）。</summary>
    public int SlotCount;
}

/// <summary>全局变量槽（模块私有，链接时串联）。</summary>
public sealed class EcsGlobal
{
    public required string Name;
    public required string Module;
    public EcsTypeCode Type;
    /// <summary>链接后的镜像全局槽位（链接时填写）。</summary>
    public int ImageSlot;
}

/// <summary>链接后的函数。</summary>
public sealed class EcsFunction
{
    public required string Name;
    public required string Module;
    public int NParams;
    public int NSlots;
    public bool HasReturn;
    public List<uint> Code = new();
    /// <summary>
    /// PC 执行专用的宽 A/B/C 操作数旁表，键为指令首字的 pc。ECX2 指令本体仍保留低 8 位，
    /// 因此可烧录函数通常没有旁表项；桌面解释器用这里的完整整数槽号解除 255 槽限制。
    /// 该旁表写入 ECM 编译缓存，但不写入 MCU .ecx。
    /// </summary>
    public Dictionary<int, EcsWideOperands> WideOperands = new();
    /// <summary>链接后在本镜像函数表中的下标。</summary>
    public int ImageIndex;

    /// <summary>
    /// 行号表（交错 [pc, line, ...]，pc 严格递增；JVM LineNumberTable 同型稀疏表）。
    /// 运行错误经 LineAt(pc) 映射回源码行；仅进 ECM 缓存与桌面诊断，不写入 MCU .ecx。
    /// </summary>
    public List<int> LineTable = new();

    /// <summary>pc → 源码行（1 基；空表或 pc 早于首登记返回 0）。二分取 ≤pc 的最近登记。</summary>
    public int LineAt(int pc)
    {
        int lo = 0, hi = LineTable.Count / 2 - 1, result = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (LineTable[mid * 2] <= pc)
            {
                result = LineTable[mid * 2 + 1];
                lo = mid + 1;
            }
            else
                hi = mid - 1;
        }
        return result;
    }

    /// <summary>读取指令操作数；桌面宽槽旁表只覆盖超出 ECX2 8 位字段的槽位。</summary>
    public EcsOperands OperandsAt(int pc, uint word)
    {
        int a = (int)((word >> 8) & 0xFF);
        int b = (int)((word >> 16) & 0xFF);
        int c = (int)((word >> 24) & 0xFF);
        bool hasWideC = false;
        if (WideOperands.TryGetValue(pc, out EcsWideOperands wide))
        {
            if ((wide.Mask & EcsWideOperands.AMask) != 0) a = wide.A;
            if ((wide.Mask & EcsWideOperands.BMask) != 0) b = wide.B;
            if ((wide.Mask & EcsWideOperands.CMask) != 0)
            {
                c = wide.C;
                hasWideC = true;
            }
        }
        if (!hasWideC && ((EcsOpcode)(word & 0xFF) is EcsOpcode.Call or EcsOpcode.CallN) && c == 255)
        {
            c = -1;   // ECX2 兼容哨兵；旁表中的 C=255 则表示真实桌面槽位 255
        }
        return new EcsOperands(a, b, c);
    }
}

/// <summary>镜像原生函数名表条目。</summary>
public sealed class EcsNative
{
    public required string Name;
}

/// <summary>模块导入：需要的外部函数（按 名+参数个数 解析，docs/EcmEcxFormat.md §4.2）。</summary>
public sealed class EcsImport
{
    public required string Name;
    public int NParams;
    public bool HasReturn;
}

/// <summary>模块导出：本模块可见于其他模块的函数。</summary>
public sealed class EcsExport
{
    public required string Name;
    public int LocalFid;
    public int NParams;
    public bool HasReturn;
}

/// <summary>
/// 模块产物（.ecm 的内存表示，docs/EcmEcxFormat.md §4）。
/// 函数 Call 目标：本模块 = 局部 fid；外部 = 0x80000000 | 导入表索引。
/// LoadG/StoreG Bx = 模块局部全局槽；CallN ext = 模块局部原生 idx；LoadK Bx = 模块常量池 idx。
/// </summary>
public sealed class ModuleArtifact
{
    /// <summary>缓存读出完整性自检（disk/process 两条 TryLoad 路径共享）：
    /// 接口区存在且接口哈希与内容自洽；损坏/被篡改 → false（调用方按未命中重编）。</summary>
    public bool IntegrityCheck()
        => Interface is { } iface && iface.InterfaceHash == InterfaceHasher.Compute(iface);

    public required string Name;
    public required List<EcsFunction> Functions;
    public required ModulePool Pool;
    public required List<EcsImport> Imports;
    public required List<EcsExport> Exports;
    public required List<EcsGlobal> Globals;
    public required List<EcsNative> Natives;
    /// <summary>程序级类型表快照（v1；真独立编译期改为本模块声明集）。</summary>
    public required List<EcsStructLayout> Structs;
    public required List<string> ILNames;
    /// <summary>本模块是否承载 $eval（v1：main 模块；模块模式仅 main 为 true）。</summary>
    public bool HasEval;
    /// <summary>模块有顶层语句（$eval 即 &lt;init:module&gt;，链接期合成 &lt;main&gt; 调用，ModuleSystem.md §5.4-4）。</summary>
    public bool HasInit;
    /// <summary>init 函数的模块局部 fid（HasInit 时有效；链接期合成 &lt;main&gt; 用）。</summary>
    public int InitFid = -1;
    /// <summary>模块含按键指令（模块模式逐模块扫描后链接期并集；v1 路径不设置）。</summary>
    public bool KeyAction;
    /// <summary>模块引用图像标签（模块模式逐模块扫描后链接期并集；v1 路径不设置）。</summary>
    public bool NeedIL;
    /// <summary>模块接口（ECM v2 接口区；缺省 v1 产物为 null，ModuleSystem.md §4）。</summary>
    public ModuleInterface? Interface;
    /// <summary>本模块 SSA（keepSsa 时保留，供诊断/对拍；不参与 .ecm 序列化）。</summary>
    public SsaProgram? Ssa;
}

/// <summary>
/// 链接后内存执行镜像。桌面可携带宽槽旁表；纯 C VM 只接收 EcxWriter 校验后的 ECX2 子集。
/// </summary>
public sealed class EcxImage
{
    public List<EcsConst> Consts = new();
    public List<EcsStructLayout> Structs = new();
    public List<EcsGlobal> Globals = new();
    public List<EcsNative> Natives = new();
    public List<EcsFunction> Functions = new();
    public int Entry;
    /// <summary>模块名表（诊断/元数据用）。</summary>
    public List<string> Modules = new();
    /// <summary>程序含按键指令（宿主需创建手柄）——对齐 CompileResult.KeyAction。</summary>
    public bool KeyAction;
    /// <summary>程序引用图像标签——对齐 CompileResult.NeedIL。</summary>
    public bool NeedIL;
    /// <summary>
    /// 特征需求掩码（EcsImageFeatures：CAPTURE/FFI/FILE；IL 由 NeedIL 投影，写入头部保留位
    /// u16 @0x0A）。加载规则：host 掩码缺位 → 拒跑（IL → ECS_ERR_IL，其余 → ECS_ERR_FEAT）。
    /// </summary>
    public uint Features;
    /// <summary>全程序最大帧槽数（max(funcs.nslots)，静态预分配帧区用，EcmEcxFormat §2.1）。</summary>
    public int MaxSlots;
    /// <summary>静态调用图最长链（递归按 1 计；运行期上限另由 ECS_MAX_CALL_DEPTH 约束）。</summary>
    public int MaxDepth;

    public string FunctionName(int fid) => fid >= 0 && fid < Functions.Count ? Functions[fid].Name : $"?func{fid}";
}

/// <summary>编码/链接诊断。</summary>
public readonly record struct BytecodeDiagnostic(string Message, string? Function, int Instruction)
{
    public override string ToString() => Function == null ? Message : $"{Function}+{Instruction}: {Message}";
}

/// <summary>编码失败异常。</summary>
public class BytecodeException : Exception
{
    public ImmutableArray<BytecodeDiagnostic> Diagnostics { get; }

    public BytecodeException(IEnumerable<BytecodeDiagnostic> diagnostics)
        : base(string.Join("\n", diagnostics.Select(d => d.ToString())))
    {
        Diagnostics = [.. diagnostics];
    }
}

/// <summary>解码后的完整 iABC 操作数。</summary>
public readonly record struct EcsOperands(int A, int B, int C);

/// <summary>桌面宽槽旁表项；Mask 指定哪些字段覆盖 ECX2 指令字中的 8 位值。</summary>
public readonly record struct EcsWideOperands(byte Mask, int A, int B, int C)
{
    public const byte AMask = 1;
    public const byte BMask = 2;
    public const byte CMask = 4;
}