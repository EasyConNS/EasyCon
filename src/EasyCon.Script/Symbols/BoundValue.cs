using EasyCon.Script.Runtime;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace EasyCon.Script.Symbols;

/// <summary>
/// 表示一个动态值，可以是 int、bool、string 或一维数组（元素为 Value）。
/// 支持相等性比较、有序比较（同类型）和下标索引（字符串按 Unicode 字符，数组按元素）。
/// 零装箱设计：值类型直接存储在结构体字段中，不经过 object 装箱。
/// 显式布局：_intVal / _doubleVal / _longVal 三者互斥，共享偏移 [4]，结构体 32B（有效载荷 20B）。
/// 注意：非 readonly struct（因显式布局重叠字段不能同时为 readonly），但语义上仍为不可变值。
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct Value : IEquatable<Value>, IComparable<Value>
{
    // Tag 字节：标识当前存储的类型
    private const byte TAG_VOID = 0;
    private const byte TAG_BOOL = 1;
    private const byte TAG_BYTE = 2;
    private const byte TAG_INT32 = 3;
    private const byte TAG_UINT32 = 4;
    private const byte TAG_UINT64 = 5;
    private const byte TAG_FLOAT64 = 6;
    private const byte TAG_STRING = 7;
    private const byte TAG_ARRAY = 9;
    private const byte TAG_PTR = 10;
    private const byte TAG_STRUCT = 12;
    private const byte TAG_MAX = 15; // 预留 0-15 的 tag 范围，最高位保留用于扩展

    [FieldOffset(0)] private readonly byte _tag;
    [FieldOffset(4)] private int _int32Val;              // ┐ 也用于 bool (0/1)
    [FieldOffset(4)] private double f64Val;              // ├ 几者互斥，共享偏移
    [FieldOffset(4)] private long _longVal;              // ┘
    [FieldOffset(16)] private readonly object? _refVal;   // 8B 对齐（offset 12 有 4B padding）
    [FieldOffset(24)] private readonly ScriptType? _arrayElemType;

    // ArrayType 缓存，避免每次 Value.Type 访问分配新对象
    private static readonly Dictionary<ScriptType, ArrayType> _arrayTypeCache = [];

    // 仅设置不重叠的字段；_doubleVal / _longVal 由调用方在外层赋值
    private Value(byte tag, int intVal, object? refVal, ScriptType? arrayElemType)
    {
        _tag = tag;
        _int32Val = intVal;
        _refVal = refVal;
        _arrayElemType = arrayElemType;
    }

    /// <summary>当前值的类型（从 tag 计算，无额外存储）</summary>
    public ScriptType Type => _tag switch
    {
        TAG_VOID => ScriptType.Void,
        TAG_BOOL => ScriptType.Bool,
        TAG_BYTE => ScriptType.Byte,
        TAG_INT32 => ScriptType.Int,
        TAG_UINT32 => ScriptType.UInt,
        TAG_UINT64 => ScriptType.UInt64,
        TAG_FLOAT64 => ScriptType.Double,
        TAG_STRING => ScriptType.String,
        TAG_PTR => ScriptType.Ptr,
        TAG_ARRAY => ScriptType.ArrayOf(_arrayElemType!),
        TAG_STRUCT => new StructType((EcsStructDef)_refVal!),
        _ => ScriptType.Void
    };

    #region 工厂方法与转换

    public static Value From(object? o)
    {
        if (o == null) return Void;
        if (o is Value v) return v;

        return o switch
        {
            int i => FromInt(i),
            bool b => FromBool(b),
            byte b => FromByte(b),
            string s => FromString(s),
            double d => FromDouble(d),
            long l => FromPtr(l),
            uint ui => FromUInt(ui),
            ulong ul => FromUInt64(ul),
            IEnumerable<Value> list => CreateArray(
                list.FirstOrDefault().Type ?? ScriptType.Int,
                list
            ),
            EcsStruct es => FromStruct(es),
            _ => throw new ArgumentException($"无法将类型 {o.GetType()} 转换为脚本 Value")
        };
    }

    public static implicit operator Value(int v) => FromInt(v);
    public static implicit operator Value(bool v) => FromBool(v);
    public static implicit operator Value(string v) => FromString(v);
    public static implicit operator Value(double v) => FromDouble(v);
    public static implicit operator Value(long v) => FromPtr(v);
    public static implicit operator Value(uint v) => FromUInt(v);
    public static implicit operator Value(ulong v) => FromUInt64(v);

    public static Value FromBool(bool v) => new(TAG_BOOL, v ? 1 : 0, null, null);
    public static Value FromByte(byte v) => new(TAG_BYTE, v, null, null);
    public static Value FromInt(int v) => new(TAG_INT32, v, null, null);
    public static Value FromUInt(uint v) => new(TAG_UINT32, unchecked((int)v), null, null);
    public static Value FromUInt64(ulong v)
    {
        var val = new Value(TAG_UINT64, 0, null, null);
        val._longVal = (long)v;
        return val;
    }
    public static Value FromString(string v) => new(TAG_STRING, 0, v, null);

    public static Value FromDouble(double v)
    {
        var val = new Value(TAG_FLOAT64, 0, null, null);
        val.f64Val = v;
        return val;
    }

    public static Value FromPtr(long v)
    {
        var val = new Value(TAG_PTR, 0, null, null);
        val._longVal = v;
        return val;
    }

    public static Value Void => new(TAG_VOID, 0, null, null);

    public static Value FromStruct(EcsStruct v) => new(TAG_STRUCT, 0, v, null);
    public EcsStruct AsStruct() => _tag == TAG_STRUCT ? (EcsStruct)_refVal! : throw new InvalidCastException();
    internal bool TryGetStructPtr(out IntPtr ptr)
    {
        if (_tag == TAG_STRUCT && _refVal is EcsStruct s)
        {
            ptr = s.NativePtr;
            return true;
        }
        ptr = default;
        return false;
    }

    public static Value CreateArray(ScriptType elementType, IEnumerable<Value> elements)
    {
        var list = elements.ToList();
        foreach (var e in list)
        {
            if (!elementType.IsAssignableFrom(e.Type))
                throw new InvalidOperationException($"元素类型 {e.Type} 与数组声明类型 {elementType} 不匹配");
        }
        return new Value(TAG_ARRAY, 0, ScriptArray.Create(elementType, list), elementType);
    }

    /// <summary>
    /// 从已有的 ScriptArray 构建 Value（跳过元素验证，用于 RuntimeHeap 内部）
    /// </summary>
    internal static Value FromArray(ScriptArray array, ScriptType elementType) =>
        new(TAG_ARRAY, 0, array, elementType);

    public readonly int AsInt() => _tag == TAG_INT32 ? _int32Val : throw new InvalidCastException();
    public int ToInt() => _tag switch
    {
        TAG_INT32 => _int32Val,
        TAG_FLOAT64 => (int)f64Val,
        TAG_BOOL => _int32Val != 0 ? 1 : 0,
        _ => throw new InvalidCastException($"类型 {Type.Name} 无法转换为 <int>")
    };

    public readonly bool AsBool() => _tag == TAG_BOOL ? _int32Val != 0 : throw new InvalidCastException();
    public readonly string AsString() => _tag == TAG_STRING ? (string)_refVal! : throw new InvalidCastException();
    public ScriptArray AsArray() => _tag == TAG_ARRAY ? (ScriptArray)_refVal! : throw new InvalidCastException();
    public readonly double AsDouble() => _tag == TAG_FLOAT64 ? f64Val : throw new InvalidCastException();
    public double ToDouble() => _tag == TAG_INT32 ? (double)_int32Val : throw new InvalidCastException($"类型 {Type.Name} 无法转换为 <double>");
    public readonly long AsPtr() => _tag == TAG_PTR ? _longVal : throw new InvalidCastException();
    public readonly uint AsUInt() => _tag == TAG_UINT32 ? unchecked((uint)_int32Val) : throw new InvalidCastException();
    public readonly ulong AsUInt64() => _tag == TAG_UINT64 ? (ulong)_longVal : throw new InvalidCastException();
    public readonly byte AsByte() => _tag == TAG_BYTE ? (byte)_int32Val : throw new InvalidCastException();

    #endregion

    public object? ToObject() => _tag switch
    {
        TAG_BOOL => _int32Val != 0,
        TAG_BYTE => (byte)_int32Val,
        TAG_INT32 => _int32Val,
        TAG_UINT32 => unchecked((uint)_int32Val),
        TAG_UINT64 => (ulong)_longVal,
        TAG_FLOAT64 => f64Val,
        TAG_STRING => (string?)_refVal,
        TAG_PTR => _longVal,
        _ => null
    };

    #region 运算与索引

    public int Length => _tag switch
    {
        TAG_STRING => new StringInfo(((string)_refVal!)).LengthInTextElements,
        TAG_ARRAY => ((ScriptArray)_refVal!).Length,
        _ => 0
    };

    public Value this[int index]
    {
        get
        {
            if (_tag == TAG_STRING)
            {
                var str = (string)_refVal!;
                return FromString(str[index].ToString());
            }
            if (_tag == TAG_ARRAY)
            {
                return ((ScriptArray)_refVal!)[index];
            }
            throw new InvalidOperationException($"{Type} 不支持索引");
        }
    }

    public Value this[Range range]
    {
        get
        {
            if (_tag == TAG_STRING)
            {
                return FromString(((string)_refVal!)[range]);
            }
            if (_tag == TAG_ARRAY)
            {
                var arr = (ScriptArray)_refVal!;
                var (offset, length) = range.GetOffsetAndLength(arr.Length);
                return new Value(TAG_ARRAY, 0, arr.GetRange(offset, length), _arrayElemType);
            }
            throw new InvalidOperationException($"{Type} 不支持切片");
        }
    }

    public bool Contains(Value item)
    {
        if (_tag == TAG_STRING && item._tag == TAG_STRING)
            return ((string)_refVal!).Contains((string)item._refVal!, StringComparison.Ordinal);

        if (_tag == TAG_ARRAY)
        {
            if (!_arrayElemType!.Equals(item.Type))
                return false;

            return ((ScriptArray)_refVal!).Contains(item);
        }

        throw new InvalidOperationException($"{Type} 不支持包含运算");
    }

    #endregion

    #region 比较逻辑

    public bool Equals(Value other)
    {
        // PTR 与 INT 互相比较：允许 $handle == 0
        if ((_tag == TAG_PTR && other._tag == TAG_INT32) ||
            (_tag == TAG_INT32 && other._tag == TAG_PTR))
            return (_tag == TAG_PTR ? _longVal : _int32Val) == (other._tag == TAG_PTR ? other._longVal : other._int32Val);

        if (_tag != other._tag) return false;
        return _tag switch
        {
            TAG_VOID => true,
            TAG_BOOL => _int32Val == other._int32Val,
            TAG_BYTE => _int32Val == other._int32Val,
            TAG_INT32 => _int32Val == other._int32Val,
            TAG_UINT32 => _int32Val == other._int32Val,
            TAG_UINT64 => _longVal == other._longVal,
            TAG_FLOAT64 => f64Val == other.f64Val,
            TAG_PTR => _longVal == other._longVal,
            TAG_STRING => string.Equals((string)_refVal!, (string)other._refVal!, StringComparison.Ordinal),
            TAG_ARRAY => SequenceEqualArray((ScriptArray)_refVal!, (ScriptArray)other._refVal!),
            TAG_STRUCT => ReferenceEquals(_refVal, other._refVal),
            _ => false
        };
    }

    public int CompareTo(Value other)
    {
        // PTR 与 INT 互相比较
        if ((_tag == TAG_PTR && other._tag == TAG_INT32) ||
            (_tag == TAG_INT32 && other._tag == TAG_PTR))
        {
            var a = _tag == TAG_PTR ? _longVal : _int32Val;
            var b = other._tag == TAG_PTR ? other._longVal : other._int32Val;
            return a.CompareTo(b);
        }

        if (_tag != other._tag)
            throw new InvalidOperationException($"不同类型无法比较: {Type} 与 {other.Type}");

        return _tag switch
        {
            TAG_INT32 => _int32Val.CompareTo(other._int32Val),
            TAG_BYTE => _int32Val.CompareTo(other._int32Val),
            TAG_UINT32 => ((uint)_int32Val).CompareTo((uint)other._int32Val),
            TAG_UINT64 => ((ulong)_longVal).CompareTo((ulong)other._longVal),
            TAG_FLOAT64 => f64Val.CompareTo(other.f64Val),
            TAG_PTR => _longVal.CompareTo(other._longVal),
            TAG_STRING => string.Compare((string)_refVal!, (string)other._refVal!, StringComparison.Ordinal),
            _ => throw new InvalidOperationException($"类型不支持比较 <{Type}>与<{other.Type}>")
        };
    }

    public override bool Equals(object? obj) => obj is Value v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(_tag, _int32Val, f64Val, _longVal);

    public static bool operator ==(Value left, Value right) => left.Equals(right);
    public static bool operator !=(Value left, Value right) => !left.Equals(right);
    public static bool operator <(Value left, Value right) => left.CompareTo(right) < 0;
    public static bool operator >(Value left, Value right) => left.CompareTo(right) > 0;
    public static bool operator <=(Value left, Value right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Value left, Value right) => left.CompareTo(right) >= 0;

    #endregion

    public override string ToString() => _tag switch
    {
        TAG_ARRAY => ArrayToString((ScriptArray)_refVal!),
        TAG_FLOAT64 => f64Val.ToString(),
        TAG_BOOL => _int32Val != 0 ? "true" : "false",
        TAG_PTR => $"0x{_longVal:X}",
        TAG_STRING => (string)_refVal!,
        TAG_BYTE => _int32Val.ToString(),
        TAG_UINT32 => unchecked((uint)_int32Val).ToString(),
        TAG_UINT64 => ((ulong)_longVal).ToString(),
        TAG_INT32 => _int32Val.ToString(),
        TAG_STRUCT => $"struct:{((EcsStruct)_refVal!).Definition.Name}",
        _ => "void"
    };

    // 辅助属性：判断是否为 Array<T>
    private bool IsArray => _tag == TAG_ARRAY;
    private ScriptType? ElementType => _tag == TAG_ARRAY ? _arrayElemType : null;

    private static bool SequenceEqualArray(ScriptArray a, ScriptArray b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!a[i].Equals(b[i])) return false;
        return true;
    }

    private static string ArrayToString(ScriptArray arr)
    {
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < arr.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(arr[i].ToString());
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// 连接两个相同类型的数组
    /// </summary>
    public Value Concat(Value other)
    {
        if (_tag != TAG_ARRAY || other._tag != TAG_ARRAY)
            throw new InvalidOperationException("只有数组可以执行 Concat 操作");

        if (!Type.Equals(other.Type))
            throw new InvalidOperationException($"无法连接不同类型的数组：{Type} 和 {other.Type}");

        return new Value(TAG_ARRAY, 0, AsArray().AddRange(other.AsArray()), _arrayElemType);
    }

    /// <summary>
    /// 返回一个新数组，其中指定索引处的元素被替换为新值（copy-on-write）
    /// </summary>
    public void SetIndex(int index, Value newValue)
    {
        if (_tag != TAG_ARRAY)
            throw new InvalidOperationException("只有数组支持元素赋值");

        var arr = (ScriptArray)_refVal!;
        if (index < 0 || index >= arr.Length)
            throw new IndexOutOfRangeException($"索引 {index} 超出数组范围 [0, {arr.Length})");

        if (!_arrayElemType!.IsAssignableFrom(newValue.Type))
            throw new InvalidOperationException($"类型约束冲突：无法将 {newValue.Type} 赋值给 {_arrayElemType} 类型的数组元素");

        arr.SetItem(index, newValue);
    }

    /// <summary>
    /// 向数组末尾追加一个符合类型的元素
    /// </summary>
    public Value Append(Value item)
    {
        if (_tag != TAG_ARRAY)
            throw new InvalidOperationException("只有数组可以执行 Append 操作");

        var targetType = _arrayElemType!;
        if (!targetType.IsAssignableFrom(item.Type))
            throw new InvalidOperationException($"类型约束冲突：无法向 {Type} 追加 {item.Type} 类型的元素");

        return new Value(TAG_ARRAY, 0, AsArray().Append(item), _arrayElemType);
    }

    /// <summary>转换为布尔值，用于逻辑判断（int: 非0为真；bool: 自身；string: 非空为真；array: 非空为真）</summary>
    public bool ToBoolean() => _tag switch
    {
        TAG_INT32 => _int32Val != 0,
        TAG_BOOL => _int32Val != 0,
        TAG_BYTE => _int32Val != 0,
        TAG_UINT32 => _int32Val != 0,
        TAG_UINT64 => _longVal != 0,
        TAG_STRING => !string.IsNullOrEmpty((string)_refVal!),
        TAG_ARRAY => ((ScriptArray)_refVal!).Length > 0,
        TAG_FLOAT64 => f64Val != 0.0,
        TAG_PTR => _longVal != 0,
        TAG_STRUCT => _refVal != null,
        _ => false
    };

    #region 类型断言

    /// <summary>
    /// 绑定期已通过 ApplyImplicitConversion 插入转换节点，运行时操作数类型必须一致。
    /// </summary>
    private static void AssertSameTag(ref Value left, ref Value right)
    {
        if (left._tag != right._tag)
            throw new InvalidOperationException($"运行时类型不匹配: {left.Type} vs {right.Type}（绑定期遗漏转换）");
    }

    #endregion

    public static Value operator +(Value left, Value right)
    {
        if (left._tag == TAG_VOID || right._tag == TAG_VOID)
            throw new InvalidOperationException("空类型不支持运算");

        if (left._tag == TAG_STRING || right._tag == TAG_STRING)
            return FromString(left.ToString() + right.ToString());

        if (left._tag == TAG_ARRAY && right._tag == TAG_ARRAY)
            return left.Concat(right);

        AssertSameTag(ref left, ref right);

        return left._tag switch
        {
            TAG_INT32 => FromInt(left._int32Val + right._int32Val),
            TAG_BYTE => FromByte((byte)(left._int32Val + right._int32Val)),
            TAG_UINT32 => FromUInt(unchecked((uint)left._int32Val + (uint)right._int32Val)),
            TAG_UINT64 => FromUInt64((ulong)left._longVal + (ulong)right._longVal),
            TAG_FLOAT64 => FromDouble(left.f64Val + right.f64Val),
            _ => throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '+'")
        };
    }

    public static Value operator -(Value left, Value right)
    {
        AssertSameTag(ref left, ref right);

        return left._tag switch
        {
            TAG_INT32 => FromInt(left._int32Val - right._int32Val),
            TAG_BYTE => FromByte((byte)(left._int32Val - right._int32Val)),
            TAG_UINT32 => FromUInt(unchecked((uint)left._int32Val - (uint)right._int32Val)),
            TAG_UINT64 => FromUInt64((ulong)left._longVal - (ulong)right._longVal),
            TAG_FLOAT64 => FromDouble(left.f64Val - right.f64Val),
            _ => throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '-'")
        };
    }

    public static Value operator *(Value left, Value right)
    {
        AssertSameTag(ref left, ref right);

        return left._tag switch
        {
            TAG_INT32 => FromInt(left._int32Val * right._int32Val),
            TAG_BYTE => FromByte((byte)(left._int32Val * right._int32Val)),
            TAG_UINT32 => FromUInt(unchecked((uint)left._int32Val * (uint)right._int32Val)),
            TAG_UINT64 => FromUInt64((ulong)left._longVal * (ulong)right._longVal),
            TAG_FLOAT64 => FromDouble(left.f64Val * right.f64Val),
            _ => throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '*'")
        };
    }

    public static Value operator /(Value left, Value right)
    {
        AssertSameTag(ref left, ref right);

        return left._tag switch
        {
            TAG_INT32 => right._int32Val == 0
                ? throw new DivideByZeroException("整数除零")
                : FromInt(left._int32Val / right._int32Val),
            TAG_BYTE => right._int32Val == 0
                ? throw new DivideByZeroException("整数除零")
                : FromByte((byte)(left._int32Val / right._int32Val)),
            TAG_UINT32 => right._int32Val == 0
                ? throw new DivideByZeroException("整数除零")
                : FromUInt(unchecked((uint)left._int32Val / (uint)right._int32Val)),
            TAG_UINT64 => right._longVal == 0
                ? throw new DivideByZeroException("整数除零")
                : FromUInt64((ulong)left._longVal / (ulong)right._longVal),
            TAG_FLOAT64 => FromDouble(left.f64Val / right.f64Val),
            _ => throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '/'")
        };
    }

    public static Value operator %(Value left, Value right)
    {
        AssertSameTag(ref left, ref right);

        return left._tag switch
        {
            TAG_INT32 => right._int32Val == 0
                ? throw new DivideByZeroException("整数除零")
                : FromInt(left._int32Val % right._int32Val),
            TAG_BYTE => right._int32Val == 0
                ? throw new DivideByZeroException("整数除零")
                : FromByte((byte)(left._int32Val % right._int32Val)),
            TAG_UINT32 => right._int32Val == 0
                ? throw new DivideByZeroException("整数除零")
                : FromUInt(unchecked((uint)left._int32Val % (uint)right._int32Val)),
            TAG_UINT64 => right._longVal == 0
                ? throw new DivideByZeroException("整数除零")
                : FromUInt64((ulong)left._longVal % (ulong)right._longVal),
            _ => throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '%'")
        };
    }

    public static Value operator &(Value left, Value right)
    {
        if (left._tag == TAG_STRING || right._tag == TAG_STRING)
            return FromString(left.ToString() + right.ToString());

        AssertSameTag(ref left, ref right);

        return left._tag switch
        {
            TAG_INT32 => FromInt(left._int32Val & right._int32Val),
            TAG_BYTE => FromByte((byte)(left._int32Val & right._int32Val)),
            TAG_UINT32 => FromUInt(unchecked((uint)left._int32Val & (uint)right._int32Val)),
            TAG_UINT64 => FromUInt64((ulong)left._longVal & (ulong)right._longVal),
            _ => throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '&'")
        };
    }

    public static Value operator |(Value left, Value right)
    {
        AssertSameTag(ref left, ref right);

        return left._tag switch
        {
            TAG_INT32 => FromInt(left._int32Val | right._int32Val),
            TAG_BYTE => FromByte((byte)(left._int32Val | right._int32Val)),
            TAG_UINT32 => FromUInt(unchecked((uint)left._int32Val | (uint)right._int32Val)),
            TAG_UINT64 => FromUInt64((ulong)left._longVal | (ulong)right._longVal),
            _ => throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '|'")
        };
    }

    public static Value operator ^(Value left, Value right)
    {
        AssertSameTag(ref left, ref right);

        return left._tag switch
        {
            TAG_INT32 => FromInt(left._int32Val ^ right._int32Val),
            TAG_BYTE => FromByte((byte)(left._int32Val ^ right._int32Val)),
            TAG_UINT32 => FromUInt(unchecked((uint)left._int32Val ^ (uint)right._int32Val)),
            TAG_UINT64 => FromUInt64((ulong)left._longVal ^ (ulong)right._longVal),
            _ => throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '^'")
        };
    }

    public static Value operator <<(Value left, Value right)
    {
        var shift = right._tag switch
        {
            TAG_INT32 => right._int32Val,
            TAG_BYTE => right._int32Val,
            TAG_UINT32 => (int)unchecked((uint)right._int32Val),
            TAG_UINT64 => (int)right._longVal,
            _ => throw new InvalidOperationException($"位移量类型不支持: {right.Type}")
        };

        return left._tag switch
        {
            TAG_INT32 => FromInt(left._int32Val << shift),
            TAG_BYTE => FromByte((byte)(left._int32Val << shift)),
            TAG_UINT32 => FromUInt(unchecked((uint)left._int32Val << shift)),
            TAG_UINT64 => FromUInt64((ulong)left._longVal << shift),
            _ => throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '<<'")
        };
    }

    public static Value operator >>(Value left, Value right)
    {
        var shift = right._tag switch
        {
            TAG_INT32 => right._int32Val,
            TAG_BYTE => right._int32Val,
            TAG_UINT32 => (int)unchecked((uint)right._int32Val),
            TAG_UINT64 => (int)right._longVal,
            _ => throw new InvalidOperationException($"位移量类型不支持: {right.Type}")
        };

        return left._tag switch
        {
            TAG_INT32 => FromInt(left._int32Val >> shift),
            TAG_BYTE => FromByte((byte)(left._int32Val >> shift)),
            TAG_UINT32 => FromUInt(unchecked((uint)left._int32Val >> shift)),
            TAG_UINT64 => FromUInt64((ulong)left._longVal >> shift),
            _ => throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '>>'")
        };
    }

    public Value RoundDiv(Value right)
    {
        if (_tag != right._tag)
            throw new InvalidOperationException($"在 {Type} 和 {right.Type} 之间不支持四舍五入除法");

        return _tag switch
        {
            TAG_INT32 => right._int32Val == 0
                ? throw new DivideByZeroException("整数除零")
                : FromInt((int)Math.Round((double)_int32Val / right._int32Val, MidpointRounding.AwayFromZero)),
            TAG_BYTE => right._int32Val == 0
                ? throw new DivideByZeroException("整数除零")
                : FromByte((byte)Math.Round((double)_int32Val / right._int32Val, MidpointRounding.AwayFromZero)),
            TAG_UINT32 => right._int32Val == 0
                ? throw new DivideByZeroException("整数除零")
                : FromUInt(unchecked((uint)Math.Round((double)(uint)_int32Val / (uint)right._int32Val, MidpointRounding.AwayFromZero))),
            TAG_UINT64 => right._longVal == 0
                ? throw new DivideByZeroException("整数除零")
                : FromUInt64((ulong)Math.Round((double)(ulong)_longVal / (ulong)right._longVal, MidpointRounding.AwayFromZero)),
            _ => throw new InvalidOperationException($"在 {Type} 和 {right.Type} 之间不支持四舍五入除法")
        };
    }
}

#region 类型
public abstract class ScriptType : IEquatable<ScriptType>
{
    public abstract string Name { get; }

    public override string ToString() => Name;
    public abstract bool IsAssignableFrom(ScriptType other);

    public abstract override int GetHashCode();

    public abstract bool Equals(ScriptType? other);
    public override bool Equals(object? obj) => obj is ScriptType other && Equals(other);

    public static bool operator ==(ScriptType? left, ScriptType? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }
    public static bool operator !=(ScriptType? left, ScriptType? right) => !(left == right);

    // 基础标量类型
    public static readonly VoidType Void = new();
    public static readonly ScalarType Bool = new("bool");
    public static readonly ScalarType Byte = new("byte");
    public static readonly ScalarType Int = new("int");
    public static readonly ScalarType UInt = new("uint");
    public static readonly ScalarType UInt64 = new("uint64");
    public static readonly ScalarType Double = new("double");
    public static readonly ScalarType String = new("string");
    public static readonly ScalarType Ptr = new("ptr");
    public static readonly AnyType Any = new();

    private static readonly Dictionary<ScriptType, ArrayType> _arrayOfCache = [];
    public static ArrayType ArrayOf(ScriptType elementType)
    {
        if (_arrayOfCache.TryGetValue(elementType, out var cached)) return cached;
        var created = new ArrayType(elementType);
        _arrayOfCache[elementType] = created;
        return created;
    }
}

public sealed class ScalarType(string name) : ScriptType
{
    public override string Name => name;
    public override bool IsAssignableFrom(ScriptType other) => other is ScalarType s && s.Name == this.Name;
    public override bool Equals(ScriptType? other) => other is ScalarType s && s.Name == this.Name;
    public override int GetHashCode() => Name.GetHashCode();
}

public sealed class VoidType : ScriptType
{
    public override string Name => "void";
    public override bool IsAssignableFrom(ScriptType other) => other is VoidType;
    public override bool Equals(ScriptType? other) => other is VoidType;
    public override int GetHashCode() => 0;
}

/// <summary>
/// 多态内置函数的参数占位符——匹配任何类型。不参与用户代码。
/// </summary>
public sealed class AnyType : ScriptType
{
    public override string Name => "any";
    public override bool IsAssignableFrom(ScriptType other) => true;
    public override bool Equals(ScriptType? other) => other is AnyType;
    public override int GetHashCode() => 1;
}

public sealed class ArrayType : ScriptType
{
    public ScriptType ElementType { get; }
    /// <summary>数组长度，0表示动态长度</summary>
    public int Count { get; }
    public override string Name => Count > 0 ? $"{ElementType.Name}[{Count}]" : $"{ElementType.Name}[]";

    public ArrayType(ScriptType elementType, int count = 0)
    {
        ElementType = elementType;
        Count = count;
    }

    public override bool IsAssignableFrom(ScriptType other) =>
        other is ArrayType a && ElementType.Equals(a.ElementType);

    public override bool Equals(ScriptType? other) =>
        other is ArrayType a && ElementType.Equals(a.ElementType);

    public override int GetHashCode() => HashCode.Combine("Array", ElementType);
}

public sealed class StructType : ScriptType
{
    public EcsStructDef Definition { get; }
    public override string Name => Definition.Name;

    public StructType(EcsStructDef def) { Definition = def; }

    public override bool IsAssignableFrom(ScriptType other) =>
        other is StructType s && s.Definition == Definition;

    public override bool Equals(ScriptType? other) =>
        other is StructType s && s.Definition == Definition;

    public override int GetHashCode() => Definition.Name.GetHashCode();
}
#endregion