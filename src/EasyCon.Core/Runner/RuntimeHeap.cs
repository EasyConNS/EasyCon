using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;

namespace EasyCon.Core.Runner;

/// <summary>
/// Handle-based 运行时堆，管理 string、ScriptArray、EcsStruct 的生命周期。
/// Handle 从 1 开始，0 表示 null/空。
/// </summary>
internal sealed class RuntimeHeap
{
    private const byte TAG_FREE = 0;
    private const byte TAG_STRING = 1;
    private const byte TAG_ARRAY = 2;
    private const byte TAG_STRUCT = 3;

    private string?[] _strings = new string?[16];
    private ScriptArray?[] _arrays = new ScriptArray?[16];
    private EcsStruct?[] _structs = new EcsStruct?[16];
    private byte[] _tags = new byte[16];
    private readonly Stack<int> _freeList = [];
    private int _count;

    public int StoreString(string value)
    {
        int handle = AllocSlot();
        _strings[handle] = value;
        _tags[handle] = TAG_STRING;
        return handle;
    }

    public int StoreArray(ScriptArray array)
    {
        int handle = AllocSlot();
        _arrays[handle] = array;
        _tags[handle] = TAG_ARRAY;
        return handle;
    }

    public int StoreStruct(EcsStruct s)
    {
        int handle = AllocSlot();
        _structs[handle] = s;
        _tags[handle] = TAG_STRUCT;
        return handle;
    }

    /// <summary>
    /// 存储 Value 的引用类型数据（深拷贝），返回 handle。
    /// </summary>
    public int StoreDeepCopy(ScriptType type, Value value)
    {
        if (type.Equals(ScriptType.String))
            return StoreString(value.AsString());

        if (type is ArrayType)
            return StoreArray(value.AsArray().Clone());

        if (type is StructType)
        {
            var src = value.AsStruct();
            var clone = new EcsStruct(src.Definition, src.NativePtr);
            return StoreStruct(clone);
        }

        throw new InvalidOperationException($"不支持 handle 存储: {type}");
    }

    public string GetString(int handle) =>
        handle > 0 && handle < _tags.Length && _tags[handle] == TAG_STRING
            ? _strings[handle] ?? string.Empty
            : string.Empty;

    public ScriptArray GetArray(int handle) =>
        handle > 0 && handle < _tags.Length && _tags[handle] == TAG_ARRAY
            ? _arrays[handle]!
            : throw new InvalidOperationException($"无效数组 handle: {handle}");

    public EcsStruct GetStruct(int handle) =>
        handle > 0 && handle < _tags.Length && _tags[handle] == TAG_STRUCT
            ? _structs[handle]!
            : throw new InvalidOperationException($"无效结构体 handle: {handle}");

    /// <summary>
    /// 根据 handle + 类型还原为原始对象（string/ScriptArray/EcsStruct），供类型化缓存使用
    /// </summary>
    public object? DerefObject(int handle, ScriptType type)
    {
        if (handle == 0)
        {
            if (type.Equals(ScriptType.String)) return string.Empty;
            return null;
        }

        return _tags[handle] switch
        {
            TAG_STRING => _strings[handle] ?? string.Empty,
            TAG_ARRAY => _arrays[handle],
            TAG_STRUCT => _structs[handle],
            _ => null
        };
    }

    /// <summary>
    /// 根据 handle + 类型还原为 Value
    /// </summary>
    public Value Deref(int handle, ScriptType type)
    {
        if (handle == 0)
        {
            if (type.Equals(ScriptType.String)) return Value.FromString(string.Empty);
            return Value.Void;
        }

        return _tags[handle] switch
        {
            TAG_STRING => Value.FromString(_strings[handle]!),
            TAG_ARRAY => Value.FromArray(_arrays[handle]!, ((ArrayType)type).ElementType),
            TAG_STRUCT => Value.FromStruct(_structs[handle]!),
            _ => throw new InvalidOperationException($"handle {handle} 类型标记无效: {_tags[handle]}")
        };
    }

    /// <summary>
    /// 深拷贝 handle 指向的数据，返回新 handle
    /// </summary>
    public int DeepCopyHandle(int handle)
    {
        if (handle == 0) return 0;

        return _tags[handle] switch
        {
            TAG_STRING => StoreString(_strings[handle]!), // string 不可变，直接共享引用
            TAG_ARRAY => StoreArray(_arrays[handle]!.Clone()),
            TAG_STRUCT => StoreStruct(new EcsStruct(_structs[handle]!.Definition, _structs[handle]!.NativePtr)),
            _ => throw new InvalidOperationException($"handle {handle} 类型标记无效")
        };
    }

    public void Free(int handle)
    {
        if (handle <= 0 || handle >= _tags.Length || _tags[handle] == TAG_FREE) return;

        if (_tags[handle] == TAG_STRUCT)
        {
            _structs[handle]?.Dispose();
            _structs[handle] = null;
        }
        else if (_tags[handle] == TAG_ARRAY)
        {
            _arrays[handle] = null;
        }
        else if (_tags[handle] == TAG_STRING)
        {
            _strings[handle] = null;
        }

        _tags[handle] = TAG_FREE;
        _freeList.Push(handle);
    }

    public void FreeAll(ReadOnlySpan<int> handles)
    {
        foreach (ref readonly var h in handles)
            Free(h);
    }

    /// <summary>
    /// 释放所有剩余 handle（Evaluator Dispose 时调用）
    /// </summary>
    public void FreeAll()
    {
        for (int i = 1; i < _tags.Length; i++)
        {
            if (_tags[i] != TAG_FREE)
            {
                if (_tags[i] == TAG_STRUCT) _structs[i]?.Dispose();
                _strings[i] = null;
                _arrays[i] = null;
                _structs[i] = null;
                _tags[i] = TAG_FREE;
            }
        }
        _freeList.Clear();
        _count = 0;
    }

    private int AllocSlot()
    {
        if (_freeList.TryPop(out int slot))
            return slot;

        _count++;
        if (_count >= _tags.Length)
            Grow();

        return _count;
    }

    private void Grow()
    {
        int newSize = _tags.Length * 2;
        Array.Resize(ref _strings, newSize);
        Array.Resize(ref _arrays, newSize);
        Array.Resize(ref _structs, newSize);
        Array.Resize(ref _tags, newSize);
    }
}