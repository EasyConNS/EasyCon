using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Symbols;

enum SlotCategory : byte
{
    Int,    // bool, byte, int, uint
    Long,   // uint64, ptr
    Double, // double
    Handle  // string, array, struct
}

record struct SlotDesc(SlotCategory Category, int Index);

record struct FrameLayout(int IntSlots, int LongSlots, int DoubleSlots, int HandleSlots)
{
    public readonly int TotalSlots => IntSlots + LongSlots + DoubleSlots + HandleSlots;
}

abstract class Symbol(string name)
{
    public readonly string Name = name;
}

abstract class VariableSymbol(string name, bool isReadOnly, ScriptType type) : Symbol(name)
{
    public readonly ScriptType Type = type;
    public readonly bool IsReadOnly = isReadOnly;
    internal object? Value = null;
}

sealed class GlobalVariableSymbol(string name, bool isReadOnly, ScriptType type) : VariableSymbol(name, isReadOnly, type)
{
    public override string ToString() => $"GlobalVar({Name}: {Type})";
}

class LocalVariableSymbol(string name, bool isReadOnly, ScriptType type) : VariableSymbol(name, isReadOnly, type)
{
    /// <summary>在函数帧 Value[] 中的索引，由 Binder 分配</summary>
    public int SlotIndex { get; set; } = -1;

    /// <summary>类型化 slot 描述，由 Binder 分配</summary>
    public SlotDesc Slot { get; set; }

    public override string ToString() => $"LocalVar({Name}: {Type}, Slot={SlotIndex})";
}

sealed class ParamSymbol(string name, ScriptType type, int ordinal = 0, bool hasDefault = false, object? defaultValue = null) : LocalVariableSymbol(name, false, type)
{
    public int Ordinal { get; } = ordinal;
    public bool HasDefaultValue { get; } = hasDefault;
    public object? DefaultValue { get; } = defaultValue;
}

sealed class FunctionSymbol(
    string name,
    IEnumerable<ParamSymbol> parameters,
    ScriptType returnType,
    FuncDeclBlock? declaration = null,
    string libraryName = "internal",
    string? externalName = null) : Symbol(name)
{
    public ImmutableArray<ParamSymbol> Parameters { get; } = [.. parameters];
    public readonly FuncDeclBlock? Declaration = declaration;
    public ScriptType ReturnType { get; } = returnType;
    public readonly string LibraryName = libraryName;
    public string ExternalName => externalName ?? Name;

    /// <summary>函数局部变量帧大小（含参数），由 Binder 在绑定完成后设置</summary>
    public int LocalSlotCount { get; set; }

    /// <summary>类型化帧布局，由 Binder 在绑定完成后设置</summary>
    public FrameLayout Layout { get; set; }

    public override string ToString() => $"Func({Name}: {ReturnType})";
}