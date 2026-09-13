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
    /// <summary>链接后在本镜像函数表中的下标。</summary>
    public int ImageIndex;
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
/// 链接后可执行镜像（ECX 的内存表示），纯 C 虚拟机的唯一输入。
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