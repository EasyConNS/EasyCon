using System.Runtime.InteropServices;

namespace EasyCon.Script.Runtime;

public enum EcsFieldType { Int, Bool, Ptr, Double, String, Struct }

public class EcsFieldDef
{
    public string Name;
    public EcsFieldType Type;
    public int Offset;
    public int Size;
    public int Alignment;
    public int ArrayCount = 1;
    public EcsStructDef? StructDef;
}

public class EcsStructDef
{
    public string Name;
    public List<EcsFieldDef> Fields = [];
    public int Size;
    public int Alignment;
}

public static class StructLayout
{
    public static void Calculate(EcsStructDef def)
    {
        int offset = 0, maxAlign = 1;

        foreach (var f in def.Fields)
        {
            int elemSize = f.Type switch
            {
                EcsFieldType.Int => 4,
                EcsFieldType.Bool => 4,
                EcsFieldType.Ptr => IntPtr.Size,
                EcsFieldType.Double => 8,
                EcsFieldType.String => IntPtr.Size,
                EcsFieldType.Struct => f.StructDef!.Size,
                _ => 4
            };
            int align = f.Type == EcsFieldType.Struct
                ? f.StructDef!.Alignment
                : elemSize;

            offset = AlignUp(offset, align);

            f.Offset = offset;
            f.Size = elemSize * f.ArrayCount;
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
        int elemSize = f.Size / f.ArrayCount;
        IntPtr p = _ptr + f.Offset + index * elemSize;

        if (f.Type == EcsFieldType.Struct)
        {
            var src = (EcsStruct)value;
            SafeCopy(p, src.NativePtr, f.StructDef!.Size);
            return;
        }

        switch (f.Type)
        {
            case EcsFieldType.Int: Marshal.WriteInt32(p, Convert.ToInt32(value)); break;
            case EcsFieldType.Bool: Marshal.WriteInt32(p, Convert.ToBoolean(value) ? 1 : 0); break;
            case EcsFieldType.Ptr: Marshal.WriteIntPtr(p, (IntPtr)value); break;
            case EcsFieldType.Double:
                Marshal.WriteInt64(p, BitConverter.DoubleToInt64Bits(Convert.ToDouble(value)));
                break;
            case EcsFieldType.String:
                Marshal.WriteIntPtr(p, MarshalStringToNative((string)value));
                break;
        }
    }

    public object GetField(EcsFieldDef f) => GetFieldElement(f, 0);

    public object GetFieldElement(EcsFieldDef f, int index)
    {
        int elemSize = f.Size / f.ArrayCount;
        IntPtr p = _ptr + f.Offset + index * elemSize;

        return f.Type switch
        {
            EcsFieldType.Int => Marshal.ReadInt32(p),
            EcsFieldType.Bool => Marshal.ReadInt32(p) != 0,
            EcsFieldType.Ptr => Marshal.ReadIntPtr(p),
            EcsFieldType.Double => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p)),
            EcsFieldType.String => PtrToString(Marshal.ReadIntPtr(p)),
            _ => throw new NotSupportedException()
        };
    }

    public EcsStruct GetNested(EcsFieldDef f, int index = 0)
    {
        int elemSize = f.Size / f.ArrayCount;
        return new EcsStruct(f.StructDef!, _ptr + f.Offset + index * elemSize, ownsMemory: false);
    }

    public object[] GetFieldAsArray(EcsFieldDef f)
    {
        var result = new object[f.ArrayCount];
        for (int i = 0; i < f.ArrayCount; i++)
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