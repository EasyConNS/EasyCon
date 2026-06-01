using System.Runtime.InteropServices;

namespace EasyCon.Script.Binding.Ssa;

/// <summary>
/// SCCP 格标签：Top（未求值）、Const（已知常量）、Bottom（非常量/过度定义）。
/// 格半序：Top &gt; Const &gt; Bottom；meet 单调下降，保证算法终止。
/// </summary>
internal enum LatticeTag : byte
{
    Top,
    Const,
    Bottom,
}

/// <summary>
/// SCCP 格值：零分配 tagged struct，复用 ConstPayload 存储常量载荷。
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct LatticeValue
{
    public LatticeTag Tag;
    public ConstPayload Value;
    public SsaOp ConstKind; // 仅 Tag==Const 时有效

    // ---- 工厂方法 ----

    public static LatticeValue Top() => new() { Tag = LatticeTag.Top };

    public static LatticeValue Bottom() => new() { Tag = LatticeTag.Bottom };

    public static LatticeValue ConstInt(int v)
    {
        var p = new ConstPayload(); p.SetInt(v);
        return new() { Tag = LatticeTag.Const, ConstKind = SsaOp.ConstInt, Value = p };
    }

    public static LatticeValue ConstBool(bool v)
    {
        var p = new ConstPayload(); p.SetBool(v);
        return new() { Tag = LatticeTag.Const, ConstKind = SsaOp.ConstBool, Value = p };
    }

    public static LatticeValue ConstDouble(double v)
    {
        var p = new ConstPayload(); p.SetDouble(v);
        return new() { Tag = LatticeTag.Const, ConstKind = SsaOp.ConstDouble, Value = p };
    }

    public static LatticeValue ConstByte(byte v)
    {
        var p = new ConstPayload(); p.SetByte(v);
        return new() { Tag = LatticeTag.Const, ConstKind = SsaOp.ConstByte, Value = p };
    }

    public static LatticeValue ConstUInt(uint v)
    {
        var p = new ConstPayload(); p.SetUInt(v);
        return new() { Tag = LatticeTag.Const, ConstKind = SsaOp.ConstUInt, Value = p };
    }

    public static LatticeValue ConstUInt64(ulong v)
    {
        var p = new ConstPayload(); p.SetUInt64(v);
        return new() { Tag = LatticeTag.Const, ConstKind = SsaOp.ConstUInt64, Value = p };
    }

    public static LatticeValue ConstString(string v) => new()
    {
        Tag = LatticeTag.Const,
        ConstKind = SsaOp.ConstString,
        // 字符串需要特殊处理，不存 ConstPayload
    };

    public static LatticeValue ConstPtr(long v)
    {
        var p = new ConstPayload(); p.SetPtr(v);
        return new() { Tag = LatticeTag.Const, ConstKind = SsaOp.ConstPtr, Value = p };
    }

    // ---- 从 SsaValue 的常量载荷创建 ----

    public static LatticeValue FromConstant(SsaValue v)
    {
        return v.Op switch
        {
            SsaOp.ConstBool => ConstBool(v.Const.GetBool()),
            SsaOp.ConstByte => ConstByte(v.Const.GetByte()),
            SsaOp.ConstInt => ConstInt(v.Const.GetInt()),
            SsaOp.ConstUInt => ConstUInt(v.Const.GetUInt()),
            SsaOp.ConstUInt64 => ConstUInt64(v.Const.GetUInt64()),
            SsaOp.ConstDouble => ConstDouble(v.Const.GetDouble()),
            SsaOp.ConstPtr => ConstPtr(v.Const.GetPtr()),
            SsaOp.ConstString => ConstString(v.ConstString ?? ""),
            _ => Bottom(),
        };
    }

    // ---- Meet 操作 ----

    /// <summary>
    /// 格 meet（最大下界）：Top∧x=x, Const∧Const=same→Const/diff→Bottom, x∧Bottom=Bottom。
    /// 返回是否发生了变化（新值 != 旧值 a）。
    /// </summary>
    public static bool Meet(ref LatticeValue a, LatticeValue b)
    {
        // Top ∧ x = x（无论 x 是什么）
        if (a.Tag == LatticeTag.Top)
        {
            a = b;
            return a.Tag != LatticeTag.Top;
        }

        // x ∧ Top = x（不变）
        if (b.Tag == LatticeTag.Top)
            return false;

        // Bottom ∧ x = Bottom（不变）
        if (a.Tag == LatticeTag.Bottom)
            return false;

        // x ∧ Bottom = Bottom（若 a 还不是 Bottom 则变化）
        if (b.Tag == LatticeTag.Bottom)
        {
            if (a.Tag != LatticeTag.Bottom)
            {
                a = Bottom();
                return true;
            }
            return false;
        }

        // Const ∧ Const → 相同常量则不变，否则 Bottom
        if (a.ConstKind != b.ConstKind || !EqualPayload(a, b))
        {
            a = Bottom();
            return true;
        }
        return false;
    }

    // ---- Evaluate：给定操作码和格操作数计算结果 ----

    /// <summary>
    /// 对二元操作求值。任一操作数为 Top → Top；任一操作数为 Bottom → Bottom；
    /// 两者均为 Const → 尝试折叠；折叠失败 → Bottom。
    /// </summary>
    public static LatticeValue EvaluateBinary(SsaOp op, LatticeValue left, LatticeValue right)
    {
        if (left.Tag == LatticeTag.Top || right.Tag == LatticeTag.Top)
            return Top();
        if (left.Tag == LatticeTag.Bottom || right.Tag == LatticeTag.Bottom)
            return Bottom();

        // 两者均为 Const → 折叠
        return FoldBinary(op, ref left, ref right);
    }

    /// <summary>
    /// 对一元操作求值。
    /// </summary>
    public static LatticeValue EvaluateUnary(SsaOp op, LatticeValue operand)
    {
        if (operand.Tag == LatticeTag.Top)
            return Top();
        if (operand.Tag == LatticeTag.Bottom)
            return Bottom();

        return FoldUnary(op, ref operand);
    }

    // ---- 内部：常量折叠 ----

    private static LatticeValue FoldBinary(SsaOp op, ref LatticeValue left, ref LatticeValue right)
    {
        switch (op)
        {
            // int 算术
            case SsaOp.AddInt: return ConstInt(left.Value.GetInt() + right.Value.GetInt());
            case SsaOp.SubInt: return ConstInt(left.Value.GetInt() - right.Value.GetInt());
            case SsaOp.MulInt: return ConstInt(left.Value.GetInt() * right.Value.GetInt());
            case SsaOp.DivInt:
                if (right.Value.GetInt() == 0) return Bottom(); // 除零不折叠
                return ConstInt(left.Value.GetInt() / right.Value.GetInt());
            case SsaOp.ModInt:
                if (right.Value.GetInt() == 0) return Bottom();
                return ConstInt(left.Value.GetInt() % right.Value.GetInt());
            case SsaOp.RoundDivInt:
                if (right.Value.GetInt() == 0) return Bottom();
                return ConstInt((int)Math.Round((double)left.Value.GetInt() / right.Value.GetInt()));

            // uint 算术
            case SsaOp.AddUInt: return ConstUInt(left.Value.GetUInt() + right.Value.GetUInt());
            case SsaOp.SubUInt: return ConstUInt(left.Value.GetUInt() - right.Value.GetUInt());
            case SsaOp.MulUInt: return ConstUInt(left.Value.GetUInt() * right.Value.GetUInt());
            case SsaOp.DivUInt:
                if (right.Value.GetUInt() == 0) return Bottom();
                return ConstUInt(left.Value.GetUInt() / right.Value.GetUInt());
            case SsaOp.ModUInt:
                if (right.Value.GetUInt() == 0) return Bottom();
                return ConstUInt(left.Value.GetUInt() % right.Value.GetUInt());

            // uint64 算术
            case SsaOp.AddUInt64: return ConstUInt64(left.Value.GetUInt64() + right.Value.GetUInt64());
            case SsaOp.SubUInt64: return ConstUInt64(left.Value.GetUInt64() - right.Value.GetUInt64());
            case SsaOp.MulUInt64: return ConstUInt64(left.Value.GetUInt64() * right.Value.GetUInt64());
            case SsaOp.DivUInt64:
                if (right.Value.GetUInt64() == 0) return Bottom();
                return ConstUInt64(left.Value.GetUInt64() / right.Value.GetUInt64());
            case SsaOp.ModUInt64:
                if (right.Value.GetUInt64() == 0) return Bottom();
                return ConstUInt64(left.Value.GetUInt64() % right.Value.GetUInt64());

            // double 算术
            case SsaOp.AddDouble: return ConstDouble(left.Value.GetDouble() + right.Value.GetDouble());
            case SsaOp.SubDouble: return ConstDouble(left.Value.GetDouble() - right.Value.GetDouble());
            case SsaOp.MulDouble: return ConstDouble(left.Value.GetDouble() * right.Value.GetDouble());
            case SsaOp.DivDouble: return ConstDouble(left.Value.GetDouble() / right.Value.GetDouble());

            // int 比较
            case SsaOp.EqInt: return ConstBool(left.Value.GetInt() == right.Value.GetInt());
            case SsaOp.NeqInt: return ConstBool(left.Value.GetInt() != right.Value.GetInt());
            case SsaOp.LtInt: return ConstBool(left.Value.GetInt() < right.Value.GetInt());
            case SsaOp.LeqInt: return ConstBool(left.Value.GetInt() <= right.Value.GetInt());
            case SsaOp.GtInt: return ConstBool(left.Value.GetInt() > right.Value.GetInt());
            case SsaOp.GeqInt: return ConstBool(left.Value.GetInt() >= right.Value.GetInt());

            // uint 比较
            case SsaOp.EqUInt: return ConstBool(left.Value.GetUInt() == right.Value.GetUInt());
            case SsaOp.NeqUInt: return ConstBool(left.Value.GetUInt() != right.Value.GetUInt());
            case SsaOp.LtUInt: return ConstBool(left.Value.GetUInt() < right.Value.GetUInt());
            case SsaOp.LeqUInt: return ConstBool(left.Value.GetUInt() <= right.Value.GetUInt());
            case SsaOp.GtUInt: return ConstBool(left.Value.GetUInt() > right.Value.GetUInt());
            case SsaOp.GeqUInt: return ConstBool(left.Value.GetUInt() >= right.Value.GetUInt());

            // uint64 比较
            case SsaOp.EqUInt64: return ConstBool(left.Value.GetUInt64() == right.Value.GetUInt64());
            case SsaOp.NeqUInt64: return ConstBool(left.Value.GetUInt64() != right.Value.GetUInt64());
            case SsaOp.LtUInt64: return ConstBool(left.Value.GetUInt64() < right.Value.GetUInt64());
            case SsaOp.LeqUInt64: return ConstBool(left.Value.GetUInt64() <= right.Value.GetUInt64());
            case SsaOp.GtUInt64: return ConstBool(left.Value.GetUInt64() > right.Value.GetUInt64());
            case SsaOp.GeqUInt64: return ConstBool(left.Value.GetUInt64() >= right.Value.GetUInt64());

            // double 比较
            case SsaOp.EqDouble: return ConstBool(left.Value.GetDouble() == right.Value.GetDouble());
            case SsaOp.NeqDouble: return ConstBool(left.Value.GetDouble() != right.Value.GetDouble());
            case SsaOp.LtDouble: return ConstBool(left.Value.GetDouble() < right.Value.GetDouble());
            case SsaOp.LeqDouble: return ConstBool(left.Value.GetDouble() <= right.Value.GetDouble());
            case SsaOp.GtDouble: return ConstBool(left.Value.GetDouble() > right.Value.GetDouble());
            case SsaOp.GeqDouble: return ConstBool(left.Value.GetDouble() >= right.Value.GetDouble());

            // bool/string/ptr/byte 比较
            case SsaOp.EqBool: return ConstBool(left.Value.GetBool() == right.Value.GetBool());
            case SsaOp.NeqBool: return ConstBool(left.Value.GetBool() != right.Value.GetBool());
            case SsaOp.EqByte: return ConstBool(left.Value.GetInt() == right.Value.GetInt());
            case SsaOp.NeqByte: return ConstBool(left.Value.GetInt() != right.Value.GetInt());
            case SsaOp.LtByte: return ConstBool((byte)left.Value.GetInt() < (byte)right.Value.GetInt());
            case SsaOp.LeqByte: return ConstBool((byte)left.Value.GetInt() <= (byte)right.Value.GetInt());
            case SsaOp.GtByte: return ConstBool((byte)left.Value.GetInt() > (byte)right.Value.GetInt());
            case SsaOp.GeqByte: return ConstBool((byte)left.Value.GetInt() >= (byte)right.Value.GetInt());
            case SsaOp.EqPtr: return ConstBool(left.Value.GetPtr() == right.Value.GetPtr());
            case SsaOp.NeqPtr: return ConstBool(left.Value.GetPtr() != right.Value.GetPtr());

            // 位运算
            case SsaOp.AndInt: return ConstInt(left.Value.GetInt() & right.Value.GetInt());
            case SsaOp.OrInt: return ConstInt(left.Value.GetInt() | right.Value.GetInt());
            case SsaOp.XorInt: return ConstInt(left.Value.GetInt() ^ right.Value.GetInt());
            case SsaOp.ShlInt: return ConstInt(left.Value.GetInt() << right.Value.GetInt());
            case SsaOp.ShrInt: return ConstInt(left.Value.GetInt() >> right.Value.GetInt());

            // Concat（仅两个都是 ConstString 时）
            case SsaOp.Concat:
                return Bottom(); // 字符串拼接需要 ConstString 特殊处理，暂不在此处支持

            default:
                return Bottom();
        }
    }

    private static LatticeValue FoldUnary(SsaOp op, ref LatticeValue operand)
    {
        switch (op)
        {
            case SsaOp.LogicNot: return ConstBool(!operand.Value.GetBool());
            case SsaOp.NotInt: return ConstInt(~operand.Value.GetInt());
            case SsaOp.ConvBoolToInt: return ConstInt(operand.Value.GetBool() ? 1 : 0);
            case SsaOp.ConvByteToInt: return ConstInt(operand.Value.GetInt());
            case SsaOp.ConvIntToDouble: return ConstDouble(operand.Value.GetInt());
            case SsaOp.ConvDoubleToInt: return ConstInt((int)operand.Value.GetDouble());
            case SsaOp.ConvIntToByte: return ConstByte((byte)operand.Value.GetInt());
            case SsaOp.ConvIntToUInt: return ConstUInt(unchecked((uint)operand.Value.GetInt()));
            case SsaOp.ConvIntToUInt64: return ConstUInt64(unchecked((ulong)operand.Value.GetInt()));
            case SsaOp.ConvUIntToUInt64: return ConstUInt64(unchecked((ulong)operand.Value.GetUInt()));
            case SsaOp.ConvUInt64ToPtr: return ConstPtr(unchecked((long)operand.Value.GetUInt64()));
            case SsaOp.ConvPtrToInt: return ConstInt(unchecked((int)operand.Value.GetPtr()));
            case SsaOp.ConvUInt64ToInt: return ConstInt(unchecked((int)operand.Value.GetUInt64()));
            case SsaOp.ConvToInt: return ConstInt(operand.Value.GetInt());
            default: return Bottom();
        }
    }

    // ---- 内部工具 ----

    private static bool EqualPayload(LatticeValue a, LatticeValue b)
    {
        return a.ConstKind switch
        {
            SsaOp.ConstBool => a.Value.GetBool() == b.Value.GetBool(),
            SsaOp.ConstByte => a.Value.GetByte() == b.Value.GetByte(),
            SsaOp.ConstInt => a.Value.GetInt() == b.Value.GetInt(),
            SsaOp.ConstUInt => a.Value.GetUInt() == b.Value.GetUInt(),
            SsaOp.ConstUInt64 => a.Value.GetUInt64() == b.Value.GetUInt64(),
            SsaOp.ConstDouble => a.Value.GetDouble() == b.Value.GetDouble(),
            SsaOp.ConstPtr => a.Value.GetPtr() == b.Value.GetPtr(),
            // ConstString 不在 ConstPayload 中，meet 直接走 Bottom
            _ => false,
        };
    }
}