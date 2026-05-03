using EasyCon.Script.Symbols;
using System.Runtime.InteropServices;

namespace EasyCon.Script.Runtime;

public class EcsFieldDef
{
    public string Name;
    public ScriptType FieldType;
    public int Offset;
    public int Size;
    public int Alignment;
}

public class EcsStructDef
{
    public string Name;
    public List<EcsFieldDef> Fields = [];
    public int Size;
    public int Alignment;
}

internal static class TypeLayout
{
    internal static int GetNativeSize(ScriptType type) => type switch
    {
        _ when type.Equals(ScriptType.Byte) => 1,
        _ when type.Equals(ScriptType.Bool) => 4,
        _ when type.Equals(ScriptType.Int) => 4,
        _ when type.Equals(ScriptType.UInt) => 4,
        _ when type.Equals(ScriptType.UInt64) => 8,
        _ when type.Equals(ScriptType.Double) => 8,
        _ when type.Equals(ScriptType.String) => IntPtr.Size,
        _ when type.Equals(ScriptType.Ptr) => IntPtr.Size,
        StructType s => s.Definition.Size,
        FixedArrayType a => GetNativeSize(a.ElementType) * a.Count,
        _ => 4
    };

    internal static ScriptType GetElementType(ScriptType type) => type is FixedArrayType a ? a.ElementType : type;
}

public static class StructLayout
{
    public static void Calculate(EcsStructDef def)
    {
        int offset = 0, maxAlign = 1;

        foreach (var f in def.Fields)
        {
            var baseType = TypeLayout.GetElementType(f.FieldType);
            int elemSize = TypeLayout.GetNativeSize(baseType);
            int align = baseType is StructType s ? s.Definition.Alignment : elemSize;

            offset = AlignUp(offset, align);

            f.Offset = offset;
            f.Size = TypeLayout.GetNativeSize(f.FieldType);
            f.Alignment = align;

            offset += f.Size;
            maxAlign = Math.Max(maxAlign, align);
        }

        def.Size = AlignUp(offset, maxAlign);
        def.Alignment = maxAlign;
    }

    static int AlignUp(int offset, int align) => (offset + align - 1) & ~(align - 1);
}

public sealed class EcsStruct : IDisposable
{
    readonly EcsStructDef _def;
    IntPtr _ptr;
    readonly bool _ownsMemory;

    public EcsStructDef Definition => _def;
    public IntPtr NativePtr => _ptr;

    public EcsStruct(EcsStructDef def)
    {
        _def = def;
        _ownsMemory = true;
        _ptr = Marshal.AllocHGlobal(def.Size);
        ZeroMemory(_ptr, def.Size);
    }

    internal EcsStruct(EcsStructDef def, IntPtr ptr, bool ownsMemory)
    {
        _def = def;
        _ptr = ptr;
        _ownsMemory = ownsMemory;
    }

    public EcsStruct(EcsStructDef def, IntPtr sourcePtr)
    {
        _def = def;
        _ownsMemory = true;
        _ptr = Marshal.AllocHGlobal(def.Size);
        ZeroMemory(_ptr, def.Size);
        if (sourcePtr != IntPtr.Zero)
            SafeCopy(_ptr, sourcePtr, def.Size);
    }

    public void SetField(EcsFieldDef f, object value) => SetFieldElement(f, 0, value);

    public void SetFieldElement(EcsFieldDef f, int index, object value)
    {
        var elemType = TypeLayout.GetElementType(f.FieldType);
        int elemSize = TypeLayout.GetNativeSize(elemType);
        IntPtr p = _ptr + f.Offset + index * elemSize;

        if (elemType is StructType st)
        {
            var src = (EcsStruct)value;
            SafeCopy(p, src.NativePtr, st.Definition.Size);
            return;
        }

        if (elemType.Equals(ScriptType.Byte)) Marshal.WriteByte(p, Convert.ToByte(value));
        else if (elemType.Equals(ScriptType.Int)) Marshal.WriteInt32(p, Convert.ToInt32(value));
        else if (elemType.Equals(ScriptType.Bool)) Marshal.WriteInt32(p, Convert.ToBoolean(value) ? 1 : 0);
        else if (elemType.Equals(ScriptType.UInt)) Marshal.WriteInt32(p, unchecked((int)Convert.ToUInt32(value)));
        else if (elemType.Equals(ScriptType.UInt64)) Marshal.WriteInt64(p, (long)Convert.ToUInt64(value));
        else if (elemType.Equals(ScriptType.Ptr)) Marshal.WriteIntPtr(p, (IntPtr)value);
        else if (elemType.Equals(ScriptType.Double))
            Marshal.WriteInt64(p, BitConverter.DoubleToInt64Bits(Convert.ToDouble(value)));
        else if (elemType.Equals(ScriptType.String))
            Marshal.WriteIntPtr(p, MarshalStringToNative((string)value));
    }

    public object GetField(EcsFieldDef f) => GetFieldElement(f, 0);

    public object GetFieldElement(EcsFieldDef f, int index)
    {
        var elemType = TypeLayout.GetElementType(f.FieldType);
        int elemSize = TypeLayout.GetNativeSize(elemType);
        IntPtr p = _ptr + f.Offset + index * elemSize;

        if (elemType.Equals(ScriptType.Byte)) return Marshal.ReadByte(p);
        if (elemType.Equals(ScriptType.Int)) return Marshal.ReadInt32(p);
        if (elemType.Equals(ScriptType.Bool)) return Marshal.ReadInt32(p) != 0;
        if (elemType.Equals(ScriptType.UInt)) return unchecked((uint)Marshal.ReadInt32(p));
        if (elemType.Equals(ScriptType.UInt64)) return (ulong)Marshal.ReadInt64(p);
        if (elemType.Equals(ScriptType.Ptr)) return Marshal.ReadIntPtr(p);
        if (elemType.Equals(ScriptType.Double)) return BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p));
        if (elemType.Equals(ScriptType.String)) return PtrToString(Marshal.ReadIntPtr(p));
        throw new NotSupportedException($"不支持的字段类型: {elemType}");
    }

    public EcsStruct GetNested(EcsFieldDef f, int index = 0)
    {
        var elemType = TypeLayout.GetElementType(f.FieldType);
        int elemSize = TypeLayout.GetNativeSize(elemType);
        return new EcsStruct(((StructType)elemType).Definition, _ptr + f.Offset + index * elemSize, ownsMemory: false);
    }

    public object[] GetFieldAsArray(EcsFieldDef f)
    {
        if (f.FieldType is not FixedArrayType fat)
            return [GetFieldElement(f, 0)];
        var result = new object[fat.Count];
        for (int i = 0; i < fat.Count; i++)
            result[i] = GetFieldElement(f, i);
        return result;
    }

    EcsFieldDef GetField(string name) =>
        _def.Fields.FirstOrDefault(f => f.Name == name)
        ?? throw new KeyNotFoundException(name);

    public void Set(string name, object value) => SetField(GetField(name), value);
    public object Get(string name) => GetField(GetField(name));

    public void Dispose()
    {
        if (_ownsMemory && _ptr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_ptr);
            _ptr = IntPtr.Zero;
        }
    }

    static void ZeroMemory(IntPtr p, int size)
    {
        for (int i = 0; i < size; i += 8)
            Marshal.WriteInt64(p + i, 0);
        // Handle remaining bytes
        for (int i = (size / 8) * 8; i < size; i++)
            Marshal.WriteByte(p + i, 0);
    }

    static void SafeCopy(IntPtr dst, IntPtr src, int count)
    {
        for (int i = 0; i + 8 <= count; i += 8)
            Marshal.WriteInt64(dst + i, Marshal.ReadInt64(src + i));
        for (int i = (count / 8) * 8; i < count; i++)
            Marshal.WriteByte(dst + i, Marshal.ReadByte(src + i));
    }

    static IntPtr MarshalStringToNative(string value) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Marshal.StringToHGlobalUni(value)
            : Marshal.StringToHGlobalAnsi(value);

    static string PtrToString(IntPtr ptr) =>
        ptr == IntPtr.Zero ? string.Empty
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Marshal.PtrToStringUni(ptr)!
            : Marshal.PtrToStringAnsi(ptr)!;
}