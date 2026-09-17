using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Script.Ssa;

/// <summary>
/// 将 Bound IR 转换为 SSA IR。
/// 遍历 Bound 的结构化语句，生成扁平的 SsaBlock + SsaValue。
/// </summary>
sealed partial class SsaBuilder
{
    private SsaValue EmitIntrinsic(BoundCallExpression call)
    {
        var fn = call.Function;
        if (fn == BuiltinFunctions.IntConvert)
        {
            var arg = EmitExpression(call.Arguments[0]);
            return EmitIntrinsicInt(arg);
        }
        if (fn == BuiltinFunctions.StrConvert)
        {
            var arg = EmitExpression(call.Arguments[0]);
            if (arg.Type.Equals(ScriptType.String)) return arg;
            return EmitAndAdd(SsaOp.ConvToString, ScriptType.String, arg);
        }
        if (fn == BuiltinFunctions.Length)
        {
            var arg = EmitExpression(call.Arguments[0]);
            return EmitAndAdd(SsaOp.ArrayLen, ScriptType.Int, arg);
        }
        if (fn == BuiltinFunctions.Append)
        {
            var arr = EmitExpression(call.Arguments[0]);
            var val = EmitExpression(call.Arguments[1]);
            return EmitAndAdd(SsaOp.ArrayAppend, call.Type, arr, val);
        }
        if (fn == BuiltinFunctions.Wait)
        {
            var dur = EmitExpression(call.Arguments[0]);
            return EmitAndAdd(SsaOp.Wait, ScriptType.Void, dur);
        }
        if (fn == BuiltinFunctions.Rand)
        {
            var max = EmitExpression(call.Arguments[0]);
            return EmitAndAdd(SsaOp.Rand, ScriptType.Int, max);
        }
        if (fn == BuiltinFunctions.CaptureHole)
        {
            var x = EmitExpression(call.Arguments[0]);
            var y = EmitExpression(call.Arguments[1]);
            var w = EmitExpression(call.Arguments[2]);
            var h = EmitExpression(call.Arguments[3]);
            var v = NewValue(SsaOp.Capture, ScriptType.String, x, y);
            v.ExtraArgs = new List<SsaValue> { w, h };
            AddExtraUses(v.ExtraArgs);
            AddInst(v);
            return v;
        }
        if (fn == BuiltinFunctions.OcrHole)
        {
            var x = EmitExpression(call.Arguments[0]);
            var y = EmitExpression(call.Arguments[1]);
            var w = EmitExpression(call.Arguments[2]);
            var h = EmitExpression(call.Arguments[3]);
            var lang = EmitExpression(call.Arguments[4]);
            var v = NewValue(SsaOp.Ocr, ScriptType.String, x, y);
            v.ExtraArgs = new List<SsaValue> { w, h, lang };
            AddExtraUses(v.ExtraArgs);
            AddInst(v);
            return v;
        }
        if (fn == BuiltinFunctions.RoiHole)
        {
            var image = EmitExpression(call.Arguments[0]);
            var x = EmitExpression(call.Arguments[1]);
            var y = EmitExpression(call.Arguments[2]);
            var w = EmitExpression(call.Arguments[3]);
            var h = EmitExpression(call.Arguments[4]);
            var v = NewValue(SsaOp.Roi, ScriptType.String, image, x);
            v.ExtraArgs = new List<SsaValue> { y, w, h };
            AddExtraUses(v.ExtraArgs);
            AddInst(v);
            return v;
        }
        if (fn == BuiltinFunctions.OcrInitHole)
        {
            var lang = EmitExpression(call.Arguments[0]);
            var dataPath = EmitExpression(call.Arguments[1]);
            var engineMode = EmitExpression(call.Arguments[2]);
            var psmode = EmitExpression(call.Arguments[3]);
            var v = NewValue(SsaOp.OcrInit, ScriptType.Bool, lang, dataPath);
            v.ExtraArgs = new List<SsaValue> { engineMode, psmode };
            AddExtraUses(v.ExtraArgs);
            AddInst(v);
            return v;
        }
        throw new InvalidOperationException($"未知内联函数: {fn.Name}");
    }

    private SsaValue EmitIntrinsicInt(SsaValue arg)
    {
        if (arg.Type.Equals(ScriptType.Int)) return arg;
        if (arg.Type.Equals(ScriptType.Double)) return EmitAndAdd(SsaOp.ConvDoubleToInt, ScriptType.Int, arg);
        if (arg.Type.Equals(ScriptType.Bool)) return EmitAndAdd(SsaOp.ConvBoolToInt, ScriptType.Int, arg);
        if (arg.Type.Equals(ScriptType.Byte)) return EmitAndAdd(SsaOp.ConvByteToInt, ScriptType.Int, arg);
        if (arg.Type.Equals(ScriptType.UInt)) return EmitAndAdd(SsaOp.ConvIntToUInt, ScriptType.Int, arg);
        if (arg.Type.Equals(ScriptType.UInt64)) return EmitAndAdd(SsaOp.ConvUInt64ToInt, ScriptType.Int, arg);
        return EmitAndAdd(SsaOp.ConvToInt, ScriptType.Int, arg);
    }

    private static SsaOp MapBinaryOp(BoundBinaryOperator op) => op.Kind switch
    {
        BoundBinaryOperatorKind.Addition when op.LeftType.Equals(ScriptType.Int) => SsaOp.AddInt,
        BoundBinaryOperatorKind.Addition when op.LeftType.Equals(ScriptType.UInt) => SsaOp.AddUInt,
        BoundBinaryOperatorKind.Addition when op.LeftType.Equals(ScriptType.Double) => SsaOp.AddDouble,
        BoundBinaryOperatorKind.Addition when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.AddUInt64,
        BoundBinaryOperatorKind.Addition when op.LeftType.Equals(ScriptType.Byte) => SsaOp.AddInt,

        BoundBinaryOperatorKind.Subtraction when op.LeftType.Equals(ScriptType.Int) => SsaOp.SubInt,
        BoundBinaryOperatorKind.Subtraction when op.LeftType.Equals(ScriptType.UInt) => SsaOp.SubUInt,
        BoundBinaryOperatorKind.Subtraction when op.LeftType.Equals(ScriptType.Double) => SsaOp.SubDouble,
        BoundBinaryOperatorKind.Subtraction when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.SubUInt64,
        BoundBinaryOperatorKind.Subtraction when op.LeftType.Equals(ScriptType.Byte) => SsaOp.SubInt,

        BoundBinaryOperatorKind.Multiplication when op.LeftType.Equals(ScriptType.Int) => SsaOp.MulInt,
        BoundBinaryOperatorKind.Multiplication when op.LeftType.Equals(ScriptType.UInt) => SsaOp.MulUInt,
        BoundBinaryOperatorKind.Multiplication when op.LeftType.Equals(ScriptType.Double) => SsaOp.MulDouble,
        BoundBinaryOperatorKind.Multiplication when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.MulUInt64,
        BoundBinaryOperatorKind.Multiplication when op.LeftType.Equals(ScriptType.Byte) => SsaOp.MulInt,

        BoundBinaryOperatorKind.Division when op.LeftType.Equals(ScriptType.Int) => SsaOp.DivInt,
        BoundBinaryOperatorKind.Division when op.LeftType.Equals(ScriptType.UInt) => SsaOp.DivUInt,
        BoundBinaryOperatorKind.Division when op.LeftType.Equals(ScriptType.Double) => SsaOp.DivDouble,
        BoundBinaryOperatorKind.Division when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.DivUInt64,
        BoundBinaryOperatorKind.Division when op.LeftType.Equals(ScriptType.Byte) => SsaOp.DivInt,

        BoundBinaryOperatorKind.Mod when op.LeftType.Equals(ScriptType.Int) => SsaOp.ModInt,
        BoundBinaryOperatorKind.Mod when op.LeftType.Equals(ScriptType.UInt) => SsaOp.ModUInt,
        BoundBinaryOperatorKind.Mod when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.ModUInt64,
        BoundBinaryOperatorKind.Mod when op.LeftType.Equals(ScriptType.Byte) => SsaOp.ModInt,

        BoundBinaryOperatorKind.RoundDiv when op.LeftType.Equals(ScriptType.Int) => SsaOp.RoundDivInt,
        BoundBinaryOperatorKind.RoundDiv when op.LeftType.Equals(ScriptType.Byte) => SsaOp.RoundDivInt,

        BoundBinaryOperatorKind.BitwiseAnd when op.LeftType.Equals(ScriptType.Int) => SsaOp.AndInt,
        BoundBinaryOperatorKind.BitwiseAnd when op.LeftType.Equals(ScriptType.UInt) => SsaOp.AndInt,
        BoundBinaryOperatorKind.BitwiseAnd when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.AndInt,
        BoundBinaryOperatorKind.BitwiseAnd when op.LeftType.Equals(ScriptType.Byte) => SsaOp.AndInt,

        BoundBinaryOperatorKind.BitwiseOr when op.LeftType.Equals(ScriptType.Int) => SsaOp.OrInt,
        BoundBinaryOperatorKind.BitwiseOr when op.LeftType.Equals(ScriptType.UInt) => SsaOp.OrInt,
        BoundBinaryOperatorKind.BitwiseOr when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.OrInt,
        BoundBinaryOperatorKind.BitwiseOr when op.LeftType.Equals(ScriptType.Byte) => SsaOp.OrInt,

        BoundBinaryOperatorKind.BitwiseXor when op.LeftType.Equals(ScriptType.Int) => SsaOp.XorInt,
        BoundBinaryOperatorKind.BitwiseXor when op.LeftType.Equals(ScriptType.UInt) => SsaOp.XorInt,
        BoundBinaryOperatorKind.BitwiseXor when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.XorInt,
        BoundBinaryOperatorKind.BitwiseXor when op.LeftType.Equals(ScriptType.Byte) => SsaOp.XorInt,

        BoundBinaryOperatorKind.BitLeftShift when op.LeftType.Equals(ScriptType.Int) => SsaOp.ShlInt,
        BoundBinaryOperatorKind.BitLeftShift when op.LeftType.Equals(ScriptType.UInt) => SsaOp.ShlInt,
        BoundBinaryOperatorKind.BitLeftShift when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.ShlInt,
        BoundBinaryOperatorKind.BitLeftShift when op.LeftType.Equals(ScriptType.Byte) => SsaOp.ShlInt,

        BoundBinaryOperatorKind.BitRightShift when op.LeftType.Equals(ScriptType.Int) => SsaOp.ShrInt,
        BoundBinaryOperatorKind.BitRightShift when op.LeftType.Equals(ScriptType.UInt) => SsaOp.ShrInt,
        BoundBinaryOperatorKind.BitRightShift when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.ShrInt,
        BoundBinaryOperatorKind.BitRightShift when op.LeftType.Equals(ScriptType.Byte) => SsaOp.ShrInt,

        // 比较
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.Int) => SsaOp.EqInt,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.UInt) => SsaOp.EqUInt,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.Double) => SsaOp.EqDouble,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.EqUInt64,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.Bool) => SsaOp.EqBool,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.String) => SsaOp.EqString,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.Ptr) => SsaOp.EqPtr,
        BoundBinaryOperatorKind.Equals when op.LeftType.Equals(ScriptType.Byte) => SsaOp.EqByte,

        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.Int) => SsaOp.NeqInt,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.UInt) => SsaOp.NeqUInt,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.Double) => SsaOp.NeqDouble,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.NeqUInt64,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.Bool) => SsaOp.NeqBool,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.String) => SsaOp.NeqString,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.Ptr) => SsaOp.NeqPtr,
        BoundBinaryOperatorKind.NotEquals when op.LeftType.Equals(ScriptType.Byte) => SsaOp.NeqByte,

        BoundBinaryOperatorKind.Less when op.LeftType.Equals(ScriptType.Int) => SsaOp.LtInt,
        BoundBinaryOperatorKind.Less when op.LeftType.Equals(ScriptType.UInt) => SsaOp.LtUInt,
        BoundBinaryOperatorKind.Less when op.LeftType.Equals(ScriptType.Double) => SsaOp.LtDouble,
        BoundBinaryOperatorKind.Less when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.LtUInt64,
        BoundBinaryOperatorKind.Less when op.LeftType.Equals(ScriptType.Byte) => SsaOp.LtByte,

        BoundBinaryOperatorKind.LessOrEquals when op.LeftType.Equals(ScriptType.Int) => SsaOp.LeqInt,
        BoundBinaryOperatorKind.LessOrEquals when op.LeftType.Equals(ScriptType.UInt) => SsaOp.LeqUInt,
        BoundBinaryOperatorKind.LessOrEquals when op.LeftType.Equals(ScriptType.Double) => SsaOp.LeqDouble,
        BoundBinaryOperatorKind.LessOrEquals when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.LeqUInt64,
        BoundBinaryOperatorKind.LessOrEquals when op.LeftType.Equals(ScriptType.Byte) => SsaOp.LeqByte,

        BoundBinaryOperatorKind.Greater when op.LeftType.Equals(ScriptType.Int) => SsaOp.GtInt,
        BoundBinaryOperatorKind.Greater when op.LeftType.Equals(ScriptType.UInt) => SsaOp.GtUInt,
        BoundBinaryOperatorKind.Greater when op.LeftType.Equals(ScriptType.Double) => SsaOp.GtDouble,
        BoundBinaryOperatorKind.Greater when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.GtUInt64,
        BoundBinaryOperatorKind.Greater when op.LeftType.Equals(ScriptType.Byte) => SsaOp.GtByte,

        BoundBinaryOperatorKind.GreaterOrEquals when op.LeftType.Equals(ScriptType.Int) => SsaOp.GeqInt,
        BoundBinaryOperatorKind.GreaterOrEquals when op.LeftType.Equals(ScriptType.UInt) => SsaOp.GeqUInt,
        BoundBinaryOperatorKind.GreaterOrEquals when op.LeftType.Equals(ScriptType.Double) => SsaOp.GeqDouble,
        BoundBinaryOperatorKind.GreaterOrEquals when op.LeftType.Equals(ScriptType.UInt64) => SsaOp.GeqUInt64,
        BoundBinaryOperatorKind.GreaterOrEquals when op.LeftType.Equals(ScriptType.Byte) => SsaOp.GeqByte,

        BoundBinaryOperatorKind.In => SsaOp.Contains,

        _ => throw new InvalidOperationException(
            $"未映射的二元运算: {op.Kind} + {op.LeftType}")
    };

    private static SsaOp MapConversion(ScriptType from, ScriptType to) => (from, to) switch
    {
        var (f, t) when f.Equals(t) => SsaOp.Nop,

        _ when to.Equals(ScriptType.Int) && from.Equals(ScriptType.Bool) => SsaOp.ConvBoolToInt,
        _ when to.Equals(ScriptType.Int) && from.Equals(ScriptType.Byte) => SsaOp.ConvByteToInt,
        _ when to.Equals(ScriptType.UInt) && from.Equals(ScriptType.Int) => SsaOp.ConvIntToUInt,
        _ when to.Equals(ScriptType.UInt64) && from.Equals(ScriptType.Int) => SsaOp.ConvIntToUInt64,
        _ when to.Equals(ScriptType.UInt64) && from.Equals(ScriptType.UInt) => SsaOp.ConvUIntToUInt64,
        _ when to.Equals(ScriptType.Double) && from.Equals(ScriptType.Int) => SsaOp.ConvIntToDouble,
        _ when to.Equals(ScriptType.Byte) && from.Equals(ScriptType.Int) => SsaOp.ConvIntToByte,
        _ when to.Equals(ScriptType.String) => SsaOp.ConvToString,
        _ when to.Equals(ScriptType.Ptr) && from.Equals(ScriptType.UInt64) => SsaOp.ConvUInt64ToPtr,
        _ when to.Equals(ScriptType.Ptr) && from.Equals(ScriptType.Int) => SsaOp.ConvIntToPtr,
        _ when to.Equals(ScriptType.Int) && from.Equals(ScriptType.Ptr) => SsaOp.ConvPtrToInt,
        _ when to.Equals(ScriptType.Int) && from.Equals(ScriptType.Double) => SsaOp.ConvDoubleToInt,
        _ when to.Equals(ScriptType.UInt64) && from.Equals(ScriptType.UInt64) => SsaOp.Nop,
        _ when to.Equals(ScriptType.Int) && from.Equals(ScriptType.UInt64) => SsaOp.ConvUInt64ToInt,
        _ when to.Equals(ScriptType.UInt) && from.Equals(ScriptType.UInt) => SsaOp.Nop,

        _ => throw new InvalidOperationException($"未映射的转换: {from} → {to}")
    };
}