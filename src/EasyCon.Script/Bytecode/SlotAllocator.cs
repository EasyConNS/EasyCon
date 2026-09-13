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
        /// <summary>块内使用信息：常量按使用块块首物化；UseCount 含终结符/phi 臂读取。</summary>
        sealed class BlockUseInfo
        {
            public readonly List<SsaValue> Consts = new();
            public readonly Dictionary<SsaValue, int> UseCount = new();
        }

        BlockUseInfo Info(SsaBlock b)
        {
            if (!_useInfo.TryGetValue(b, out var info))
                _useInfo[b] = info = new BlockUseInfo();
            return info;
        }

        static bool NeedsSlot(SsaOp op) => op switch
        {
            SsaOp.StoreLocal or SsaOp.StoreGlobal or SsaOp.StoreField or SsaOp.StoreIndex
                or SsaOp.StoreFieldIndex or SsaOp.Wait or SsaOp.KeyPress or SsaOp.KeyAction
                or SsaOp.StickAction or SsaOp.StickPress or SsaOp.Return
                or SsaOp.CondBranch or SsaOp.Branch or SsaOp.Nop => false,
            _ => true,
        };

        /// <summary>
        /// 槽位分配（docs/VM2.md §4.3）：跨块/块内分类 + 块内槽池 + 常量块首惰性物化。
        /// phi 结果恒跨块（写入发生在前驱边的并行副本）；其余值当且仅当全部读取都在定义块内池化；
        /// 常量是纯值，一律池化并按使用块块首物化（零使用的死常量不物化）。
        /// 活性依据：块内直线 defs/uses 全序，「定义分配 + 末次读取归还」即精确活性；
        /// phi 臂读取按臂↔前驱对齐计入对应前驱块（副本在前驱终结符发射）。
        /// </summary>
        void AssignSlots()
        {
            _next = _fn.Layout.SlotCount;

            // 1. 使用点收集：操作数/分支条件记在使用块；phi 臂记在对应前驱
            foreach (var block in _fn.Blocks)
            {
                foreach (var phi in block.Phis)
                {
                    if (phi.ExtraArgs == null) continue;
                    for (int i = 0; i < phi.ExtraArgs.Count; i++)
                    {
                        // 臂 i 在前驱 Predecessors[i] 的终结符被并行副本读取（A-01 对齐不变量）
                        var reader = i < block.Predecessors.Count ? block.Predecessors[i] : block;
                        RecordRead(phi.ExtraArgs[i], reader);
                    }
                }
                foreach (var inst in block.Instructions)
                {
                    if (inst.Arg0 != null) RecordOperand(inst, inst.Arg0, block);
                    if (inst.Arg1 != null) RecordOperand(inst, inst.Arg1, block);
                    if (inst.ExtraArgs != null)
                        foreach (var a in inst.ExtraArgs)
                            RecordRead(a, block);
                }
                if (block.BranchCondition != null)
                    RecordRead(block.BranchCondition, block);
            }

            // 2. 分类：跨块值专用槽；块内值登记池化；常量已按块登记
            bool hasPhi = false, hasNeq = false;
            int maxArity = 1;
            foreach (var block in _fn.Blocks)
            {
                var info = Info(block);
                foreach (var phi in block.Phis)
                {
                    _slots[phi] = _next++;
                    hasPhi = true;
                }
                foreach (var inst in block.Instructions)
                {
                    if (inst.Op is SsaOp.NeqInt or SsaOp.NeqUInt or SsaOp.NeqDouble or SsaOp.NeqUInt64
                        or SsaOp.NeqBool or SsaOp.NeqByte or SsaOp.NeqString or SsaOp.NeqPtr)
                        hasNeq = true;
                    int arity = ArityOf(inst);
                    if (arity > maxArity) maxArity = arity;

                    if (inst.IsConstant) { _pooled.Add(inst); continue; }   // 常量池化：Consts 已在步骤 1 登记
                    if (!NeedsSlot(inst.Op)) continue;

                    int readsHere = info.UseCount.TryGetValue(inst, out var c) ? c : 0;
                    int totalReads = _totalReads.TryGetValue(inst, out var t) ? t : 0;
                    if (totalReads > readsHere)
                        _slots[inst] = _next++;         // 跨块：专用槽
                    else
                    {
                        _pooled.Add(inst);              // 块内：定义时入池
                    }
                }
            }

            if (hasNeq) _neqTemp = _next++;
            if (hasPhi) _scratch = _next++;
            _stagingBase = _next;
            _receiveSlot = _next + maxArity;
            _next += maxArity + 1;
            _maxArity = maxArity;

            // 池区在所有保留槽之后生长
            _poolNext = _next;
        }

        /// <summary>
        /// 该操作数是否被指令以立即数形式消费（KeyI/WaitI/StickP 的常量时长）。
        /// 立即数消费不读槽：不计使用（不触发块首物化，也无需归还）。
        /// </summary>
        static bool IsImmediateOperand(SsaValue inst, SsaValue operand)
        {
            if (!operand.IsConstant || operand != inst.Arg0)
                return false;
            return inst.Op switch
            {
                SsaOp.KeyPress or SsaOp.Wait
                    => operand.Const.GetInt() is >= 0 and <= 0xFFFF,
                SsaOp.StickPress => true,
                _ => false,
            };
        }

        void RecordRead(SsaValue v, SsaBlock reader)
        {
            _totalReads[v] = _totalReads.TryGetValue(v, out var t) ? t + 1 : 1;
            var info = Info(reader);
            if (!info.UseCount.TryGetValue(v, out var c))
            {
                info.UseCount[v] = 1;
                if (v.IsConstant)
                    info.Consts.Add(v);
            }
            else
                info.UseCount[v] = c + 1;
        }

        void RecordOperand(SsaValue inst, SsaValue operand, SsaBlock reader)
        {
            if (IsImmediateOperand(inst, operand))
                return;
            RecordRead(operand, reader);
        }

        // ---- 块内槽池 ----

        int PoolAlloc()
        {
            if (_poolFree.Count > 0)
            {
                var s = _poolFree[_poolFree.Count - 1];
                _poolFree.RemoveAt(_poolFree.Count - 1);
                return s;
            }
            return _poolNext++;
        }

        /// <summary>读取点结算：池化值在读取块内的剩余计数减一，归零即归还槽位。
        /// 计数按块分段（UseCount）——跨块常量在各使用块有独立槽与独立计数，
        /// 若用全函数单计数器，首块槽位永远等不到归零（泄漏）。</summary>
        void ReleaseDying(SsaBlock block, SsaValue? v)
        {
            if (v == null || !_pooled.Contains(v))
                return;
            var info = Info(block);
            // 立即数消费（IsImmediateOperand）未计数，此处也无键可结算
            if (!info.UseCount.TryGetValue(v, out var left))
                return;
            left--;
            info.UseCount[v] = left;
            Debug.Assert(left >= 0, "读取结算次数超过登记次数");
            if (left == 0)
                _poolFree.Add(_slots[v]);
        }

        void ReleaseOperands(SsaBlock block, SsaValue inst)
        {
            ReleaseDying(block, inst.Arg0);
            ReleaseDying(block, inst.Arg1);
            if (inst.ExtraArgs != null)
                foreach (var a in inst.ExtraArgs)
                    ReleaseDying(block, a);
        }

        static int ArityOf(SsaValue inst)
        {
            int n = inst.Arg0 != null ? 1 : 0;
            if (inst.ExtraArgs != null) n += inst.ExtraArgs.Count;
            return inst.Op switch
            {
                SsaOp.Call or SsaOp.StaticCall or SsaOp.ArrayInit => n,
                SsaOp.Capture or SsaOp.Ocr or SsaOp.Roi or SsaOp.OcrInit => n,
                _ => 0,
            };
        }

        // ---- 常量物化（块首惰性，替代函数级 prologue）----

        void EmitLoadConst(SsaValue v)
        {
            var dst = Slot(v);
            switch (v.Op)
            {
                case SsaOp.ConstBool:
                    EmitIabc(EcsOpcode.LoadBool, dst, v.Const.GetBool() ? 1 : 0, 0);
                    break;
                case SsaOp.ConstByte:
                case SsaOp.ConstInt:
                case SsaOp.ConstUInt:
                    {
                        int val = v.Const.GetInt();
                        if (val >= short.MinValue && val <= short.MaxValue)
                            EmitAsBx(EcsOpcode.LoadI, dst, val);
                        else
                            EmitAbx(EcsOpcode.LoadK, dst, _pool.Add(EcsConst.FromInt(val)));
                        break;
                    }
                case SsaOp.ConstUInt64:
                    EmitAbx(EcsOpcode.LoadK, dst, _pool.Add(EcsConst.FromUInt64(v.Const.GetUInt64())));
                    break;
                case SsaOp.ConstDouble:
                    EmitAbx(EcsOpcode.LoadK, dst, _pool.Add(EcsConst.FromDouble(v.Const.GetDouble())));
                    break;
                case SsaOp.ConstPtr:
                    EmitAbx(EcsOpcode.LoadK, dst, _pool.Add(EcsConst.FromPtr(v.Const.GetPtr())));
                    break;
                case SsaOp.ConstString:
                    EmitAbx(EcsOpcode.LoadK, dst, _pool.AddString(v.ConstString ?? ""));
                    break;
                default:
                    throw Fail($"非常量值进入 prologue: {v.Op}");
            }
        }
    }
}