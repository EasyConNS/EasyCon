using EasyCon.Script.Runtime;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EasyCon.Script.Bytecode;

public static partial class BytecodeEncoder
{
    sealed partial class Encoder
    {
        // ---- 指令选择（docs/VM2.md §5.2 映射表）----

        void EmitInst(SsaValue v)
        {
            switch (v.Op)
            {
                // ---- 局部/全局 ----
                case SsaOp.LoadLocal:
                    EmitIabc(EcsOpcode.Move, Slot(v), SymSlot((LocalVariableSymbol)v.Aux!), 0);
                    break;
                case SsaOp.StoreLocal:
                    EmitIabc(EcsOpcode.SetVar, SymSlot((LocalVariableSymbol)v.Aux!), Slot(v.Arg0!), 0);
                    break;
                case SsaOp.LoadGlobal:
                    EmitAbx(EcsOpcode.LoadG, Slot(v), GlobalSlot((GlobalVariableSymbol)v.Aux!));
                    break;
                case SsaOp.StoreGlobal:
                    EmitAbx(EcsOpcode.StoreG, Slot(v.Arg0!), GlobalSlot((GlobalVariableSymbol)v.Aux!));
                    break;

                // ---- 算术 ----
                case SsaOp.AddInt: EmitIabc(EcsOpcode.AddI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.SubInt: EmitIabc(EcsOpcode.SubI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.MulInt: EmitIabc(EcsOpcode.MulI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.DivInt: EmitIabc(EcsOpcode.DivI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.ModInt: EmitIabc(EcsOpcode.ModI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.RoundDivInt: EmitIabc(EcsOpcode.RDivI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;

                case SsaOp.AddUInt: EmitIabc(EcsOpcode.AddU, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.SubUInt: EmitIabc(EcsOpcode.SubU, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.MulUInt: EmitIabc(EcsOpcode.MulU, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.DivUInt: EmitIabc(EcsOpcode.DivU, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.ModUInt: EmitIabc(EcsOpcode.ModU, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;

                case SsaOp.AddUInt64: EmitIabc(EcsOpcode.AddL, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.SubUInt64: EmitIabc(EcsOpcode.SubL, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.MulUInt64: EmitIabc(EcsOpcode.MulL, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.DivUInt64: EmitIabc(EcsOpcode.DivL, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.ModUInt64: EmitIabc(EcsOpcode.ModL, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;

                case SsaOp.AddDouble: EmitIabc(EcsOpcode.AddD, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.SubDouble: EmitIabc(EcsOpcode.SubD, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.MulDouble: EmitIabc(EcsOpcode.MulD, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.DivDouble: EmitIabc(EcsOpcode.DivD, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;

                // ---- 位运算 ----
                case SsaOp.AndInt: EmitIabc(EcsOpcode.BandI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.OrInt: EmitIabc(EcsOpcode.BorI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.XorInt: EmitIabc(EcsOpcode.BxorI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.ShlInt: EmitIabc(EcsOpcode.ShlI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.ShrInt: EmitIabc(EcsOpcode.ShrI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.NotInt: EmitIabc(EcsOpcode.BnotI, Slot(v), Slot(v.Arg0!), 0); break;

                // ---- 比较 ----
                case SsaOp.EqInt: EmitCmp(EcsOpcode.EqI, v); break;
                case SsaOp.LtInt: EmitCmp(EcsOpcode.LtI, v); break;
                case SsaOp.LeqInt: EmitCmp(EcsOpcode.LeI, v); break;
                case SsaOp.GtInt: EmitCmp(EcsOpcode.GtI, v); break;
                case SsaOp.GeqInt: EmitCmp(EcsOpcode.GeI, v); break;
                case SsaOp.EqUInt: EmitCmp(EcsOpcode.EqU, v); break;
                case SsaOp.LtUInt: EmitCmp(EcsOpcode.LtU, v); break;
                case SsaOp.LeqUInt: EmitCmp(EcsOpcode.LeU, v); break;
                case SsaOp.GtUInt: EmitCmp(EcsOpcode.GtU, v); break;
                case SsaOp.GeqUInt: EmitCmp(EcsOpcode.GeU, v); break;
                case SsaOp.EqDouble: EmitCmp(EcsOpcode.EqD, v); break;
                case SsaOp.LtDouble: EmitCmp(EcsOpcode.LtD, v); break;
                case SsaOp.LeqDouble: EmitCmp(EcsOpcode.LeD, v); break;
                case SsaOp.GtDouble: EmitCmp(EcsOpcode.GtD, v); break;
                case SsaOp.GeqDouble: EmitCmp(EcsOpcode.GeD, v); break;
                case SsaOp.EqUInt64: EmitCmp(EcsOpcode.EqL, v); break;
                case SsaOp.LtUInt64: EmitCmp(EcsOpcode.LtL, v); break;
                case SsaOp.LeqUInt64: EmitCmp(EcsOpcode.LeL, v); break;
                case SsaOp.GtUInt64: EmitCmp(EcsOpcode.GtL, v); break;
                case SsaOp.GeqUInt64: EmitCmp(EcsOpcode.GeL, v); break;
                case SsaOp.EqString: EmitCmp(EcsOpcode.EqS, v); break;
                case SsaOp.EqPtr: EmitCmp(EcsOpcode.EqP, v); break;
                case SsaOp.EqBool: EmitCmp(EcsOpcode.EqI, v); break;
                case SsaOp.EqByte: EmitCmp(EcsOpcode.EqI, v); break;
                case SsaOp.LtByte: EmitCmp(EcsOpcode.LtI, v); break;
                case SsaOp.LeqByte: EmitCmp(EcsOpcode.LeI, v); break;
                case SsaOp.GtByte: EmitCmp(EcsOpcode.GtI, v); break;
                case SsaOp.GeqByte: EmitCmp(EcsOpcode.GeI, v); break;

                case SsaOp.NeqInt: EmitNeq(EcsOpcode.EqI, v); break;
                case SsaOp.NeqUInt: EmitNeq(EcsOpcode.EqU, v); break;
                case SsaOp.NeqDouble: EmitNeq(EcsOpcode.EqD, v); break;
                case SsaOp.NeqUInt64: EmitNeq(EcsOpcode.EqL, v); break;
                case SsaOp.NeqString: EmitNeq(EcsOpcode.EqS, v); break;
                case SsaOp.NeqPtr: EmitNeq(EcsOpcode.EqP, v); break;
                case SsaOp.NeqBool: EmitNeq(EcsOpcode.EqI, v); break;
                case SsaOp.NeqByte: EmitNeq(EcsOpcode.EqI, v); break;

                // ---- 一元/转换 ----
                case SsaOp.LogicNot: EmitIabc(EcsOpcode.Not, Slot(v), Slot(v.Arg0!), 0); break;
                case SsaOp.ConvBoolToInt: EmitIabc(EcsOpcode.Move, Slot(v), Slot(v.Arg0!), 0); break;
                case SsaOp.ConvByteToInt: EmitIabc(EcsOpcode.Move, Slot(v), Slot(v.Arg0!), 0); break;
                case SsaOp.ConvIntToUInt: EmitConv(EcsConvKind.IntToUInt, v); break;
                case SsaOp.ConvIntToUInt64: EmitConv(EcsConvKind.IntToUInt64, v); break;
                case SsaOp.ConvIntToDouble: EmitConv(EcsConvKind.IntToDouble, v); break;
                case SsaOp.ConvIntToByte: EmitConv(EcsConvKind.IntToByte, v); break;
                case SsaOp.ConvUIntToUInt64: EmitConv(EcsConvKind.UIntToUInt64, v); break;
                case SsaOp.ConvUInt64ToPtr: EmitConv(EcsConvKind.UInt64ToPtr, v); break;
                case SsaOp.ConvPtrToInt: EmitConv(EcsConvKind.PtrToInt, v); break;
                case SsaOp.ConvIntToPtr: EmitConv(EcsConvKind.IntToPtr, v); break;
                case SsaOp.ConvDoubleToInt: EmitConv(EcsConvKind.DoubleToInt, v); break;
                case SsaOp.ConvUInt64ToInt: EmitConv(EcsConvKind.UInt64ToInt, v); break;
                case SsaOp.ConvToString: EmitConv(EcsConvKind.ToStr, v); break;
                case SsaOp.ConvToInt: EmitConv(EcsConvKind.ToInt, v); break;

                // ---- 复合数据 ----
                case SsaOp.ArrayInit: EmitArrayInit(v); break;
                case SsaOp.LoadIndex: EmitIabc(EcsOpcode.GetI, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.StoreIndex: EmitIabc(EcsOpcode.SetI, Slot(v.ExtraArgs![0]), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.Slice:
                    {
                        bool hasEnd = v.ExtraArgs != null && v.ExtraArgs.Count > 0
                            && v.ExtraArgs[0].Type.Equals(ScriptType.Int);
                        EmitExt(EcsOpcode.Slice, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!),
                            hasEnd ? (uint)Slot(v.ExtraArgs![0]) : 0xFFFFFFFFu);
                        break;
                    }
                case SsaOp.Contains: EmitIabc(EcsOpcode.Cont, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.ArrayAppend: EmitIabc(EcsOpcode.Append, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.Concat: EmitIabc(EcsOpcode.Cat, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!)); break;
                case SsaOp.ArrayLen: EmitIabc(EcsOpcode.Len, Slot(v), Slot(v.Arg0!), 0); break;
                case SsaOp.DeepCopy: EmitIabc(EcsOpcode.SetVar, Slot(v), Slot(v.Arg0!), 0); break;

                // ---- 结构体 ----
                case SsaOp.StructInit:
                    EmitAbx(EcsOpcode.NewSt, Slot(v), StructSid(v.Type));
                    break;
                case SsaOp.LoadField:
                    EmitIabc(EcsOpcode.GetF, Slot(v), Slot(v.Arg0!), FieldIndex(v));
                    break;
                case SsaOp.StoreField:
                    EmitIabc(EcsOpcode.PutF, Slot(v.Arg1!), Slot(v.Arg0!), FieldIndex(v));
                    break;
                case SsaOp.LoadFieldIndex:
                    EmitExt(EcsOpcode.GetFI, Slot(v), Slot(v.Arg0!), FieldIndex(v), (uint)Slot(v.Arg1!));
                    break;
                case SsaOp.StoreFieldIndex:
                    EmitExt(EcsOpcode.PutFI, Slot(v.ExtraArgs![0]), Slot(v.Arg0!), FieldIndex(v), (uint)Slot(v.Arg1!));
                    break;

                // ---- 域操作 ----
                case SsaOp.KeyPress:
                    {
                        var key = KeyByte(v);
                        var dur = v.Arg0!;
                        if (dur.IsConstant && dur.Const.GetInt() is >= 0 and <= 0xFFFF)
                            EmitAbx(EcsOpcode.KeyI, key, dur.Const.GetInt());
                        else
                            EmitIabc(EcsOpcode.KeyV, key, Slot(dur), 0);
                        break;
                    }
                case SsaOp.KeyAction:
                    {
                        var key = KeyByte(v);
                        bool release = v.Const.GetBool();
                        EmitIabc(EcsOpcode.KeySt, key, release ? 0 : 1, 0);
                        break;
                    }
                case SsaOp.StickAction:
                    {
                        UnpackStick(v, out var side, out var x, out var y);
                        EmitIabc(EcsOpcode.StickSet, side, x, y);
                        break;
                    }
                case SsaOp.StickPress:
                    {
                        UnpackStick(v, out var side, out var x, out var y);
                        var dur = v.Arg0!;
                        if (dur.IsConstant)
                            EmitExt(EcsOpcode.StickP, side, x, y, unchecked((uint)dur.Const.GetInt()));
                        else
                            EmitExt(EcsOpcode.StickPv, side, 0, Slot(dur), (uint)(x | (y << 16)));
                        break;
                    }
                case SsaOp.Wait:
                    {
                        var dur = v.Arg0!;
                        if (dur.IsConstant && dur.Const.GetInt() is >= 0 and <= 0xFFFF)
                            EmitAbx(EcsOpcode.WaitI, 0, dur.Const.GetInt());
                        else
                            EmitIabc(EcsOpcode.WaitV, Slot(dur), 0, 0);
                        break;
                    }
                case SsaOp.Rand:
                    EmitIabc(EcsOpcode.Rand, Slot(v), Slot(v.Arg0!), 0);
                    break;
                case SsaOp.ImageLabel:
                    {
                        var name = ((RuntimeValueNameSymbol)v.Aux!).Name;
                        EmitAbx(EcsOpcode.Img, Slot(v), _pool.AddString(name));
                        break;
                    }
                case SsaOp.RuntimeValue:
                    {
                        var name = ((RuntimeValueNameSymbol)v.Aux!).Name;
                        if (name == Syntax.RuntimeValues.Time)
                            EmitStagedCall(EcsOpcode.CallN, unchecked((int)(EcsSyscall.CallFlag | (uint)EcsSyscall.Time)), Array.Empty<SsaValue>(), Slot(v));
                        else if (name == Syntax.RuntimeValues.App)
                            EmitStagedCall(EcsOpcode.CallN, unchecked((int)(EcsSyscall.CallFlag | (uint)EcsSyscall.App)), Array.Empty<SsaValue>(), Slot(v));
                        else
                            throw Fail($"不支持的运行时值 {name}");
                        break;
                    }
                case SsaOp.Capture: EmitHoleCall(v, "__CAPTURE__"); break;
                case SsaOp.Ocr: EmitHoleCall(v, "__OCR__"); break;
                case SsaOp.Roi: EmitHoleCall(v, "__ROI__"); break;
                case SsaOp.OcrInit: EmitHoleCall(v, "__OCR_INIT__"); break;

                // ---- 调用 ----
                case SsaOp.Call:
                case SsaOp.StaticCall:
                    EmitCallInst(v);
                    break;

                default:
                    throw Fail($"未映射的 SsaOp: {v.Op}");
            }
        }

        void EmitCmp(EcsOpcode op, SsaValue v)
            => EmitIabc(op, Slot(v), Slot(v.Arg0!), Slot(v.Arg1!));

        void EmitNeq(EcsOpcode eqOp, SsaValue v)
        {
            var tmp = _neqTemp;
            EmitIabc(eqOp, tmp, Slot(v.Arg0!), Slot(v.Arg1!));
            EmitIabc(EcsOpcode.Not, Slot(v), tmp, 0);
        }

        void EmitConv(EcsConvKind kind, SsaValue v)
            => EmitIabc(EcsOpcode.Conv, Slot(v), Slot(v.Arg0!), (int)kind);

        void EmitArrayInit(SsaValue v)
        {
            var elemCode = TypeCode(((ArrayType)v.Type).ElementType);
            if (v.Arg0 == null)
            {
                EmitAbx(EcsOpcode.NewArrE, Slot(v), elemCode);
                return;
            }

            var args = new List<SsaValue> { v.Arg0 };
            if (v.ExtraArgs != null) args.AddRange(v.ExtraArgs);
            for (int i = 0; i < args.Count; i++)
                EmitIabc(EcsOpcode.Move, _stagingBase + i, Slot(args[i]), 0);
            EmitExt(EcsOpcode.NewArrV, Slot(v), args.Count, _stagingBase, elemCode);
        }

        void EmitCallInst(SsaValue v)
        {
            var sym = (FunctionSymbol)v.Aux!;
            var args = CallArgs(v);
            bool hasResult = !sym.ReturnType.Equals(ScriptType.Void) && v.Uses > 0;

            // 用户函数（含 stdlib）：值相等语义与 SsaEvaluator 一致（A-07）。
            // 本模块函数 → 模块局部 fid；跨模块 → 导入表标记 0x80000000|importIdx（docs/EcmEcxFormat.md §4.1）。
            // extern 声明恒走 CallN 按名分发（镜像里没有 extern 的函数体，导入标记将无法解析）；
            // 其余（内建原生等）→ CallN 按名分发。
            bool isLocal = _ctx.LocalFuncIds.TryGetValue(sym, out var localFid);
            bool isExtern = !string.Equals(sym.LibraryName, "internal", StringComparison.Ordinal);
            bool isExternal = !isLocal && !isExtern && (
                _ctx.Program.Functions.ContainsKey(sym)
                || (_ctx.ExternalFunctions != null && _ctx.ExternalFunctions.Contains(sym)));
            if (isLocal || isExternal)
            {
                for (int i = 0; i < args.Count; i++)
                    EmitIabc(EcsOpcode.Move, _stagingBase + i, Slot(args[i]), 0);

                uint target = isLocal
                    ? unchecked((uint)localFid)
                    : 0x80000000u | unchecked((uint)_ctx.ImportId(sym.Name, sym.Parameters.Length));
                EmitExt(EcsOpcode.Call, _stagingBase, args.Count,
                    hasResult ? _receiveSlot : 255, target);

                if (hasResult)
                    EmitIabc(EcsOpcode.Move, Slot(v), _receiveSlot, 0);
            }
            else
            {
                // 文件族 → syscall 编号调用（EXT 旗标 0x80000000|编号，不进原生名表，docs/VM2.md §9.1）；
                // 其余内建/采集洞 → CallN 按名分发。
                var targetId = EcsSyscall.TryGetTarget(NativeName(sym), out var scTarget)
                    ? unchecked((int)scTarget)
                    : _ctx.NativeId(NativeName(sym));
                EmitStagedCall(EcsOpcode.CallN, targetId, args,
                    hasResult ? Slot(v) : -1);
            }
        }

        void EmitStagedCall(EcsOpcode op, int targetId, IReadOnlyList<SsaValue> args, int resultSlot)
        {
            for (int i = 0; i < args.Count; i++)
                EmitIabc(EcsOpcode.Move, _stagingBase + i, Slot(args[i]), 0);
            EmitExt(op, _stagingBase, args.Count,
                resultSlot >= 0 ? resultSlot : 255, unchecked((uint)targetId));
        }

        void EmitHoleCall(SsaValue v, string native)
        {
            var args = CallArgs(v);
            EmitStagedCall(EcsOpcode.CallN, _ctx.NativeId(native), args, Slot(v));
        }

        static IReadOnlyList<SsaValue> CallArgs(SsaValue v)
        {
            // 洞调用（Capture/Ocr/Roi/OcrInit）的 SSA 形态是 Arg0+Arg1+ExtraArgs，
            // 普通调用是 Arg0+ExtraArgs（Arg1 恒空）——两者都必须完整打包
            var args = new List<SsaValue>();
            if (v.Arg0 != null) args.Add(v.Arg0);
            if (v.Arg1 != null) args.Add(v.Arg1);
            if (v.ExtraArgs != null) args.AddRange(v.ExtraArgs);
            return args;
        }

        static string NativeName(FunctionSymbol sym) => BuildNativeName(sym);

        static byte KeyByte(SsaValue v)
        {
            var key = ((GamePadKeySymbol)v.Aux!).Key;
            if ((uint)key > 255)
                throw new BytecodeException(new[] { new BytecodeDiagnostic($"按键码越界: {key}", null, 0) });
            return (byte)key;
        }

        static void UnpackStick(SsaValue v, out int side, out int x, out int y)
        {
            var key = ((GamePadKeySymbol)v.Aux!).Key;
            side = key == GamePadKey.RS ? 1 : 0;
            int packed = v.Const.GetInt();
            x = (packed >> 16) & 0xFF;
            y = (packed >> 8) & 0xFF;
        }

        int GlobalSlot(GlobalVariableSymbol sym) => _ctx.GlobalSlot(sym);

        int StructSid(ScriptType type)
        {
            var name = ((StructType)type).Definition.Name;
            if (_ctx.StructIds.TryGetValue(name, out var sid))
                return sid;
            throw Fail($"结构体 {name} 不在类型表（内部错误）");
        }

        int FieldIndex(SsaValue v)
        {
            var def = ((StructType)v.Arg0!.Type).Definition;
            int idx = def.Fields.IndexOf((EcsFieldDef)v.Aux!);
            if (idx < 0)
                throw Fail("字段不在结构体定义中（内部错误）");
            return idx;
        }

        static byte TypeCode(ScriptType type)
        {
            if (type.Equals(ScriptType.Bool)) return (byte)EcsTypeCode.Bool;
            if (type.Equals(ScriptType.Byte)) return (byte)EcsTypeCode.Byte;
            if (type.Equals(ScriptType.Int)) return (byte)EcsTypeCode.Int;
            if (type.Equals(ScriptType.UInt)) return (byte)EcsTypeCode.UInt;
            if (type.Equals(ScriptType.UInt64)) return (byte)EcsTypeCode.UInt64;
            if (type.Equals(ScriptType.Double)) return (byte)EcsTypeCode.Double;
            if (type.Equals(ScriptType.String)) return (byte)EcsTypeCode.String;
            if (type.Equals(ScriptType.Ptr)) return (byte)EcsTypeCode.Ptr;
            if (type is StructType) return (byte)EcsTypeCode.Struct;
            return (byte)EcsTypeCode.Any;
        }

    }
}