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
    public void CreateArray_TypeIsArray()
    {
        var arr = Value.CreateArray(ScriptType.Int, [Value.FromInt(1), Value.FromInt(2)]);
        Assert.That(arr.Type, Is.InstanceOf<ArrayType>());
        Assert.That(((ArrayType)arr.Type).ElementType, Is.EqualTo(ScriptType.Int));
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
    public void Add_IntDouble_Throws_RuntimeNoPromotion()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromInt(3) + Value.FromDouble(1.5));
    }

    [Test]
    public void Add_DoubleInt_Throws_RuntimeNoPromotion()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromDouble(1.5) + Value.FromInt(3));
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
        Assert.That(r.AsArray().Length, Is.EqualTo(3));
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
    public void Add_ByteByte()
    {
        var r = Value.FromByte(3) + Value.FromByte(5);
        Assert.That(r.AsByte(), Is.EqualTo(8));
    }

    [Test]
    public void Add_UIntUInt()
    {
        var r = Value.FromUInt(100u) + Value.FromUInt(200u);
        Assert.That(r.AsUInt(), Is.EqualTo(300u));
    }

    [Test]
    public void Add_UInt64UInt64()
    {
        var r = Value.FromUInt64(1000ul) + Value.FromUInt64(2000ul);
        Assert.That(r.AsUInt64(), Is.EqualTo(3000ul));
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
    public void Sub_IntDouble_Throws_RuntimeNoPromotion()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromInt(5) - Value.FromDouble(1.5));
    }

    [Test]
    public void Sub_UnsupportedTypes_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromString("a") - Value.FromString("b"));
    }

    [Test]
    public void Sub_ByteByte()
    {
        Assert.That((Value.FromByte(10) - Value.FromByte(4)).AsByte(), Is.EqualTo(6));
    }

    [Test]
    public void Sub_UIntUInt()
    {
        Assert.That((Value.FromUInt(300u) - Value.FromUInt(100u)).AsUInt(), Is.EqualTo(200u));
    }

    [Test]
    public void Sub_UInt64UInt64()
    {
        Assert.That((Value.FromUInt64(3000ul) - Value.FromUInt64(1000ul)).AsUInt64(), Is.EqualTo(2000ul));
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
    public void Mul_IntDouble_Throws_RuntimeNoPromotion()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromInt(3) * Value.FromDouble(2.0));
    }

    [Test]
    public void Mul_ByteByte()
    {
        Assert.That((Value.FromByte(3) * Value.FromByte(7)).AsByte(), Is.EqualTo(21));
    }

    [Test]
    public void Mul_UIntUInt()
    {
        Assert.That((Value.FromUInt(3u) * Value.FromUInt(7u)).AsUInt(), Is.EqualTo(21u));
    }

    [Test]
    public void Mul_UInt64UInt64()
    {
        Assert.That((Value.FromUInt64(3ul) * Value.FromUInt64(7ul)).AsUInt64(), Is.EqualTo(21ul));
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
    public void Div_ByteByte()
    {
        Assert.That((Value.FromByte(17) / Value.FromByte(5)).AsByte(), Is.EqualTo(3));
    }

    [Test]
    public void Div_ByteByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => _ = Value.FromByte(10) / Value.FromByte(0));
    }

    [Test]
    public void Div_UIntUInt()
    {
        Assert.That((Value.FromUInt(17u) / Value.FromUInt(5u)).AsUInt(), Is.EqualTo(3u));
    }

    [Test]
    public void Div_UIntByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => _ = Value.FromUInt(10u) / Value.FromUInt(0u));
    }

    [Test]
    public void Div_UInt64UInt64()
    {
        Assert.That((Value.FromUInt64(17ul) / Value.FromUInt64(5ul)).AsUInt64(), Is.EqualTo(3ul));
    }

    [Test]
    public void Div_UInt64ByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => _ = Value.FromUInt64(10ul) / Value.FromUInt64(0ul));
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

    [Test]
    public void Mod_ByteByte()
    {
        Assert.That((Value.FromByte(17) % Value.FromByte(5)).AsByte(), Is.EqualTo(2));
    }

    [Test]
    public void Mod_UIntUInt()
    {
        Assert.That((Value.FromUInt(17u) % Value.FromUInt(5u)).AsUInt(), Is.EqualTo(2u));
    }

    [Test]
    public void Mod_UInt64UInt64()
    {
        Assert.That((Value.FromUInt64(17ul) % Value.FromUInt64(5ul)).AsUInt64(), Is.EqualTo(2ul));
    }

    [Test]
    public void Mod_ByteByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => _ = Value.FromByte(10) % Value.FromByte(0));
    }

    [Test]
    public void Mod_UIntByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => _ = Value.FromUInt(10u) % Value.FromUInt(0u));
    }

    [Test]
    public void Mod_UInt64ByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => _ = Value.FromUInt64(10ul) % Value.FromUInt64(0ul));
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
    public void BitAnd_Byte()
    {
        Assert.That((Value.FromByte(12) & Value.FromByte(10)).AsByte(), Is.EqualTo(8));
    }

    [Test]
    public void BitAnd_UInt()
    {
        Assert.That((Value.FromUInt(12u) & Value.FromUInt(10u)).AsUInt(), Is.EqualTo(8u));
    }

    [Test]
    public void BitAnd_UInt64()
    {
        Assert.That((Value.FromUInt64(12ul) & Value.FromUInt64(10ul)).AsUInt64(), Is.EqualTo(8ul));
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
    public void BitOr_Byte()
    {
        Assert.That((Value.FromByte(12) | Value.FromByte(10)).AsByte(), Is.EqualTo(14));
    }

    [Test]
    public void BitOr_UInt()
    {
        Assert.That((Value.FromUInt(12u) | Value.FromUInt(10u)).AsUInt(), Is.EqualTo(14u));
    }

    [Test]
    public void BitOr_UInt64()
    {
        Assert.That((Value.FromUInt64(12ul) | Value.FromUInt64(10ul)).AsUInt64(), Is.EqualTo(14ul));
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

    [Test]
    public void BitXor_Byte()
    {
        Assert.That((Value.FromByte(12) ^ Value.FromByte(10)).AsByte(), Is.EqualTo(6));
    }

    [Test]
    public void BitXor_UInt()
    {
        Assert.That((Value.FromUInt(12u) ^ Value.FromUInt(10u)).AsUInt(), Is.EqualTo(6u));
    }

    [Test]
    public void BitXor_UInt64()
    {
        Assert.That((Value.FromUInt64(12ul) ^ Value.FromUInt64(10ul)).AsUInt64(), Is.EqualTo(6ul));
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
    public void Equals_IntDouble_DifferentTypes_ReturnsFalse()
    {
        Assert.That(Value.FromInt(3) == Value.FromDouble(3.0), Is.False);
        Assert.That(Value.FromDouble(3.0) == Value.FromInt(3), Is.False);
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
    public void CompareTo_IntDouble_DifferentTypes_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Value.FromInt(3).CompareTo(Value.FromDouble(3.0)));
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
    public void Equals_ByteSame()
    {
        Assert.That(Value.FromByte(42) == Value.FromByte(42), Is.True);
        Assert.That(Value.FromByte(42) == Value.FromByte(99), Is.False);
    }

    [Test]
    public void CompareTo_Byte()
    {
        Assert.That(Value.FromByte(3).CompareTo(Value.FromByte(5)), Is.LessThan(0));
        Assert.That(Value.FromByte(5).CompareTo(Value.FromByte(5)), Is.EqualTo(0));
        Assert.That(Value.FromByte(7).CompareTo(Value.FromByte(5)), Is.GreaterThan(0));
    }

    [Test]
    public void Equals_UIntSame()
    {
        Assert.That(Value.FromUInt(42u) == Value.FromUInt(42u), Is.True);
        Assert.That(Value.FromUInt(42u) == Value.FromUInt(99u), Is.False);
    }

    [Test]
    public void CompareTo_UInt()
    {
        Assert.That(Value.FromUInt(3u).CompareTo(Value.FromUInt(5u)), Is.LessThan(0));
        Assert.That(Value.FromUInt(5u).CompareTo(Value.FromUInt(5u)), Is.EqualTo(0));
        Assert.That(Value.FromUInt(7u).CompareTo(Value.FromUInt(5u)), Is.GreaterThan(0));
    }

    [Test]
    public void Equals_UInt64Same()
    {
        Assert.That(Value.FromUInt64(42ul) == Value.FromUInt64(42ul), Is.True);
        Assert.That(Value.FromUInt64(42ul) == Value.FromUInt64(99ul), Is.False);
    }

    [Test]
    public void CompareTo_UInt64()
    {
        Assert.That(Value.FromUInt64(3ul).CompareTo(Value.FromUInt64(5ul)), Is.LessThan(0));
        Assert.That(Value.FromUInt64(5ul).CompareTo(Value.FromUInt64(5ul)), Is.EqualTo(0));
        Assert.That(Value.FromUInt64(7ul).CompareTo(Value.FromUInt64(5ul)), Is.GreaterThan(0));
    }

    [Test]
    public void Equals_BoolSame()
    {
        Assert.That(Value.FromBool(true) == Value.FromBool(true), Is.True);
        Assert.That(Value.FromBool(false) == Value.FromBool(false), Is.True);
        Assert.That(Value.FromBool(true) == Value.FromBool(false), Is.False);
    }

    [Test]
    public void Equals_DoubleSame()
    {
        Assert.That(Value.FromDouble(3.14) == Value.FromDouble(3.14), Is.True);
        Assert.That(Value.FromDouble(3.14) == Value.FromDouble(2.71), Is.False);
    }

    [Test]
    public void Equals_Double_NaN()
    {
        // NaN != NaN 按 IEEE 754 规则
        Assert.That(Value.FromDouble(double.NaN) == Value.FromDouble(double.NaN), Is.False);
    }

    [Test]
    public void Equals_PtrWithZero()
    {
        // PTR 与 INT 0 可以比较
        Assert.That(Value.FromPtr(0L) == Value.FromInt(0), Is.True);
        Assert.That(Value.FromPtr(0L) == Value.FromInt(1), Is.False);
        Assert.That(Value.FromPtr(42L) == Value.FromInt(0), Is.False);
    }

    [Test]
    public void CompareTo_PtrWithZero()
    {
        Assert.That(Value.FromPtr(0L).CompareTo(Value.FromInt(0)), Is.EqualTo(0));
        Assert.That(Value.FromPtr(5L).CompareTo(Value.FromInt(3)), Is.GreaterThan(0));
        Assert.That(Value.FromPtr(0L).CompareTo(Value.FromInt(1)), Is.LessThan(0));
    }

    [Test]
    public void Equals_PtrSame()
    {
        Assert.That(Value.FromPtr(0xFFL) == Value.FromPtr(0xFFL), Is.True);
        Assert.That(Value.FromPtr(0xFFL) == Value.FromPtr(0x00L), Is.False);
    }

    [Test]
    public void CompareTo_Ptr()
    {
        Assert.That(Value.FromPtr(3L).CompareTo(Value.FromPtr(5L)), Is.LessThan(0));
        Assert.That(Value.FromPtr(5L).CompareTo(Value.FromPtr(5L)), Is.EqualTo(0));
        Assert.That(Value.FromPtr(7L).CompareTo(Value.FromPtr(5L)), Is.GreaterThan(0));
    }

    [Test]
    public void RelationalOperators_Byte()
    {
        Assert.That(Value.FromByte(3) < Value.FromByte(5), Is.True);
        Assert.That(Value.FromByte(5) > Value.FromByte(3), Is.True);
        Assert.That(Value.FromByte(5) <= Value.FromByte(5), Is.True);
        Assert.That(Value.FromByte(5) >= Value.FromByte(4), Is.True);
    }

    [Test]
    public void RelationalOperators_UInt()
    {
        Assert.That(Value.FromUInt(3u) < Value.FromUInt(5u), Is.True);
        Assert.That(Value.FromUInt(5u) > Value.FromUInt(3u), Is.True);
        Assert.That(Value.FromUInt(5u) <= Value.FromUInt(5u), Is.True);
        Assert.That(Value.FromUInt(5u) >= Value.FromUInt(4u), Is.True);
    }

    [Test]
    public void RelationalOperators_UInt64()
    {
        Assert.That(Value.FromUInt64(3ul) < Value.FromUInt64(5ul), Is.True);
        Assert.That(Value.FromUInt64(5ul) > Value.FromUInt64(3ul), Is.True);
        Assert.That(Value.FromUInt64(5ul) <= Value.FromUInt64(5ul), Is.True);
        Assert.That(Value.FromUInt64(5ul) >= Value.FromUInt64(4ul), Is.True);
    }

    [Test]
    public void RelationalOperators_Double()
    {
        Assert.That(Value.FromDouble(1.5) < Value.FromDouble(2.5), Is.True);
        Assert.That(Value.FromDouble(2.5) > Value.FromDouble(1.5), Is.True);
        Assert.That(Value.FromDouble(2.5) <= Value.FromDouble(2.5), Is.True);
        Assert.That(Value.FromDouble(2.5) >= Value.FromDouble(1.5), Is.True);
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
    public void RelationalOperators_IntDouble_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Value.FromInt(3) < Value.FromDouble(5.0));
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

    [TestCase((byte)0, false)]
    [TestCase((byte)1, true)]
    [TestCase((byte)255, true)]
    public void ToBoolean_Byte(byte input, bool expected)
    {
        Assert.That(Value.FromByte(input).ToBoolean(), Is.EqualTo(expected));
    }

    [TestCase(0u, false)]
    [TestCase(1u, true)]
    public void ToBoolean_UInt(uint input, bool expected)
    {
        Assert.That(Value.FromUInt(input).ToBoolean(), Is.EqualTo(expected));
    }

    [TestCase(0ul, false)]
    [TestCase(1ul, true)]
    public void ToBoolean_UInt64(ulong input, bool expected)
    {
        Assert.That(Value.FromUInt64(input).ToBoolean(), Is.EqualTo(expected));
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
        Assert.That(r.AsArray().Length, Is.EqualTo(2));
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
        Assert.That(r.AsArray().Length, Is.EqualTo(3));
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
        Assert.That(r.AsArray().Length, Is.EqualTo(3));
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
        Assert.That(a.AsArray().Length, Is.EqualTo(1));
        Assert.That(b.AsArray().Length, Is.EqualTo(2));
    }

    [Test]
    public void Contains_String()
    {
        var s = Value.FromString("hello");
        Assert.That(s.Contains(Value.FromString("ell")), Is.True);
        Assert.That(s.Contains(Value.FromString("ELL")), Is.False);
        Assert.That(s.Contains(Value.FromString("")), Is.True);
    }

    [Test]
    public void Contains_Array()
    {
        var a = Value.CreateArray(ScriptType.Int, [Value.FromInt(1), Value.FromInt(2)]);
        Assert.That(a.Contains(Value.FromInt(2)), Is.True);
        Assert.That(a.Contains(Value.FromInt(3)), Is.False);
        Assert.That(a.Contains(Value.FromString("2")), Is.False);
    }

    [Test]
    public void Contains_NonContainer_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Value.FromInt(1).Contains(Value.FromInt(1)));
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

    [Test]
    public void ToString_Byte()
    {
        Assert.That(Value.FromByte(42).ToString(), Is.EqualTo("42"));
    }

    [Test]
    public void ToString_UInt()
    {
        Assert.That(Value.FromUInt(42u).ToString(), Is.EqualTo("42"));
    }

    [Test]
    public void ToString_UInt64()
    {
        Assert.That(Value.FromUInt64(42ul).ToString(), Is.EqualTo("42"));
    }

    [Test]
    public void ToString_Double()
    {
        Assert.That(Value.FromDouble(3.14).ToString(), Is.EqualTo("3.14"));
    }

    [Test]
    public void ToString_Ptr()
    {
        Assert.That(Value.FromPtr(0xFFL).ToString(), Is.EqualTo("0xFF"));
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
        Assert.That(r.AsArray().Length, Is.EqualTo(1));
    }

    #endregion
}