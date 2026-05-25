using EasyCon.Script.Symbols;

namespace EasyCon.Script.Runtime;

/// <summary>
/// 强类型脚本数组，替代 ImmutableList<Value>。
/// <see cref="SetItem"/> 原地修改；<see cref="Append"/>/<see cref="AddRange"/>/<see cref="GetRange"/> 返回新实例。
/// </summary>
public abstract class ScriptArray
{
    public abstract ScriptType ElementType { get; }
    public abstract int Length { get; }
    public abstract Value this[int index] { get; }

    /// <summary>原地修改指定索引的元素</summary>
    public abstract void SetItem(int index, Value value);

    /// <summary>深拷贝，用于赋值语义</summary>
    public abstract ScriptArray Clone();

    /// <summary>追加一个元素，返回新数组</summary>
    public abstract ScriptArray Append(Value value);

    /// <summary>连接另一个数组，返回新数组</summary>
    public abstract ScriptArray AddRange(ScriptArray other);

    /// <summary>切片，返回新数组</summary>
    public abstract ScriptArray GetRange(int offset, int count);

    /// <summary>是否包含指定元素</summary>
    public abstract bool Contains(Value item);

    /// <summary>
    /// 根据元素类型创建对应的强类型数组实例
    /// </summary>
    public static ScriptArray Create(ScriptType elementType, IReadOnlyList<Value> elements)
    {
        if (elementType.Equals(ScriptType.Int))
            return new IntArray(elements, elementType);
        if (elementType.Equals(ScriptType.Byte))
            return new ByteArray(elements, elementType);
        if (elementType.Equals(ScriptType.Bool))
            return new BoolArray(elements, elementType);
        if (elementType.Equals(ScriptType.UInt))
            return new UIntArray(elements, elementType);
        if (elementType.Equals(ScriptType.UInt64))
            return new UInt64Array(elements, elementType);
        if (elementType.Equals(ScriptType.Double))
            return new DoubleArray(elements, elementType);
        if (elementType.Equals(ScriptType.String))
            return new StringArray(elements, elementType);
        if (elementType.Equals(ScriptType.Ptr))
            return new LongArray(elements, elementType);
        return new ValueArray(elements, elementType);
    }

    internal static ScriptType InferElementType(IReadOnlyList<Value> elements)
    {
        return elements.Count > 0 ? elements[0].Type : ScriptType.Int;
    }
}

public sealed class IntArray : ScriptArray
{
    private readonly int[] _data;
    private readonly ScriptType _elementType;

    public override ScriptType ElementType => _elementType;
    public override int Length => _data.Length;
    public override Value this[int index] => Value.FromInt(_data[index]);

    public IntArray(int[] data, ScriptType elementType) { _data = data; _elementType = elementType; }
    public IntArray(IReadOnlyList<Value> elements, ScriptType elementType)
    {
        _elementType = elementType;
        _data = new int[elements.Count];
        for (int i = 0; i < elements.Count; i++) _data[i] = elements[i].AsInt();
    }

    public override void SetItem(int index, Value value) => _data[index] = value.AsInt();
    public override ScriptArray Clone() => new IntArray((int[])_data.Clone(), _elementType);
    public override ScriptArray Append(Value value)
    {
        var newArr = new int[_data.Length + 1];
        Array.Copy(_data, newArr, _data.Length);
        newArr[_data.Length] = value.AsInt();
        return new IntArray(newArr, _elementType);
    }
    public override ScriptArray AddRange(ScriptArray other)
    {
        var o = (IntArray)other;
        var newArr = new int[_data.Length + o._data.Length];
        Array.Copy(_data, newArr, _data.Length);
        Array.Copy(o._data, 0, newArr, _data.Length, o._data.Length);
        return new IntArray(newArr, _elementType);
    }
    public override ScriptArray GetRange(int offset, int count)
    {
        var newArr = new int[count];
        Array.Copy(_data, offset, newArr, 0, count);
        return new IntArray(newArr, _elementType);
    }
    public override bool Contains(Value item) => Array.IndexOf(_data, item.AsInt()) >= 0;
}

public sealed class BoolArray : ScriptArray
{
    private readonly int[] _data;
    private readonly ScriptType _elementType;

    public override ScriptType ElementType => _elementType;
    public override int Length => _data.Length;
    public override Value this[int index] => Value.FromBool(_data[index] != 0);

    public BoolArray(int[] data, ScriptType elementType) { _data = data; _elementType = elementType; }
    public BoolArray(IReadOnlyList<Value> elements, ScriptType elementType)
    {
        _elementType = elementType;
        _data = new int[elements.Count];
        for (int i = 0; i < elements.Count; i++) _data[i] = elements[i].AsBool() ? 1 : 0;
    }

    public override void SetItem(int index, Value value) => _data[index] = value.AsBool() ? 1 : 0;
    public override ScriptArray Clone() => new BoolArray((int[])_data.Clone(), _elementType);
    public override ScriptArray Append(Value value)
    {
        var newArr = new int[_data.Length + 1];
        Array.Copy(_data, newArr, _data.Length);
        newArr[_data.Length] = value.AsBool() ? 1 : 0;
        return new BoolArray(newArr, _elementType);
    }
    public override ScriptArray AddRange(ScriptArray other)
    {
        var o = (BoolArray)other;
        var newArr = new int[_data.Length + o._data.Length];
        Array.Copy(_data, newArr, _data.Length);
        Array.Copy(o._data, 0, newArr, _data.Length, o._data.Length);
        return new BoolArray(newArr, _elementType);
    }
    public override ScriptArray GetRange(int offset, int count)
    {
        var newArr = new int[count];
        Array.Copy(_data, offset, newArr, 0, count);
        return new BoolArray(newArr, _elementType);
    }
    public override bool Contains(Value item)
    {
        var target = item.AsBool() ? 1 : 0;
        return Array.IndexOf(_data, target) >= 0;
    }
}

public sealed class ByteArray : ScriptArray
{
    private readonly byte[] _data;
    private readonly ScriptType _elementType;

    public override ScriptType ElementType => _elementType;
    public override int Length => _data.Length;
    public override Value this[int index] => Value.FromByte(_data[index]);

    public ByteArray(byte[] data, ScriptType elementType) { _data = data; _elementType = elementType; }
    public ByteArray(IReadOnlyList<Value> elements, ScriptType elementType)
    {
        _elementType = elementType;
        _data = new byte[elements.Count];
        for (int i = 0; i < elements.Count; i++) _data[i] = elements[i].AsByte();
    }

    public override void SetItem(int index, Value value) => _data[index] = value.AsByte();
    public override ScriptArray Clone() => new ByteArray((byte[])_data.Clone(), _elementType);
    public override ScriptArray Append(Value value)
    {
        var newArr = new byte[_data.Length + 1];
        Array.Copy(_data, newArr, _data.Length);
        newArr[_data.Length] = value.AsByte();
        return new ByteArray(newArr, _elementType);
    }
    public override ScriptArray AddRange(ScriptArray other)
    {
        var o = (ByteArray)other;
        var newArr = new byte[_data.Length + o._data.Length];
        Array.Copy(_data, newArr, _data.Length);
        Array.Copy(o._data, 0, newArr, _data.Length, o._data.Length);
        return new ByteArray(newArr, _elementType);
    }
    public override ScriptArray GetRange(int offset, int count)
    {
        var newArr = new byte[count];
        Array.Copy(_data, offset, newArr, 0, count);
        return new ByteArray(newArr, _elementType);
    }
    public override bool Contains(Value item) => Array.IndexOf(_data, item.AsByte()) >= 0;
}

public sealed class UIntArray : ScriptArray
{
    private readonly uint[] _data;
    private readonly ScriptType _elementType;

    public override ScriptType ElementType => _elementType;
    public override int Length => _data.Length;
    public override Value this[int index] => Value.FromUInt(_data[index]);

    public UIntArray(uint[] data, ScriptType elementType) { _data = data; _elementType = elementType; }
    public UIntArray(IReadOnlyList<Value> elements, ScriptType elementType)
    {
        _elementType = elementType;
        _data = new uint[elements.Count];
        for (int i = 0; i < elements.Count; i++) _data[i] = elements[i].AsUInt();
    }

    public override void SetItem(int index, Value value) => _data[index] = value.AsUInt();
    public override ScriptArray Clone() => new UIntArray((uint[])_data.Clone(), _elementType);
    public override ScriptArray Append(Value value)
    {
        var newArr = new uint[_data.Length + 1];
        Array.Copy(_data, newArr, _data.Length);
        newArr[_data.Length] = value.AsUInt();
        return new UIntArray(newArr, _elementType);
    }
    public override ScriptArray AddRange(ScriptArray other)
    {
        var o = (UIntArray)other;
        var newArr = new uint[_data.Length + o._data.Length];
        Array.Copy(_data, newArr, _data.Length);
        Array.Copy(o._data, 0, newArr, _data.Length, o._data.Length);
        return new UIntArray(newArr, _elementType);
    }
    public override ScriptArray GetRange(int offset, int count)
    {
        var newArr = new uint[count];
        Array.Copy(_data, offset, newArr, 0, count);
        return new UIntArray(newArr, _elementType);
    }
    public override bool Contains(Value item) => Array.IndexOf(_data, item.AsUInt()) >= 0;
}

public sealed class UInt64Array : ScriptArray
{
    private readonly ulong[] _data;
    private readonly ScriptType _elementType;

    public override ScriptType ElementType => _elementType;
    public override int Length => _data.Length;
    public override Value this[int index] => Value.FromUInt64(_data[index]);

    public UInt64Array(ulong[] data, ScriptType elementType) { _data = data; _elementType = elementType; }
    public UInt64Array(IReadOnlyList<Value> elements, ScriptType elementType)
    {
        _elementType = elementType;
        _data = new ulong[elements.Count];
        for (int i = 0; i < elements.Count; i++) _data[i] = elements[i].AsUInt64();
    }

    public override void SetItem(int index, Value value) => _data[index] = value.AsUInt64();
    public override ScriptArray Clone() => new UInt64Array((ulong[])_data.Clone(), _elementType);
    public override ScriptArray Append(Value value)
    {
        var newArr = new ulong[_data.Length + 1];
        Array.Copy(_data, newArr, _data.Length);
        newArr[_data.Length] = value.AsUInt64();
        return new UInt64Array(newArr, _elementType);
    }
    public override ScriptArray AddRange(ScriptArray other)
    {
        var o = (UInt64Array)other;
        var newArr = new ulong[_data.Length + o._data.Length];
        Array.Copy(_data, newArr, _data.Length);
        Array.Copy(o._data, 0, newArr, _data.Length, o._data.Length);
        return new UInt64Array(newArr, _elementType);
    }
    public override ScriptArray GetRange(int offset, int count)
    {
        var newArr = new ulong[count];
        Array.Copy(_data, offset, newArr, 0, count);
        return new UInt64Array(newArr, _elementType);
    }
    public override bool Contains(Value item) => Array.IndexOf(_data, item.AsUInt64()) >= 0;
}

public sealed class DoubleArray : ScriptArray
{
    private readonly double[] _data;
    private readonly ScriptType _elementType;

    public override ScriptType ElementType => _elementType;
    public override int Length => _data.Length;
    public override Value this[int index] => Value.FromDouble(_data[index]);

    public DoubleArray(double[] data, ScriptType elementType) { _data = data; _elementType = elementType; }
    public DoubleArray(IReadOnlyList<Value> elements, ScriptType elementType)
    {
        _elementType = elementType;
        _data = new double[elements.Count];
        for (int i = 0; i < elements.Count; i++) _data[i] = elements[i].AsDouble();
    }

    public override void SetItem(int index, Value value) => _data[index] = value.AsDouble();
    public override ScriptArray Clone() => new DoubleArray((double[])_data.Clone(), _elementType);
    public override ScriptArray Append(Value value)
    {
        var newArr = new double[_data.Length + 1];
        Array.Copy(_data, newArr, _data.Length);
        newArr[_data.Length] = value.AsDouble();
        return new DoubleArray(newArr, _elementType);
    }
    public override ScriptArray AddRange(ScriptArray other)
    {
        var o = (DoubleArray)other;
        var newArr = new double[_data.Length + o._data.Length];
        Array.Copy(_data, newArr, _data.Length);
        Array.Copy(o._data, 0, newArr, _data.Length, o._data.Length);
        return new DoubleArray(newArr, _elementType);
    }
    public override ScriptArray GetRange(int offset, int count)
    {
        var newArr = new double[count];
        Array.Copy(_data, offset, newArr, 0, count);
        return new DoubleArray(newArr, _elementType);
    }
    public override bool Contains(Value item) => Array.IndexOf(_data, item.AsDouble()) >= 0;
}

public sealed class StringArray : ScriptArray
{
    private readonly string?[] _data;
    private readonly ScriptType _elementType;

    public override ScriptType ElementType => _elementType;
    public override int Length => _data.Length;
    public override Value this[int index] => Value.FromString(_data[index]!);

    public StringArray(string?[] data, ScriptType elementType) { _data = data; _elementType = elementType; }
    public StringArray(IReadOnlyList<Value> elements, ScriptType elementType)
    {
        _elementType = elementType;
        _data = new string[elements.Count];
        for (int i = 0; i < elements.Count; i++) _data[i] = elements[i].AsString();
    }

    public override void SetItem(int index, Value value) => _data[index] = value.AsString();
    public override ScriptArray Clone()
    {
        var newArr = new string?[_data.Length];
        Array.Copy(_data, newArr, _data.Length);
        return new StringArray(newArr, _elementType);
    }
    public override ScriptArray Append(Value value)
    {
        var newArr = new string?[_data.Length + 1];
        Array.Copy(_data, newArr, _data.Length);
        newArr[_data.Length] = value.AsString();
        return new StringArray(newArr, _elementType);
    }
    public override ScriptArray AddRange(ScriptArray other)
    {
        var o = (StringArray)other;
        var newArr = new string?[_data.Length + o._data.Length];
        Array.Copy(_data, newArr, _data.Length);
        Array.Copy(o._data, 0, newArr, _data.Length, o._data.Length);
        return new StringArray(newArr, _elementType);
    }
    public override ScriptArray GetRange(int offset, int count)
    {
        var newArr = new string?[count];
        Array.Copy(_data, offset, newArr, 0, count);
        return new StringArray(newArr, _elementType);
    }
    public override bool Contains(Value item)
    {
        var target = item.AsString();
        for (int i = 0; i < _data.Length; i++)
            if (string.Equals(_data[i], target, StringComparison.Ordinal)) return true;
        return false;
    }
}

public sealed class LongArray : ScriptArray
{
    private readonly long[] _data;
    private readonly ScriptType _elementType;

    public override ScriptType ElementType => _elementType;
    public override int Length => _data.Length;
    public override Value this[int index] => Value.FromPtr(_data[index]);

    public LongArray(long[] data, ScriptType elementType) { _data = data; _elementType = elementType; }
    public LongArray(IReadOnlyList<Value> elements, ScriptType elementType)
    {
        _elementType = elementType;
        _data = new long[elements.Count];
        for (int i = 0; i < elements.Count; i++) _data[i] = elements[i].AsPtr();
    }

    public override void SetItem(int index, Value value) => _data[index] = value.AsPtr();
    public override ScriptArray Clone() => new LongArray((long[])_data.Clone(), _elementType);
    public override ScriptArray Append(Value value)
    {
        var newArr = new long[_data.Length + 1];
        Array.Copy(_data, newArr, _data.Length);
        newArr[_data.Length] = value.AsPtr();
        return new LongArray(newArr, _elementType);
    }
    public override ScriptArray AddRange(ScriptArray other)
    {
        var o = (LongArray)other;
        var newArr = new long[_data.Length + o._data.Length];
        Array.Copy(_data, newArr, _data.Length);
        Array.Copy(o._data, 0, newArr, _data.Length, o._data.Length);
        return new LongArray(newArr, _elementType);
    }
    public override ScriptArray GetRange(int offset, int count)
    {
        var newArr = new long[count];
        Array.Copy(_data, offset, newArr, 0, count);
        return new LongArray(newArr, _elementType);
    }
    public override bool Contains(Value item) => Array.IndexOf(_data, item.AsPtr()) >= 0;
}

/// <summary>
/// 通用数组，用于 struct、ptr 等无专用实现的元素类型
/// </summary>
public sealed class ValueArray : ScriptArray
{
    private readonly Value[] _data;
    private readonly ScriptType _elementType;

    public override ScriptType ElementType => _elementType;
    public override int Length => _data.Length;
    public override Value this[int index] => _data[index];

    public ValueArray(Value[] data, ScriptType elementType) { _data = data; _elementType = elementType; }
    public ValueArray(IReadOnlyList<Value> elements, ScriptType elementType)
    {
        _elementType = elementType;
        _data = new Value[elements.Count];
        for (int i = 0; i < elements.Count; i++) _data[i] = elements[i];
    }

    public override void SetItem(int index, Value value) => _data[index] = value;
    public override ScriptArray Clone()
    {
        var newArr = new Value[_data.Length];
        Array.Copy(_data, newArr, _data.Length);
        return new ValueArray(newArr, _elementType);
    }
    public override ScriptArray Append(Value value)
    {
        var newArr = new Value[_data.Length + 1];
        Array.Copy(_data, newArr, _data.Length);
        newArr[_data.Length] = value;
        return new ValueArray(newArr, _elementType);
    }
    public override ScriptArray AddRange(ScriptArray other)
    {
        var o = (ValueArray)other;
        var newArr = new Value[_data.Length + o._data.Length];
        Array.Copy(_data, newArr, _data.Length);
        Array.Copy(o._data, 0, newArr, _data.Length, o._data.Length);
        return new ValueArray(newArr, _elementType);
    }
    public override ScriptArray GetRange(int offset, int count)
    {
        var newArr = new Value[count];
        Array.Copy(_data, offset, newArr, 0, count);
        return new ValueArray(newArr, _elementType);
    }
    public override bool Contains(Value item)
    {
        for (int i = 0; i < _data.Length; i++)
            if (_data[i].Equals(item)) return true;
        return false;
    }
}