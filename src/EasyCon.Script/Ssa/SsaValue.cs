using EasyCon.Script.Binding;
using EasyCon.Script.Symbols;
using System.Runtime.InteropServices;

namespace EasyCon.Script.Ssa;

/// <summary>
/// SSA 值：每个计算产生一个且仅一个 SsaValue。
/// 三地址码形式：Result = Op(Arg0, Arg1)。
/// 常量载荷使用显式布局避免值类型装箱；字符串通过独立字段存储（引用类型无装箱）。
/// </summary>
public sealed class SsaValue
{
    public readonly int Id;
    public SsaOp Op;
    public ScriptType Type;

    /// <summary>输入 1</summary>
    public SsaValue? Arg0;
    /// <summary>输入 2</summary>
    public SsaValue? Arg1;

    /// <summary>常量载荷（值类型零装箱：int/bool/double/long 通过显式布局共享）</summary>
    public ConstPayload Const;

    /// <summary>字符串常量（引用类型，无装箱）</summary>
    public string? ConstString;

    /// <summary>辅助信息（VariableSymbol / FunctionSymbol / EcsFieldDef / EcsStructDef 等引用类型，无装箱）</summary>
    public object? Aux;

    /// <summary>引用计数</summary>
    public int Uses;

    /// <summary>所属基本块</summary>
    public SsaBlock Block;

    /// <summary>分配的槽位（codegen 后填充）</summary>
    public SlotDesc Slot;

    /// <summary>附加参数列表（Phi/Call/ArrayInit 等多参数场景）</summary>
    public List<SsaValue>? ExtraArgs;

    public SsaValue(int id, SsaOp op, ScriptType type)
    {
        Id = id;
        Op = op;
        Type = type;
        Slot = new SlotDesc(-1); // -1 表示未分配槽位
    }

    public bool IsConstant => Op is >= SsaOp.ConstBool and <= SsaOp.ConstPtr;
    public bool HasSideEffect => Op is SsaOp.Call or SsaOp.StaticCall or SsaOp.KeyPress or SsaOp.KeyAction
        or SsaOp.StickAction or SsaOp.StickPress or SsaOp.Wait
        or SsaOp.StoreLocal or SsaOp.StoreGlobal or SsaOp.StoreField
        or SsaOp.StoreIndex or SsaOp.StoreFieldIndex or SsaOp.Return
        or SsaOp.ImageLabel or SsaOp.Capture or SsaOp.Ocr or SsaOp.Roi
        or SsaOp.Rand
        or SsaOp.OcrInit;

    public override string ToString() => $"v{Id}:{Op}({Type})";
}

/// <summary>
/// 常量载荷（值类型）：显式布局，int/double/long 互斥共享偏移，避免装箱。
/// 字符串常量通过 SsaValue.ConstString 单独存储（引用类型无装箱开销）。
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct ConstPayload
{
    [FieldOffset(0)] private int _intVal;      // int / uint (reinterpret) / bool (0/1) / byte
    [FieldOffset(0)] private double _doubleVal; // double
    [FieldOffset(0)] private long _longVal;     // long / ulong (reinterpret)

    // ---- 写入 ----
    public void SetBool(bool v) => _intVal = v ? 1 : 0;
    public void SetByte(byte v) => _intVal = v;
    public void SetInt(int v) => _intVal = v;
    public void SetUInt(uint v) => _intVal = unchecked((int)v);
    public void SetDouble(double v) => _doubleVal = v;
    public void SetUInt64(ulong v) => _longVal = unchecked((long)v);
    public void SetPtr(long v) => _longVal = v;

    // ---- 读取 ----
    public bool GetBool() => _intVal != 0;
    public byte GetByte() => (byte)_intVal;
    public int GetInt() => _intVal;
    public uint GetUInt() => unchecked((uint)_intVal);
    public double GetDouble() => _doubleVal;
    public ulong GetUInt64() => unchecked((ulong)_longVal);
    public long GetPtr() => _longVal;
}