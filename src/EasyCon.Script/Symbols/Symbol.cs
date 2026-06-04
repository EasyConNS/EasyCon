using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script.Symbols;

public enum SlotCategory : byte
{
    Int,    // bool, byte, int, uint
    Long,   // uint64, ptr
    Double, // double
    Handle  // string, array, struct
}

public record struct SlotDesc(SlotCategory Category, int Index);

public record struct FrameLayout(int IntSlots, int LongSlots, int DoubleSlots, int HandleSlots)
{
    public readonly int TotalSlots => IntSlots + LongSlots + DoubleSlots + HandleSlots;
}

public abstract class Symbol(string name)
{
    public readonly string Name = name;

    public override string ToString() => $"Symbol({Name})";
}

public abstract class VariableSymbol(string name, bool isReadOnly, ScriptType type) : Symbol(name)
{
    public readonly ScriptType Type = type;
    public readonly bool IsReadOnly = isReadOnly;
    internal object? Value = null;
}

public sealed class GlobalVariableSymbol(string name, bool isReadOnly, ScriptType type) : VariableSymbol(name, isReadOnly, type)
{
    public override string ToString() => $"GlobalVar({Name}: {Type})";
}

public class LocalVariableSymbol(string name, bool isReadOnly, ScriptType type) : VariableSymbol(name, isReadOnly, type)
{
    /// <summary>在函数帧 Value[] 中的索引，由 Binder 分配</summary>
    public int SlotIndex { get; set; } = -1;

    /// <summary>类型化 slot 描述，由 Binder 分配</summary>
    public SlotDesc Slot { get; set; }

    public override string ToString() => $"LocalVar({Name}: {Type}, Slot={SlotIndex})";
}

public sealed class ParamSymbol(string name, ScriptType type, int ordinal = 0, bool hasDefault = false, object? defaultValue = null) : LocalVariableSymbol(name, false, type)
{
    public int Ordinal { get; } = ordinal;
    public bool HasDefaultValue { get; } = hasDefault;
    public object? DefaultValue { get; } = defaultValue;
}

public sealed class FunctionSymbol(
    string name,
    IEnumerable<ParamSymbol> parameters,
    ScriptType returnType,
    string libraryName = "internal",
    string? externalName = null) : Symbol(name), IEquatable<FunctionSymbol>
{
    public ImmutableArray<ParamSymbol> Parameters { get; } = [.. parameters];
    internal FuncDeclBlock? Declaration { get; init; }
    public ScriptType ReturnType { get; } = returnType;
    public readonly string LibraryName = libraryName;
    public string ExternalName => externalName ?? Name;

    /// <summary>函数局部变量帧大小（含参数），由 Binder 在绑定完成后设置</summary>
    public int LocalSlotCount { get; set; }

    /// <summary>类型化帧布局，由 Binder 在绑定完成后设置</summary>
    public FrameLayout Layout { get; set; }

    public override string ToString() => $"Func({Name}: {ReturnType})";

    // ---- 基于函数签名的值相等（解决 DeclarationCollector / Binder 双实例问题） ----

    public bool Equals(FunctionSymbol? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Name != other.Name || !ReturnType.Equals(other.ReturnType))
            return false;
        if (Parameters.Length != other.Parameters.Length)
            return false;
        for (int i = 0; i < Parameters.Length; i++)
        {
            if (!Parameters[i].Type.Equals(other.Parameters[i].Type))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as FunctionSymbol);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Name);
        h.Add(ReturnType);
        h.Add(Parameters.Length);
        foreach (var p in Parameters)
            h.Add(p.Type);
        return h.ToHashCode();
    }

    public static bool operator ==(FunctionSymbol? left, FunctionSymbol? right) => Equals(left, right);
    public static bool operator !=(FunctionSymbol? left, FunctionSymbol? right) => !Equals(left, right);
}