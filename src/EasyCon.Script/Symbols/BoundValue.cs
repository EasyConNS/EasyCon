using System.Collections.Immutable;
using System.Globalization;

namespace EasyCon.Script.Symbols;

/// <summary>
/// 表示一个动态值，可以是 int、bool、string 或一维数组（元素为 Value）。
/// 支持相等性比较、有序比较（同类型）和下标索引（字符串按 Unicode 字符，数组按元素）。
/// </summary>
public readonly struct Value : IEquatable<Value>, IComparable<Value>
{
    /// <summary>当前值的类型</summary>
    public ScriptType Type { get; }

    // 实际数据存储：int/bool 直接装箱，string 为字符串，array 为 Value[]
    private readonly object? _value;
    // 预定义的泛型原型
    private static readonly GenericDefinition ArrayDef = new("Array", 1);

    private Value(object? value, ScriptType type)
    {
        _value = value;
        Type = type;
    }

    #region 工厂方法与转换
    public static Value From(object? o)
    {
        if (o == null) return Void;
        if (o is Value v) return v;

        return o switch
        {
            int i => new Value(i, ScriptType.Int),
            bool b => new Value(b, ScriptType.Bool),
            string s => new Value(s, ScriptType.String),
            double d => new Value(d, ScriptType.Double),
            long l => new Value(l, ScriptType.Ptr),
            IEnumerable<Value> list => CreateArray(
                list.FirstOrDefault().Type ?? ScriptType.Int,
                list
            ),
            // 处理数组：尝试获取第一个元素的类型作为数组的泛型参数
            //Value[] array => new Value(
            //    array,
            //    array.Length > 0
            //        ? ScriptType.ArrayDefinition.Bind(array[0].Type)
            //        : ScriptType.ArrayDefinition.Bind(ScriptType.Int) // 空数组默认 int[]
            //),
            _ => throw new ArgumentException($"无法将类型 {o.GetType()} 转换为脚本 Value")
        };
    }
    public static implicit operator Value(int v) => FromInt(v);
    public static implicit operator Value(bool v) => FromBool(v);
    public static implicit operator Value(string v) => FromString(v);
    public static implicit operator Value(double v) => new(v, ScriptType.Double);
    public static implicit operator Value(long v) => new(v, ScriptType.Ptr);
    public static Value FromInt(int v) => new(v, ScriptType.Int);
    public static Value FromBool(bool v) => new(v, ScriptType.Bool);
    public static Value FromString(string v) => new(v, ScriptType.String);

    public static Value Void => new(null, ScriptType.Void);
    public static Value FromDouble(double v) => new(v, ScriptType.Double);
    public static Value FromPtr(long v) => new(v, ScriptType.Ptr);

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
        return new Value(list.ToImmutableList(), ArrayDef.Bind(elementType));
    }

    public int AsInt() => Type.Equals(ScriptType.Int) ? (int)_value! : throw new InvalidCastException();
    public bool AsBool() => Type.Equals(ScriptType.Bool) ? (bool)_value! : throw new InvalidCastException();
    public string AsString() => Type.Equals(ScriptType.String) ? (string)_value! : throw new InvalidCastException();
    public ImmutableList<Value> AsArray() => Type is GenericType { Definition.Name: "Array" } ? (ImmutableList<Value>)_value! : throw new InvalidCastException();
    public double AsDouble() => Type.Equals(ScriptType.Double) ? (double)_value! : throw new InvalidCastException();
    public long AsPtr() => Type.Equals(ScriptType.Ptr) ? (long)_value! : throw new InvalidCastException();

    #endregion

    #region 运算与索引

    public int Length => Type switch
    {
        ScalarType s when s.Name == "string" => new StringInfo(AsString()).LengthInTextElements,
        GenericType { Definition.Name: "Array" } => AsArray().Count,
        _ => 0
    };

    public Value this[int index]
    {
        get
        {
            if (Type.Equals(ScriptType.String))
            {
                var si = new StringInfo(AsString());
                return FromString(si.SubstringByTextElements(index, 1));
            }
            if (Type is GenericType { Definition.Name: "Array" })
            {
                return AsArray()[index];
            }
            throw new InvalidOperationException($"{Type} 不支持索引");
        }
    }

    public Value this[Range range]
    {
        get
        {
            if (Type.Equals(ScriptType.String))
            {
                // 注意：这里简单使用 C# string 切片，若需处理 Unicode 代理对，建议配合 StringInfo
                return FromString(AsString()[range]);
            }
            if (IsArray)
            {
                var list = AsArray();
                var (offset, length) = range.GetOffsetAndLength(list.Count);
                return new Value(list.GetRange(offset, length), Type);
            }
            throw new InvalidOperationException($"{Type} 不支持切片");
        }
    }
    #endregion

    #region 比较逻辑

    public bool Equals(Value other)
    {
        if (!Type.Equals(other.Type)) return false;
        return Type switch
        {
            ScalarType => Equals(_value, other._value),
            GenericType => AsArray().SequenceEqual(other.AsArray()),
            VoidType => true,
            _ => false
        };
    }

    public int CompareTo(Value other)
    {
        if (!Type.Equals(other.Type)) throw new InvalidOperationException("不同类型无法比较");
        if (Type.Equals(ScriptType.Int)) return AsInt().CompareTo(other.AsInt());
        if (Type.Equals(ScriptType.String)) return string.Compare(AsString(), other.AsString(), StringComparison.Ordinal);
        if (Type.Equals(ScriptType.Double)) return AsDouble().CompareTo(other.AsDouble());
        throw new InvalidOperationException($"类型不支持比较 <{Type}>与<{other.Type}>");
    }

    public override bool Equals(object? obj) => obj is Value v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Type, _value);

    public static bool operator ==(Value left, Value right) => left.Equals(right);
    public static bool operator !=(Value left, Value right) => !left.Equals(right);
    public static bool operator <(Value left, Value right) => left.CompareTo(right) < 0;
    public static bool operator >(Value left, Value right) => left.CompareTo(right) > 0;
    public static bool operator <=(Value left, Value right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Value left, Value right) => left.CompareTo(right) >= 0;

    #endregion

    public override string ToString() => Type switch
    {
        GenericType { Definition.Name: "Array" } => $"[{string.Join(", ", AsArray())}]",
        _ => _value switch
        {
            double d => d.ToString("0.##"),
            bool b => b ? "true" : "false",
            long l => $"0x{l:X}",
            _ => _value?.ToString() ?? "void"
        }
    };
    // 辅助属性：判断是否为 Array<T>
    private bool IsArray => Type is GenericType { Definition.Name: "Array" };
    private ScriptType? ElementType => IsArray ? ((GenericType)Type).TypeArguments[0] : null;
    /// <summary>
    /// 连接两个相同类型的数组
    /// </summary>
    public Value Concat(Value other)
    {
        if (!IsArray || !other.IsArray)
            throw new InvalidOperationException("只有数组可以执行 Concat 操作");

        if (!Type.Equals(other.Type))
            throw new InvalidOperationException($"无法连接不同类型的数组：{Type} 和 {other.Type}");

        var newList = AsArray().AddRange(other.AsArray());
        return new Value(newList, Type);
    }

    /// <summary>
    /// 向数组末尾追加一个符合类型的元素
    /// </summary>
    public Value Append(Value item)
    {
        if (!IsArray)
            throw new InvalidOperationException("只有数组可以执行 Append 操作");

        var targetType = ElementType!;
        if (!targetType.IsAssignableFrom(item.Type))
            throw new InvalidOperationException($"类型约束冲突：无法向 {Type} 追加 {item.Type} 类型的元素");

        var newList = AsArray().Add(item);
        return new Value(newList, Type);
    }

    /// <summary>转换为布尔值，用于逻辑判断（int: 非0为真；bool: 自身；string: 非空为真；array: 非空为真）</summary>
    public bool ToBoolean()
    {
        if (Type.Equals(ScriptType.Int)) return AsInt() != 0;
        if (Type.Equals(ScriptType.Bool)) return AsBool();
        if (Type.Equals(ScriptType.String)) return !string.IsNullOrEmpty(AsString());
        if (IsArray) return AsArray().Count > 0;
        if (Type.Equals(ScriptType.Double)) return AsDouble() != 0.0;
        if (Type.Equals(ScriptType.Ptr)) return AsPtr() != 0;
        return false;
    }

    // 重载算数运算符
    public static Value operator +(Value left, Value right)
    {
        // 1. 排除 void 类型参与运算
        if (left.Type is VoidType || right.Type is VoidType)
            throw new InvalidOperationException("空类型不支持运算");

        // 2. 相同类型处理
        if (left.Type.Equals(right.Type))
        {
            // 处理基础标量类型
            if (left.Type.Equals(ScriptType.Int))
            {
                return FromInt(left.AsInt() + right.AsInt());
            }

            if (left.Type.Equals(ScriptType.Double))
            {
                return FromDouble(left.AsDouble() + right.AsDouble());
            }

            if (left.Type.Equals(ScriptType.String))
            {
                return FromString(left.AsString() + right.AsString());
            }

            // 处理泛型数组拼接 (Array<T> + Array<T>)
            if (left.IsArray)
            {
                return left.Concat(right);
            }
        }

        // 3. 混合类型处理：double 与 int 互相提升
        if (left.Type.Equals(ScriptType.Double) && right.Type.Equals(ScriptType.Int))
            return FromDouble(left.AsDouble() + right.AsInt());
        if (left.Type.Equals(ScriptType.Int) && right.Type.Equals(ScriptType.Double))
            return FromDouble(left.AsInt() + right.AsDouble());

        // 4. 混合类型处理：字符串拼接规则
        // 只要有一侧是 string，则将另一侧转为字符串进行拼接
        if (left.Type.Equals(ScriptType.String) || right.Type.Equals(ScriptType.String))
        {
            return FromString(left.ToString() + right.ToString());
        }

        // 5. 其他不支持的组合
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '+'");
    }


    public static Value operator &(Value left, Value right)
    {
        // 1. 排除 void 类型参与运算
        if (left.Type is VoidType || right.Type is VoidType)
            throw new InvalidOperationException("空类型不支持运算");
        // 2. 混合类型处理：字符串拼接规则
        // 只要有一侧是 string，则将另一侧转为字符串进行拼接
        if (left.Type.Equals(ScriptType.String) || right.Type.Equals(ScriptType.String))
        {
            return FromString(left.ToString() + right.ToString());
        }
        // 3. 其他不支持的组合
        throw new InvalidOperationException($"在 {left.Type} 和 {right.Type} 之间不支持操作 '&'");
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
/// 泛型占位符 (如 T)
/// </summary>
public sealed class TypeParameter(string name) : ScriptType
{
    public override string Name => name;
    public override bool IsAssignableFrom(ScriptType other) => Equals(other);
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
#endregion