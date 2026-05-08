using EasyCon.Script.Symbols;

namespace EasyCon.Tests;

[TestFixture]
public class ValueTests
{
    #region 工厂方法与类型标签

    [Test]
    public void FromInt_TypeIsInt()
    {
        var v = Value.FromInt(42);
        Assert.That(v.Type, Is.EqualTo(ScriptType.Int));
        Assert.That(v.AsInt(), Is.EqualTo(42));
    }

    [Test]
    public void FromBool_TypeIsBool()
    {
        var v = Value.FromBool(true);
        Assert.That(v.Type, Is.EqualTo(ScriptType.Bool));
        Assert.That(v.AsBool(), Is.True);
    }

    [Test]
    public void FromString_TypeIsString()
    {
        var v = Value.FromString("hello");
        Assert.That(v.Type, Is.EqualTo(ScriptType.String));
        Assert.That(v.AsString(), Is.EqualTo("hello"));
    }

    [Test]
    public void FromDouble_TypeIsDouble()
    {
        var v = Value.FromDouble(3.14);
        Assert.That(v.Type, Is.EqualTo(ScriptType.Double));
        Assert.That(v.AsDouble(), Is.EqualTo(3.14));
    }

    [Test]
    public void FromPtr_TypeIsPtr()
    {
        var v = Value.FromPtr(0xFFL);
        Assert.That(v.Type, Is.EqualTo(ScriptType.Ptr));
        Assert.That(v.AsPtr(), Is.EqualTo(0xFFL));
    }

    [Test]
    public void Void_TypeIsVoid()
    {
        Assert.That(Value.Void.Type, Is.EqualTo(ScriptType.Void));
    }

    [Test]
    public void CreateArray_TypeIsGenericArray()
    {
        var arr = Value.CreateArray(ScriptType.Int, [Value.FromInt(1), Value.FromInt(2)]);
        Assert.That(arr.Type.Name, Is.EqualTo("Array<int>"));
    }

    [Test]
    public void CreateArray_TypeMismatch_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Value.CreateArray(ScriptType.Int, [Value.FromString("wrong")]));
    }

    #endregion

    #region 隐式转换

    [Test]
    public void ImplicitConversion_Int()
    {
        Value v = 42;
        Assert.That(v.Type, Is.EqualTo(ScriptType.Int));
        Assert.That(v.AsInt(), Is.EqualTo(42));
    }

    [Test]
    public void ImplicitConversion_Bool()
    {
        Value v = true;
        Assert.That(v.Type, Is.EqualTo(ScriptType.Bool));
    }

    [Test]
    public void ImplicitConversion_String()
    {
        Value v = "hello";
        Assert.That(v.Type, Is.EqualTo(ScriptType.String));
    }

    [Test]
    public void ImplicitConversion_Double()
    {
        Value v = 3.14;
        Assert.That(v.Type, Is.EqualTo(ScriptType.Double));
    }

    [Test]
    public void ImplicitConversion_Ptr()
    {
        Value v = 100L;
        Assert.That(v.Type, Is.EqualTo(ScriptType.Ptr));
    }

    #endregion

    #region 算术运算符

    [Test]
    public void Add_IntInt()
    {
        var r = Value.FromInt(3) + Value.FromInt(5);
        Assert.That(r.AsInt(), Is.EqualTo(8));
    }

    [Test]
    public void Add_DoubleDouble()
    {
        var r = Value.FromDouble(1.5) + Value.FromDouble(2.5);
        Assert.That(r.AsDouble(), Is.EqualTo(4.0));
    }

    [Test]
    public void Add_StringString()
    {
        var r = Value.FromString("hello") + Value.FromString(" world");
        Assert.That(r.AsString(), Is.EqualTo("hello world"));
    }

    [Test]
    public void Add_IntDouble_PromotesToDouble()
    {
        var r = Value.FromInt(3) + Value.FromDouble(1.5);
        Assert.That(r.Type, Is.EqualTo(ScriptType.Double));
        Assert.That(r.AsDouble(), Is.EqualTo(4.5));
    }

    [Test]
    public void Add_DoubleInt_PromotesToDouble()
    {
        var r = Value.FromDouble(1.5) + Value.FromInt(3);
        Assert.That(r.AsDouble(), Is.EqualTo(4.5));
    }

    [Test]
    public void Add_StringInt_Concatenates()
    {
        var r = Value.FromString("count: ") + Value.FromInt(42);
        Assert.That(r.AsString(), Is.EqualTo("count: 42"));
    }

    [Test]
    public void Add_ArrayArray_Concatenates()
    {
        var a = Value.CreateArray(ScriptType.Int, [Value.FromInt(1), Value.FromInt(2)]);
        var b = Value.CreateArray(ScriptType.Int, [Value.FromInt(3)]);
        var r = a + b;
        Assert.That(r.AsArray().Count, Is.EqualTo(3));
    }

    [Test]
    public void Add_VoidOperand_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.Void + Value.FromInt(1));
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromInt(1) + Value.Void);
    }

    [Test]
    public void Add_UnsupportedTypes_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromInt(1) + Value.FromBool(true));
    }

    [Test]
    public void Sub_IntInt()
    {
        Assert.That((Value.FromInt(10) - Value.FromInt(4)).AsInt(), Is.EqualTo(6));
    }

    [Test]
    public void Sub_DoubleDouble()
    {
        Assert.That((Value.FromDouble(5.5) - Value.FromDouble(2.0)).AsDouble(), Is.EqualTo(3.5));
    }

    [Test]
    public void Sub_IntDouble()
    {
        Assert.That((Value.FromInt(5) - Value.FromDouble(1.5)).AsDouble(), Is.EqualTo(3.5));
    }

    [Test]
    public void Sub_UnsupportedTypes_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromString("a") - Value.FromString("b"));
    }

    [Test]
    public void Mul_IntInt()
    {
        Assert.That((Value.FromInt(3) * Value.FromInt(7)).AsInt(), Is.EqualTo(21));
    }

    [Test]
    public void Mul_DoubleDouble()
    {
        Assert.That((Value.FromDouble(2.5) * Value.FromDouble(4.0)).AsDouble(), Is.EqualTo(10.0));
    }

    [Test]
    public void Mul_IntDouble()
    {
        Assert.That((Value.FromInt(3) * Value.FromDouble(2.0)).AsDouble(), Is.EqualTo(6.0));
    }

    [Test]
    public void Div_IntInt()
    {
        Assert.That((Value.FromInt(17) / Value.FromInt(5)).AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void Div_DoubleDouble()
    {
        Assert.That((Value.FromDouble(7.0) / Value.FromDouble(2.0)).AsDouble(), Is.EqualTo(3.5));
    }

    [Test]
    public void Div_IntByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => _ = Value.FromInt(10) / Value.FromInt(0));
    }

    [Test]
    public void Mod_IntInt()
    {
        Assert.That((Value.FromInt(17) % Value.FromInt(5)).AsInt(), Is.EqualTo(2));
    }

    [Test]
    public void Mod_ByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => _ = Value.FromInt(10) % Value.FromInt(0));
    }

    [Test]
    public void Mod_UnsupportedTypes_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromDouble(1.0) % Value.FromDouble(2.0));
    }

    #endregion

    #region 位运算符

    [Test]
    public void BitAnd()
    {
        Assert.That((Value.FromInt(12) & Value.FromInt(10)).AsInt(), Is.EqualTo(8));
    }

    [Test]
    public void BitAnd_StringConcat()
    {
        // & 对字符串是拼接
        var r = Value.FromString("a") & Value.FromString("b");
        Assert.That(r.AsString(), Is.EqualTo("ab"));
    }

    [Test]
    public void BitAnd_UnsupportedTypes_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromInt(1) & Value.FromBool(true));
    }

    [Test]
    public void BitOr()
    {
        Assert.That((Value.FromInt(12) | Value.FromInt(10)).AsInt(), Is.EqualTo(14));
    }

    [Test]
    public void BitOr_UnsupportedTypes_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromInt(1) | Value.FromDouble(1.0));
    }

    [Test]
    public void BitXor()
    {
        Assert.That((Value.FromInt(12) ^ Value.FromInt(10)).AsInt(), Is.EqualTo(6));
    }

    [Test]
    public void BitXor_UnsupportedTypes_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromInt(1) ^ Value.FromString("x"));
    }

    #endregion

    #region 比较运算符

    [Test]
    public void Equals_IntSame()
    {
        Assert.That(Value.FromInt(42) == Value.FromInt(42), Is.True);
        Assert.That(Value.FromInt(42) == Value.FromInt(99), Is.False);
    }

    [Test]
    public void Equals_DifferentTypes()
    {
        Assert.That(Value.FromInt(1) == Value.FromBool(true), Is.False);
        Assert.That(Value.FromInt(0) == Value.FromString("0"), Is.False);
    }

    [Test]
    public void Equals_IntDouble_EqualValues()
    {
        Assert.That(Value.FromInt(3) == Value.FromDouble(3.0), Is.True);
        Assert.That(Value.FromDouble(3.0) == Value.FromInt(3), Is.True);
    }

    [Test]
    public void Equals_IntDouble_DifferentValues()
    {
        Assert.That(Value.FromInt(3) == Value.FromDouble(4.0), Is.False);
        Assert.That(Value.FromDouble(4.0) == Value.FromInt(3), Is.False);
    }

    [Test]
    public void Equals_StringCaseSensitive()
    {
        Assert.That(Value.FromString("abc") == Value.FromString("abc"), Is.True);
        Assert.That(Value.FromString("abc") == Value.FromString("ABC"), Is.False);
    }

    [Test]
    public void Equals_Array()
    {
        var a = Value.CreateArray(ScriptType.Int, [Value.FromInt(1), Value.FromInt(2)]);
        var b = Value.CreateArray(ScriptType.Int, [Value.FromInt(1), Value.FromInt(2)]);
        var c = Value.CreateArray(ScriptType.Int, [Value.FromInt(1), Value.FromInt(3)]);
        Assert.That(a == b, Is.True);
        Assert.That(a == c, Is.False);
    }

    [Test]
    public void CompareTo_Int()
    {
        Assert.That(Value.FromInt(3).CompareTo(Value.FromInt(5)), Is.LessThan(0));
        Assert.That(Value.FromInt(5).CompareTo(Value.FromInt(5)), Is.EqualTo(0));
        Assert.That(Value.FromInt(7).CompareTo(Value.FromInt(5)), Is.GreaterThan(0));
    }

    [Test]
    public void CompareTo_Double()
    {
        Assert.That(Value.FromDouble(1.5).CompareTo(Value.FromDouble(2.5)), Is.LessThan(0));
    }

    [Test]
    public void CompareTo_String()
    {
        Assert.That(Value.FromString("a").CompareTo(Value.FromString("b")), Is.LessThan(0));
    }

    [Test]
    public void CompareTo_IntDouble_EqualValues()
    {
        Assert.That(Value.FromInt(3).CompareTo(Value.FromDouble(3.0)), Is.EqualTo(0));
        Assert.That(Value.FromDouble(3.0).CompareTo(Value.FromInt(3)), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_IntDouble_LessThan()
    {
        Assert.That(Value.FromInt(3).CompareTo(Value.FromDouble(4.0)), Is.LessThan(0));
        Assert.That(Value.FromDouble(3.0).CompareTo(Value.FromInt(4)), Is.LessThan(0));
    }

    [Test]
    public void CompareTo_IntDouble_GreaterThan()
    {
        Assert.That(Value.FromInt(5).CompareTo(Value.FromDouble(4.0)), Is.GreaterThan(0));
        Assert.That(Value.FromDouble(5.0).CompareTo(Value.FromInt(4)), Is.GreaterThan(0));
    }

    [Test]
    public void CompareTo_DifferentTypes_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Value.FromInt(1).CompareTo(Value.FromString("1")));
    }

    [Test]
    public void CompareTo_UnsupportedType_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Value.FromBool(true).CompareTo(Value.FromBool(false)));
    }

    [Test]
    public void RelationalOperators()
    {
        Assert.That(Value.FromInt(3) < Value.FromInt(5), Is.True);
        Assert.That(Value.FromInt(5) > Value.FromInt(3), Is.True);
        Assert.That(Value.FromInt(5) <= Value.FromInt(5), Is.True);
        Assert.That(Value.FromInt(5) >= Value.FromInt(4), Is.True);
    }

    [Test]
    public void RelationalOperators_IntDouble()
    {
        // 小于比较
        Assert.That(Value.FromInt(3) < Value.FromDouble(5.0), Is.True);
        Assert.That(Value.FromDouble(3.0) < Value.FromInt(5), Is.True);

        // 大于比较
        Assert.That(Value.FromInt(5) > Value.FromDouble(3.0), Is.True);
        Assert.That(Value.FromDouble(5.0) > Value.FromInt(3), Is.True);

        // 小于等于比较
        Assert.That(Value.FromInt(5) <= Value.FromDouble(5.0), Is.True);
        Assert.That(Value.FromDouble(5.0) <= Value.FromInt(5), Is.True);

        // 大于等于比较
        Assert.That(Value.FromInt(5) >= Value.FromDouble(4.0), Is.True);
        Assert.That(Value.FromDouble(5.0) >= Value.FromInt(4), Is.True);
    }

    #endregion

    #region ToBoolean

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(-1, true)]
    public void ToBoolean_Int(int input, bool expected)
    {
        Assert.That(Value.FromInt(input).ToBoolean(), Is.EqualTo(expected));
    }

    [Test]
    public void ToBoolean_Bool()
    {
        Assert.That(Value.FromBool(false).ToBoolean(), Is.False);
        Assert.That(Value.FromBool(true).ToBoolean(), Is.True);
    }

    [TestCase("", false)]
    [TestCase("x", true)]
    public void ToBoolean_String(string input, bool expected)
    {
        Assert.That(Value.FromString(input).ToBoolean(), Is.EqualTo(expected));
    }

    [TestCase(0.0, false)]
    [TestCase(3.14, true)]
    public void ToBoolean_Double(double input, bool expected)
    {
        Assert.That(Value.FromDouble(input).ToBoolean(), Is.EqualTo(expected));
    }

    [TestCase(0L, false)]
    [TestCase(42L, true)]
    public void ToBoolean_Ptr(long input, bool expected)
    {
        Assert.That(Value.FromPtr(input).ToBoolean(), Is.EqualTo(expected));
    }

    [Test]
    public void ToBoolean_Array_Empty()
    {
        var arr = Value.CreateArray(ScriptType.Int, []);
        Assert.That(arr.ToBoolean(), Is.False);
    }

    [Test]
    public void ToBoolean_Array_NonEmpty()
    {
        var arr = Value.CreateArray(ScriptType.Int, [Value.FromInt(1)]);
        Assert.That(arr.ToBoolean(), Is.True);
    }

    [Test]
    public void ToBoolean_Void()
    {
        Assert.That(Value.Void.ToBoolean(), Is.False);
    }

    #endregion

    #region 索引与切片

    [Test]
    public void Index_String()
    {
        var s = Value.FromString("abc");
        Assert.That(s[0].AsString(), Is.EqualTo("a"));
        Assert.That(s[1].AsString(), Is.EqualTo("b"));
        Assert.That(s[2].AsString(), Is.EqualTo("c"));
    }

    [Test]
    public void Index_Array()
    {
        var a = Value.CreateArray(ScriptType.Int, [Value.FromInt(10), Value.FromInt(20), Value.FromInt(30)]);
        Assert.That(a[0].AsInt(), Is.EqualTo(10));
        Assert.That(a[1].AsInt(), Is.EqualTo(20));
        Assert.That(a[2].AsInt(), Is.EqualTo(30));
    }

    [Test]
    public void Index_UnsupportedType_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromInt(42)[0]);
    }

    [Test]
    public void Slice_String()
    {
        var s = Value.FromString("hello");
        var r = s[1..3];
        Assert.That(r.AsString(), Is.EqualTo("el"));
    }

    [Test]
    public void Slice_Array()
    {
        var a = Value.CreateArray(ScriptType.Int,
            [Value.FromInt(1), Value.FromInt(2), Value.FromInt(3), Value.FromInt(4), Value.FromInt(5)]);
        var r = a[1..3];
        Assert.That(r.AsArray().Count, Is.EqualTo(2));
        Assert.That(r.AsArray()[0].AsInt(), Is.EqualTo(2));
        Assert.That(r.AsArray()[1].AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void Slice_UnsupportedType_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromInt(42)[0..1]);
    }

    #endregion

    #region Length

    [Test]
    public void Length_String()
    {
        Assert.That(Value.FromString("hello").Length, Is.EqualTo(5));
        Assert.That(Value.FromString("").Length, Is.EqualTo(0));
    }

    [Test]
    public void Length_Array()
    {
        var a = Value.CreateArray(ScriptType.Int, [Value.FromInt(1), Value.FromInt(2)]);
        Assert.That(a.Length, Is.EqualTo(2));
    }

    [Test]
    public void Length_NonContainer_ReturnsZero()
    {
        Assert.That(Value.FromInt(42).Length, Is.EqualTo(0));
        Assert.That(Value.Void.Length, Is.EqualTo(0));
    }

    #endregion

    #region Concat / Append

    [Test]
    public void Concat_Arrays()
    {
        var a = Value.CreateArray(ScriptType.Int, [Value.FromInt(1)]);
        var b = Value.CreateArray(ScriptType.Int, [Value.FromInt(2), Value.FromInt(3)]);
        var r = a.Concat(b);
        Assert.That(r.AsArray().Count, Is.EqualTo(3));
    }

    [Test]
    public void Concat_DifferentTypes_Throws()
    {
        var a = Value.CreateArray(ScriptType.Int, [Value.FromInt(1)]);
        var b = Value.CreateArray(ScriptType.String, [Value.FromString("x")]);
        Assert.Throws<InvalidOperationException>(() => a.Concat(b));
    }

    [Test]
    public void Concat_NonArray_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Value.FromInt(1).Concat(Value.FromInt(2)));
    }

    [Test]
    public void Append_ToArray()
    {
        var a = Value.CreateArray(ScriptType.Int, [Value.FromInt(1), Value.FromInt(2)]);
        var r = a.Append(Value.FromInt(3));
        Assert.That(r.AsArray().Count, Is.EqualTo(3));
        Assert.That(r.AsArray()[2].AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void Append_TypeMismatch_Throws()
    {
        var a = Value.CreateArray(ScriptType.Int, [Value.FromInt(1)]);
        Assert.Throws<InvalidOperationException>(() => a.Append(Value.FromString("wrong")));
    }

    [Test]
    public void Append_NonArray_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Value.FromInt(1).Append(Value.FromInt(2)));
    }

    [Test]
    public void Append_DoesNotMutateOriginal()
    {
        var a = Value.CreateArray(ScriptType.Int, [Value.FromInt(1)]);
        var b = a.Append(Value.FromInt(2));
        Assert.That(a.AsArray().Count, Is.EqualTo(1));
        Assert.That(b.AsArray().Count, Is.EqualTo(2));
    }

    #endregion

    #region ToString

    [Test]
    public void ToString_Int()
    {
        Assert.That(Value.FromInt(42).ToString(), Is.EqualTo("42"));
    }

    [Test]
    public void ToString_Bool()
    {
        Assert.That(Value.FromBool(true).ToString(), Is.EqualTo("true"));
        Assert.That(Value.FromBool(false).ToString(), Is.EqualTo("false"));
    }

    [Test]
    public void ToString_String()
    {
        Assert.That(Value.FromString("hello").ToString(), Is.EqualTo("hello"));
    }

    [Test]
    public void ToString_Array()
    {
        var a = Value.CreateArray(ScriptType.Int, [Value.FromInt(1), Value.FromInt(2)]);
        Assert.That(a.ToString(), Is.EqualTo("[1, 2]"));
    }

    [Test]
    public void ToString_Void()
    {
        Assert.That(Value.Void.ToString(), Is.EqualTo("void"));
    }

    #endregion

    #region 边界情况

    [Test]
    public void IntOverflow_WrapsAround()
    {
        var r = Value.FromInt(int.MaxValue) + Value.FromInt(1);
        Assert.That(r.AsInt(), Is.EqualTo(int.MinValue)); // unchecked overflow
    }

    [Test]
    public void IntNegativeArithmetic()
    {
        var r = Value.FromInt(-3) + Value.FromInt(-5);
        Assert.That(r.AsInt(), Is.EqualTo(-8));
    }

    [Test]
    public void DoubleDivisionByZero_ReturnsInfinity()
    {
        var r = Value.FromDouble(1.0) / Value.FromDouble(0.0);
        Assert.That(double.IsInfinity(r.AsDouble()), Is.True);
    }

    [Test]
    public void EmptyString_Concatenation()
    {
        var r = Value.FromString("") + Value.FromString("hello");
        Assert.That(r.AsString(), Is.EqualTo("hello"));
    }

    [Test]
    public void EmptyArray_Concatenation()
    {
        var a = Value.CreateArray(ScriptType.Int, []);
        var b = Value.CreateArray(ScriptType.Int, [Value.FromInt(1)]);
        var r = a + b;
        Assert.That(r.AsArray().Count, Is.EqualTo(1));
    }

    #endregion
}