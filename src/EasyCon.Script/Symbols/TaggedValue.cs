using System.Runtime.InteropServices;
using EasyCon.Script.Runtime;

namespace EasyCon.Script.Symbols;

/// <summary>
/// 统一值表示：16 字节 tagged union，替代 4 路类型分裂存储。
/// 所有脚本值（int/double/uint64/handle）统一存储于此类型。
/// 值类型字段通过显式布局零开销互斥共享；引用类型通过 handle 间接。
/// 设计参考 CPython localsplus 统一槽位 + LuaJIT NaN-boxing 内联值。
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct TaggedValue
{
    // ── Tag (1 byte，低 8 位) ──
    [FieldOffset(0)] public byte Tag;

    // ── 值载荷：8 字节互斥共享，偏移 8（保证 8 字节对齐） ──
    [FieldOffset(8)] public int    I32;   // bool(0/1), byte, int, uint（位保留 reinterpret）
    [FieldOffset(8)] public long   I64;   // uint64, ptr, handle(int32 in low bits)
    [FieldOffset(8)] public double F64;   // double

    // ── Tag 常量（与 Value 结构体的 tag 对齐） ──
    public const byte VOID    = 0;
    public const byte BOOL    = 1;
    public const byte BYTE    = 2;
    public const byte INT     = 3;
    public const byte UINT    = 4;
    public const byte UINT64  = 5;
    public const byte FLOAT64 = 6;
    public const byte STRING  = 7;   // I64 low32 = heap handle
    public const byte ARRAY   = 9;   // I64 low32 = heap handle
    public const byte PTR     = 10;
    public const byte STRUCT  = 12;  // I64 low32 = heap handle

    /// <summary>Void 实例（默认零值即 VOID）。</summary>
    public static readonly TaggedValue Void = default;

    // ============ 工厂方法 ============

    public static TaggedValue FromInt(int v)       => new() { Tag = INT,     I32 = v };
    public static TaggedValue FromBool(bool v)     => new() { Tag = BOOL,    I32 = v ? 1 : 0 };
    public static TaggedValue FromByte(byte v)     => new() { Tag = BYTE,    I32 = v };
    public static TaggedValue FromUInt(uint v)     => new() { Tag = UINT,    I32 = unchecked((int)v) };
    public static TaggedValue FromDouble(double v) => new() { Tag = FLOAT64, F64 = v };
    public static TaggedValue FromUInt64(ulong v)  => new() { Tag = UINT64,  I64 = unchecked((long)v) };
    public static TaggedValue FromPtr(long v)      => new() { Tag = PTR,     I64 = v };

    /// <summary>构造引用类型值（string/array/struct）：值是 heap handle。</summary>
    public static TaggedValue FromHandle(byte tag, int handle)
        => new() { Tag = tag, I64 = handle };

    public static TaggedValue FromStringHandle(int handle) => new() { Tag = STRING, I64 = handle };
    public static TaggedValue FromArrayHandle(int handle)  => new() { Tag = ARRAY,  I64 = handle };
    public static TaggedValue FromStructHandle(int handle) => new() { Tag = STRUCT, I64 = handle };

    // ============ 快速提取 ============

    public int    AsInt()      => I32;
    public bool   AsBool()     => I32 != 0;
    public byte   AsByte()     => (byte)I32;
    public uint   AsUInt()     => unchecked((uint)I32);
    public double AsDouble()   => F64;
    public ulong  AsUInt64()   => unchecked((ulong)I64);
    public long   AsPtr()      => I64;

    /// <summary>引用类型的 heap handle（低 32 位）。</summary>
    public int Handle => (int)I64;

    public bool IsVoid    => Tag == VOID;
    public bool IsHandle  => Tag is STRING or ARRAY or STRUCT;
    public bool IsString  => Tag == STRING;
    public bool IsArray   => Tag == ARRAY;
    public bool IsStruct  => Tag == STRUCT;
    public bool IsIntLike => Tag is BOOL or BYTE or INT or UINT;

    /// <summary>该值是否为整数类别（用于 ConvToInt 等）。</summary>
    public bool IsIntegerCategory => Tag is BOOL or BYTE or INT or UINT;
    public bool IsLongCategory    => Tag is UINT64 or PTR;
    public bool IsDoubleCategory  => Tag == FLOAT64;

    // ============ Value 互转（API 边界兼容） ============

    /// <summary>从 Value 构造 TaggedValue。</summary>
    public static TaggedValue FromValue(in Value v)
    {
        // 直接读 Value 的 tag 字段（通过 Type 属性间接判断避免依赖内部布局）
        var t = v.Type;
        if (t.Equals(ScriptType.Int))    return FromInt(v.AsInt());
        if (t.Equals(ScriptType.Bool))   return FromBool(v.AsBool());
        if (t.Equals(ScriptType.Byte))   return FromByte(v.AsByte());
        if (t.Equals(ScriptType.UInt))   return FromUInt(v.AsUInt());
        if (t.Equals(ScriptType.Double)) return FromDouble(v.AsDouble());
        if (t.Equals(ScriptType.UInt64)) return FromUInt64(v.AsUInt64());
        if (t.Equals(ScriptType.Ptr))    return FromPtr(v.AsPtr());
        // 引用类型：转换为 handle 时由调用方负责（需 heap 上下文）
        return default;
    }

    /// <summary>转换为 Value（不包含引用类型对象，引用类型需调用方通过 heap 解引用后单独处理）。</summary>
    public Value ToValue()
    {
        return Tag switch
        {
            INT     => Value.FromInt(I32),
            BOOL    => Value.FromBool(I32 != 0),
            BYTE    => Value.FromByte((byte)I32),
            UINT    => Value.FromUInt(unchecked((uint)I32)),
            FLOAT64 => Value.FromDouble(F64),
            UINT64  => Value.FromUInt64(unchecked((ulong)I64)),
            PTR     => Value.FromPtr(I64),
            VOID    => Value.Void,
            _       => Value.Void, // 引用类型由调用方处理
        };
    }

    public override string ToString() => $"TaggedValue(Tag={Tag})";
}
