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

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        for (int i = 0; i < Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(this[i].ToObject());
        }
        sb.Append(']');
        return sb.ToString();
    }

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
    /// 根据元素类型创建对应的强类型数组实例。
    /// 字符串数组需要 IStringHandleStore 以 handle 形式存储（参考 Python intern + LuaJIT GCstr）。
    /// </summary>
    public static ScriptArray Create(ScriptType elementType, IReadOnlyList<Value> elements, IStringHandleStore? stringStore = null)
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
            return new StringArray(elements, elementType, stringStore ?? throw new ArgumentNullException(nameof(stringStore), "字符串数组需要 IStringHandleStore"));
        if (elementType.Equals(ScriptType.Ptr))
            return new LongArray(elements, elementType);
        return new ValueArray(elements, elementType);
    }

    /// <summary>
    /// 从类型化数组直接构造，跳过 Value 中间层。用于编译期预计算。
    /// 字符串元素以 int[] handle 形式提供（已通过 IStringHandleStore.Intern 驻留）。
    /// </summary>
    public static ScriptArray CreateDirect(ScriptType elementType, object typedData, IStringHandleStore? stringStore = null)
    {
        if (elementType.Equals(ScriptType.Int))
            return new IntArray((int[])typedData, elementType);
        if (elementType.Equals(ScriptType.Bool))
            return new BoolArray((int[])typedData, elementType);
        if (elementType.Equals(ScriptType.Byte))
            return new ByteArray((byte[])typedData, elementType);
        if (elementType.Equals(ScriptType.UInt))
            return new UIntArray((uint[])typedData, elementType);
        if (elementType.Equals(ScriptType.UInt64))
            return new UInt64Array((ulong[])typedData, elementType);
        if (elementType.Equals(ScriptType.Double))
            return new DoubleArray((double[])typedData, elementType);
        if (elementType.Equals(ScriptType.String))
            return new StringArray((int[])typedData, elementType, stringStore ?? throw new ArgumentNullException(nameof(stringStore), "字符串数组需要 IStringHandleStore"));
        if (elementType.Equals(ScriptType.Ptr))
            return new LongArray((long[])typedData, elementType);
        throw new InvalidOperationException($"无类型化数组实现: {elementType}");
    }

    /// <summary>是否包含需要释放的嵌套 handle（如 StringArray 持有字符串 handle）。</summary>
    public virtual bool HasNestedHandles => false;

    /// <summary>
    /// 释放该数组持有的所有嵌套 handle（由 RuntimeHeap 在 Free 数组时回调）。
    /// 默认实现为空；StringArray 等持有 handle 的子类重写。
    /// </summary>
    public virtual void FreeNestedHandles() { }

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

/// <summary>
/// Handle 式字符串数组：内部存储 int[] handle，通过 IStringHandleStore 解引用。
/// 参考 Python list of PyObject*（每槽位是指针）+ LuaJIT 全局 GCstr 驻留。
///
/// 优势：
///   1. 内存紧凑：int[] (4B/slot) vs string[] (8B/slot)
///   2. Clone/GetRange 时为每个 handle 分配独立副本（Copy），值语义安全
///   3. Contains 优先 handle 整数比较（intern 后相同内容 → 相同 handle）
///   4. 跨缓存边界零开销：与 TaggedValue.FromStringHandle 完全对齐
///
/// 注意：StringArray 的 handle 依赖 IStringHandleStore（RuntimeHeap）的生命周期。
/// 调用者在检查返回的 ScriptArray 内容时，evaluator 必须仍存活。
/// </summary>
public sealed class StringArray : ScriptArray
{
    private int[] _handles;
    private readonly ScriptType _elementType;
    private readonly IStringHandleStore _store;

    public override ScriptType ElementType => _elementType;
    public override int Length => _handles.Length;
    public override Value this[int index] => Value.FromString(_store.Get(_handles[index]));

    /// <summary>从已有的 handle 数组直接构造（共享 handle，不深拷贝）。</summary>
    public StringArray(int[] handles, ScriptType elementType, IStringHandleStore store)
    {
        _handles = handles;
        _elementType = elementType;
        _store = store;
    }

    public StringArray(IReadOnlyList<Value> elements, ScriptType elementType, IStringHandleStore store)
    {
        _elementType = elementType;
        _store = store;
        _handles = new int[elements.Count];
        for (int i = 0; i < elements.Count; i++) _handles[i] = store.Intern(elements[i].AsString());
    }

    public override void SetItem(int index, Value value) => _handles[index] = _store.Intern(value.AsString());

    /// <summary>值语义深拷贝：为每个 handle 分配独立副本（Copy），避免释放时悬空。</summary>
    public override ScriptArray Clone()
    {
        var newArr = new int[_handles.Length];
        for (int i = 0; i < _handles.Length; i++) newArr[i] = _store.Copy(_handles[i]);
        return new StringArray(newArr, _elementType, _store);
    }

    public override ScriptArray Append(Value value)
    {
        var newArr = new int[_handles.Length + 1];
        for (int i = 0; i < _handles.Length; i++) newArr[i] = _store.Copy(_handles[i]);
        newArr[_handles.Length] = _store.Intern(value.AsString());
        return new StringArray(newArr, _elementType, _store);
    }

    public override ScriptArray AddRange(ScriptArray other)
    {
        var o = (StringArray)other;
        var newArr = new int[_handles.Length + o._handles.Length];
        for (int i = 0; i < _handles.Length; i++) newArr[i] = _store.Copy(_handles[i]);
        for (int i = 0; i < o._handles.Length; i++) newArr[_handles.Length + i] = o._store.Copy(o._handles[i]);
        return new StringArray(newArr, _elementType, _store);
    }

    public override ScriptArray GetRange(int offset, int count)
    {
        var newArr = new int[count];
        for (int i = 0; i < count; i++) newArr[i] = _store.Copy(_handles[offset + i]);
        return new StringArray(newArr, _elementType, _store);
    }

    /// <summary>
    /// 包含检查：参考 Python/LuaJIT 的 intern 快速路径。
    /// intern 后相同内容 → 相同 handle，直接整数比较 O(n)。
    /// </summary>
    public override bool Contains(Value item)
    {
        var target = _store.Intern(item.AsString());
        return Array.IndexOf(_handles, target) >= 0;
    }

    public override bool HasNestedHandles => true;

    /// <summary>释放该数组持有的所有字符串 handle（由 RuntimeHeap 在 Free 数组时回调）。</summary>
    public override void FreeNestedHandles()
    {
        for (int i = 0; i < _handles.Length; i++)
        {
            _store.Free(_handles[i]);
            _handles[i] = 0;
        }
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