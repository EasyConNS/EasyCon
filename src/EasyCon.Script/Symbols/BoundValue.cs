using EasyCon.Script.Runtime;
using System.Collections.Immutable;
using System.Globalization;
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
    private const byte TAG_INT = 1;
    private const byte TAG_BOOL = 2;
    private const byte TAG_STRING = 3;
    private const byte TAG_DOUBLE = 4;
    private const byte TAG_PTR = 5;
    private const byte TAG_ARRAY = 6;
    private const byte TAG_STRUCT = 7;

    [FieldOffset(0)] private readonly byte _tag;
    [FieldOffset(4)] private int _intVal;                // ┐ 也用于 bool (0/1)
    [FieldOffset(4)] private double _doubleVal;          // ├ 三者互斥，共享偏移
    [FieldOffset(4)] private long _longVal;              // ┘
    [FieldOffset(16)] private readonly object? _refVal;   // 8B 对齐（offset 12 有 4B padding）
    [FieldOffset(24)] private readonly ScriptType? _arrayElemType;

    // 内部访问器：供 Evaluator 热路径使用，避免经过 Type 属性
    internal byte Tag => _tag;

    // 预定义的泛型原型
    private static readonly GenericDefinition ArrayDef = new("Array", 1);

    // 仅设置不重叠的字段；_doubleVal / _longVal 由调用方在外层赋值
    private Value(byte tag, int intVal, object? refVal, ScriptType? arrayElemType)
    {
        _tag = tag;
        _intVal = intVal;
        _refVal = refVal;
        _arrayElemType = arrayElemType;
    }

    /// <summary>当前值的类型（从 tag 计算，无额外存储）</summary>
    public ScriptType Type => _tag switch
    {
        TAG_VOID => ScriptType.Void,
        TAG_INT => ScriptType.Int,
        TAG_BOOL => ScriptType.Bool,
        TAG_STRING => ScriptType.String,
        TAG_DOUBLE => ScriptType.Double,
        TAG_PTR => ScriptType.Ptr,
        TAG_ARRAY => ArrayDef.Bind(_arrayElemType!),
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
            string s => FromString(s),
            double d => FromDouble(d),
            long l => FromPtr(l),
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

    public static Value FromInt(int v) => new(TAG_INT, v, null, null);
    public static Value FromBool(bool v) => new(TAG_BOOL, v ? 1 : 0, null, null);
    public static Value FromString(string v) => new(TAG_STRING, 0, v, null);

    public static Value FromDouble(double v)
    {
        var val = new Value(TAG_DOUBLE, 0, null, null);
        val._doubleVal = v;
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

    /// <summary>
    /// 创建强类型数组
    /// </summary>
    public static Value CreateArray(ScriptType elementType, IEnumerable<Value> elements)
    {
        var list = elements.ToList();
        foreach (var e in list)
        {
            if (!elementType.IsAssignableFrom(e.Type))
                throw new InvalidOperationException($"元素类型 {e.Type} 与数组声明类型 {elementType} 不匹配");
        }
        return new Value(TAG_ARRAY, 0, list.ToImmutableList(), elementType);
    }

    public int AsInt() => _tag == TAG_INT ? _intVal : throw new InvalidCastException();
    public int ToInt() => _tag == TAG_DOUBLE ? AsBool() ? 1 : 0 : throw new InvalidCastException($"类型 {Type.Name} 无法转换为 <int>");

    public bool AsBool() => _tag == TAG_BOOL ? _intVal != 0 : throw new InvalidCastException();
    public string AsString() => _tag == TAG_STRING ? (string)_refVal! : throw new InvalidCastException();
    public ImmutableList<Value> AsArray() => _tag == TAG_ARRAY ? (ImmutableList<Value>)_refVal! : throw new InvalidCastException();
    public double AsDouble() => _tag == TAG_DOUBLE ? _doubleVal : throw new InvalidCastException();
    public double ToDouble() => _tag == TAG_INT ? (double)_intVal : throw new InvalidCastException($"类型 {Type.Name} 无法转换为 <double>");
    public long AsPtr() => _tag == TAG_PTR ? _longVal : throw new InvalidCastException();

    #endregion

    #region 运算与索引

    public int Length => _tag switch
    {
        TAG_STRING => ((string)_refVal!).Length,
        TAG_ARRAY => ((ImmutableList<Value>)_refVal!).Count,
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
                return ((ImmutableList<Value>)_refVal!)[index];
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
                var list = (ImmutableList<Value>)_refVal!;
                var (offset, length) = range.GetOffsetAndLength(list.Count);
                return new Value(TAG_ARRAY, 0, list.GetRange(offset, length), _arrayElemType);
            }
            throw new InvalidOperationException($"{Type} 不支持切片");
        }
    }

    #endregion

    #region 比较逻辑

    public bool Equals(Value other)
    {
        if (_tag != other._tag)
        {
            if (_tag == TAG_INT && other._tag == TAG_PTR)
                return (long)_intVal == other._longVal;
            if (_tag == TAG_PTR && other._tag == TAG_INT)
                return _longVal == (long)other._intVal;
            return false;
        }
        return _tag switch
        {
            TAG_VOID => true,
            TAG_INT => _intVal == other._intVal,
            TAG_BOOL => _intVal == other._intVal,
            TAG_STRING => string.Equals((string)_refVal!, (string)other._refVal!, StringComparison.Ordinal),
            TAG_DOUBLE => _doubleVal == other._doubleVal,
            TAG_PTR => _longVal == other._longVal,
            TAG_ARRAY => ((ImmutableList<Value>)_refVal!).SequenceEqual((ImmutableList<Value>)other._refVal!),
            TAG_STRUCT => ReferenceEquals(_refVal, other._refVal),
            _ => false
        };
    }

    public int CompareTo(Value other)
    {
        if (_tag != other._tag)
        {
            if (_tag == TAG_INT && other._tag == TAG_PTR)
                return ((long)_intVal).CompareTo(other._longVal);
            if (_tag == TAG_PTR && other._tag == TAG_INT)
                return _longVal.CompareTo((long)other._intVal);
            throw new InvalidOperationException("不同类型无法比较");
        }
        return _tag switch
        {
            TAG_INT => _intVal.CompareTo(other._intVal),
            TAG_STRING => string.Compare((string)_refVal!, (string)other._refVal!, StringComparison.Ordinal),
            TAG_DOUBLE => _doubleVal.CompareTo(other._doubleVal),
            TAG_PTR => _longVal.CompareTo(other._longVal),
            _ => throw new InvalidOperationException($"类型不支持比较 <{Type}>与<{other.Type}>")
        };
    }

    public override bool Equals(object? obj) => obj is Value v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(_tag, _intVal, _doubleVal, _longVal);

    public static bool operator ==(Value left, Value right) => left.Equals(right);
    public static bool operator !=(Value left, Value right) => !left.Equals(right);
    public static bool operator <(Value left, Value right) => left.CompareTo(right) < 0;
    public static bool operator >(Value left, Value right) => left.CompareTo(right) > 0;
    public static bool operator <=(Value left, Value right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Value left, Value right) => left.CompareTo(right) >= 0;

    #endregion

    public override string ToString() => _tag switch
    {
        TAG_ARRAY => $"[{string.Join(", ", (ImmutableList<Value>)_refVal!)}]",
        TAG_DOUBLE => _doubleVal.ToString(),
        TAG_BOOL => _intVal != 0 ? "true" : "false",
        TAG_PTR => $"0x{_longVal:X}",
        TAG_STRING => (string)_refVal!,
        TAG_INT => _intVal.ToString(),
        TAG_STRUCT => $"struct:{((EcsStruct)_refVal!).Definition.Name}",
        _ => "void"
    };

    // 辅助属性：判断是否为 Array<T>
    private bool IsArray => _tag == TAG_ARRAY;
    private ScriptType? ElementType => _tag == TAG_ARRAY ? _arrayElemType : null;

    /// <summary>
    /// 连接两个相同类型的数组
    /// </summary>
    public Value Concat(Value other)
    {
        if (_tag != TAG_ARRAY || other._tag != TAG_ARRAY)
            throw new InvalidOperationException("只有数组可以执行 Concat 操作");

        if (!Type.Equals(other.Type))
            throw new InvalidOperationException($"无法连接不同类型的数组：{Type} 和 {other.Type}");

        var newList = AsArray().AddRange(other.AsArray());
        return new Value(TAG_ARRAY, 0, newList, _arrayElemType);
    }

    /// <summary>
    /// 返回一个新数组，其中指定索引处的元素被替换为新值（copy-on-write）
    /// </summary>
    public Value SetIndex(int index, Value newValue)
    {
        if (_tag != TAG_ARRAY)
            throw new InvalidOperationException("只有数组支持元素赋值");

        var list = (ImmutableList<Value>)_refVal!;
        if (index < 0 || index >= list.Count)
            throw new IndexOutOfRangeException($"索引 {index} 超出数组范围 [0, {list.Count})");

        if (!_arrayElemType!.IsAssignableFrom(newValue.Type))
            throw new InvalidOperationException($"类型约束冲突：无法将 {newValue.Type} 赋值给 {_arrayElemType} 类型的数组元素");

        return new Value(TAG_ARRAY, 0, list.SetItem(index, newValue), _arrayElemType);
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

        var newList = AsArray().Add(item);
        return new Value(TAG_ARRAY, 0, newList, _arrayElemType);
    }

    /// <summary>转换为布尔值，用于逻辑判断（int: 非0为真；bool: 自身；string: 非空为真；array: 非空为真）</summary>
    public bool ToBoolean() => _tag switch
    {
        TAG_INT => _intVal != 0,
        TAG_BOOL => _intVal != 0,
        TAG_STRING => !string.IsNullOrEmpty((string)_refVal!),
        TAG_ARRAY => ((ImmutableList<Value>)_refVal!).Count > 0,
        TAG_DOUBLE => _doubleVal != 0.0,
        TAG_PTR => _longVal != 0,
        TAG_STRUCT => _refVal != null,
        _ => false
    };

    // 重载算数运算符
    public static Value operator +(Value left, Value right)
    {
        // 1. 排除 void 类型参与运算
        if (left._tag == TAG_VOID || right._tag == TAG_VOID)
            throw new InvalidOperationException("空类型不支持运算");

        // 2. 相同类型处理
        if (left._tag == right._tag)
        {
            if (left._tag == TAG_INT)
                return FromInt(left._intVal + right._intVal);

            if (left._tag == TAG_DOUBLE)
                return FromDouble(left._doubleVal + right._doubleVal);

            if (left._tag == TAG_STRING)
                return FromString((string)left._refVal! + (string)right._refVal!);

            // 处理泛型数组拼接 (Array<T> + Array<T>)
            if (left._tag == TAG_ARRAY)
                return left.Concat(right);
        }

        // 3. 混合类型处理：double 与 int 互相提升
        if (left._tag == TAG_DOUBLE && right._tag == TAG_INT)
            return FromDouble(left._doubleVal + right._intVal);
        if (left._tag == TAG_INT && right._tag == TAG_DOUBLE)
            return FromDouble(left._intVal + right._doubleVal);

        // 4. 混合类型处理：字符串拼接规则
        // 只要有一侧是 string，则将另一侧转为字符串进行拼接
        if (left._tag == TAG_STRING || right._tag == TAG_STRING)
        {
            return FromString(left.ToString() + right.ToString());
        }

        // 5. 其他不支持的组合
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '+'");
    }

    public static Value operator -(Value left, Value right)
    {
        if (left._tag == TAG_VOID || right._tag == TAG_VOID)
            throw new InvalidOperationException("空类型不支持运算");
        if (left._tag == TAG_INT && right._tag == TAG_INT)
            return FromInt(left._intVal - right._intVal);
        if (left._tag == TAG_DOUBLE && right._tag == TAG_DOUBLE)
            return FromDouble(left._doubleVal - right._doubleVal);
        if (left._tag == TAG_DOUBLE && right._tag == TAG_INT)
            return FromDouble(left._doubleVal - right._intVal);
        if (left._tag == TAG_INT && right._tag == TAG_DOUBLE)
            return FromDouble(left._intVal - right._doubleVal);
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '-'");
    }

    public static Value operator *(Value left, Value right)
    {
        if (left._tag == TAG_VOID || right._tag == TAG_VOID)
            throw new InvalidOperationException("空类型不支持运算");
        if (left._tag == TAG_INT && right._tag == TAG_INT)
            return FromInt(left._intVal * right._intVal);
        if (left._tag == TAG_DOUBLE && right._tag == TAG_DOUBLE)
            return FromDouble(left._doubleVal * right._doubleVal);
        if (left._tag == TAG_DOUBLE && right._tag == TAG_INT)
            return FromDouble(left._doubleVal * right._intVal);
        if (left._tag == TAG_INT && right._tag == TAG_DOUBLE)
            return FromDouble(left._intVal * right._doubleVal);
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '*'");
    }

    public static Value operator /(Value left, Value right)
    {
        if (left._tag == TAG_VOID || right._tag == TAG_VOID)
            throw new InvalidOperationException("空类型不支持运算");
        if (left._tag == TAG_INT && right._tag == TAG_INT)
        {
            if (right._intVal == 0) throw new DivideByZeroException("整数除零");
            return FromInt(left._intVal / right._intVal);
        }
        if (left._tag == TAG_DOUBLE && right._tag == TAG_DOUBLE)
            return FromDouble(left._doubleVal / right._doubleVal);
        if (left._tag == TAG_DOUBLE && right._tag == TAG_INT)
            return FromDouble(left._doubleVal / right._intVal);
        if (left._tag == TAG_INT && right._tag == TAG_DOUBLE)
            return FromDouble(left._intVal / right._doubleVal);
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '/'");
    }

    public static Value operator %(Value left, Value right)
    {
        if (left._tag == TAG_INT && right._tag == TAG_INT)
        {
            if (right._intVal == 0) throw new DivideByZeroException("整数除零");
            return FromInt(left._intVal % right._intVal);
        }
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '%'");
    }

    public static Value operator &(Value left, Value right)
    {
        // 1. 排除 void 类型参与运算
        if (left._tag == TAG_VOID || right._tag == TAG_VOID)
            throw new InvalidOperationException("空类型不支持运算");
        // 2. 整数按位与
        if (left._tag == TAG_INT && right._tag == TAG_INT)
            return FromInt(left._intVal & right._intVal);
        // 3. 字符串拼接规则
        if (left._tag == TAG_STRING || right._tag == TAG_STRING)
            return FromString(left.ToString() + right.ToString());
        // 4. 其他不支持的组合
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '&'");
    }

    public static Value operator |(Value left, Value right)
    {
        if (left._tag == TAG_INT && right._tag == TAG_INT)
            return FromInt(left._intVal | right._intVal);
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '|'");
    }

    public static Value operator ^(Value left, Value right)
    {
        if (left._tag == TAG_INT && right._tag == TAG_INT)
            return FromInt(left._intVal ^ right._intVal);
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '^'");
    }

    public static Value operator <<(Value left, Value right)
    {
        if (left._tag == TAG_INT && right._tag == TAG_INT)
            return FromInt(left._intVal << right._intVal);
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '<<'");
    }

    public static Value operator >>(Value left, Value right)
    {
        if (left._tag == TAG_INT && right._tag == TAG_INT)
            return FromInt(left._intVal >> right._intVal);
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '>>'");
    }

    public Value RoundDiv(Value right)
    {
        if (_tag == TAG_INT && right._tag == TAG_INT)
        {
            if (right._intVal == 0) throw new DivideByZeroException("整数除零");
            return FromInt((int)Math.Round((double)_intVal / right._intVal, MidpointRounding.AwayFromZero));
        }
        throw new InvalidOperationException($"在 {Type} 和 {right.Type} 之间不支持四舍五入除法");
    }
}

#region 类型
public abstract class ScriptType : IEquatable<ScriptType>
{
    public abstract string Name { get; }

    public override string ToString() => Name;
    public abstract bool IsAssignableFrom(ScriptType other);

    /// <summary>
    /// 判断两个类型是否在签名层面存在重叠（用于重载冲突检测）。
    /// TypeParameter 视为可匹配任何类型。
    /// </summary>
    public virtual bool TypeOverlaps(ScriptType other) => false;

    public abstract override int GetHashCode();

    public abstract bool Equals(ScriptType? other);
    public override bool Equals(object? obj) => obj is ScriptType other && Equals(other);

    // 重载比较运算符
    public static bool operator ==(ScriptType left, ScriptType right) => left.IsAssignableFrom(right);
    public static bool operator !=(ScriptType left, ScriptType right) => !left.IsAssignableFrom(right);

    // 基础标量类型
    public static readonly ScalarType Int = new("int");
    public static readonly ScalarType Bool = new("bool");
    public static readonly ScalarType String = new("string");
    public static readonly VoidType Void = new();
    public static readonly ScalarType Ptr = new("ptr");
    public static readonly ScalarType Double = new("double");

    // 预定义泛型定义
    public static readonly GenericDefinition Array = new("Array", 1);
}

/// <summary>
/// 标量类型 (int, bool, string)
/// </summary>
public sealed class ScalarType(string name) : ScriptType
{
    public override string Name => name;
    public override bool IsAssignableFrom(ScriptType other) => other is ScalarType s && s.Name == this.Name;
    public override bool TypeOverlaps(ScriptType other) => other switch
    {
        TypeParameter => true,
        ScalarType s => s.Name == Name,
        _ => false
    };
    public override bool Equals(ScriptType? other) => other is ScalarType s && s.Name == this.Name;
    public override int GetHashCode() => Name.GetHashCode();
}

public sealed class VoidType : ScriptType
{
    public override string Name => "void";
    public override bool IsAssignableFrom(ScriptType other) => other is VoidType;
    public override bool TypeOverlaps(ScriptType other) => other is VoidType;
    public override bool Equals(ScriptType? other) => other is VoidType;
    public override int GetHashCode() => 0;
}
/// <summary>
/// 泛型占位符 (如 T)
/// </summary>
public sealed class TypeParameter(string name) : ScriptType
{
    public override string Name => name;
    public override bool IsAssignableFrom(ScriptType other) => Equals(other);
    public override bool TypeOverlaps(ScriptType other) => true;
    public override bool Equals(ScriptType? other) => other is TypeParameter tp && tp.Name == Name;
    public override int GetHashCode() => Name.GetHashCode();
}
/// <summary>
/// 泛型定义 (例如 List<T>)
/// </summary>
public sealed class GenericDefinition(string name, int typeParameterCount)
{
    public string Name { get; } = name;
    public int TypeParameterCount { get; } = typeParameterCount;

    /// <summary>
    /// 使用具体参数实例化泛型
    /// </summary>
    public GenericType Bind(params ScriptType[] typeArguments)
    {
        if (typeArguments.Length != TypeParameterCount)
            throw new ArgumentException($"泛型 {Name} 参数不匹配。需要 {TypeParameterCount} 但提供了 {typeArguments.Length}");
        return new GenericType(this, typeArguments);
    }
}

/// <summary>
/// 具体化的泛型类型 (例如 List<int>)
/// </summary>
public sealed class GenericType(GenericDefinition definition, IEnumerable<ScriptType> typeArguments) : ScriptType
{
    public GenericDefinition Definition { get; } = definition;
    public ImmutableArray<ScriptType> TypeArguments { get; } = [.. typeArguments];

    public override string Name => $"{Definition.Name}<{string.Join(", ", TypeArguments.Select(t => t.Name))}>";

    public override bool IsAssignableFrom(ScriptType other)
    {
        if (other is not GenericType g) return false;
        return Definition.Name == g.Definition.Name && TypeArguments.SequenceEqual(g.TypeArguments);
    }

    public override bool TypeOverlaps(ScriptType other) => other switch
    {
        TypeParameter => true,
        GenericType g when g.Definition.Name != Definition.Name => false,
        GenericType g => TypeArguments.Length == g.TypeArguments.Length
            && TypeArguments.Zip(g.TypeArguments).All(pair => pair.First.TypeOverlaps(pair.Second)),
        _ => false
    };

    public override bool Equals(ScriptType? other) =>
        other is GenericType g && Definition.Name == g.Definition.Name && TypeArguments.SequenceEqual(g.TypeArguments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Definition.Name);
        foreach (var arg in TypeArguments) hash.Add(arg);
        return hash.ToHashCode();
    }
}

public sealed class StructType : ScriptType
{
    public EcsStructDef Definition { get; }
    public override string Name => Definition.Name;

    public StructType(EcsStructDef def) { Definition = def; }

    public override bool IsAssignableFrom(ScriptType other) =>
        other is StructType s && s.Definition == Definition;

    public override bool TypeOverlaps(ScriptType other) =>
        other is TypeParameter || (other is StructType s && s.Definition == Definition);

    public override bool Equals(ScriptType? other) =>
        other is StructType s && s.Definition == Definition;

    public override int GetHashCode() => Definition.Name.GetHashCode();
}
#endregion