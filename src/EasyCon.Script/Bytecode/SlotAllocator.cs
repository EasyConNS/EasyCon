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
        ///
        /// 读取登记（UseCount）与结算计划（_instReleases/_terminatorReleases）在同一次
        /// 遍历中推导——结算点由登记点唯一确定，发射侧只回放计划，不存在第二条
        /// 手写遍历（对齐 Go regalloc computeLive / LLVM LiveIntervals 的「分析一次、
        /// 消费多处」与 regalloc2 的「访问方式在指令上声明一次」原则；Lua lcode.c
        /// 的 freeexp 同趟释放则是单遍化的极限形态，此处因发射非单遍而取计划回放）。
        /// </summary>
        void AssignSlots()
        {
            _next = _fn.Layout.SlotCount;

            // 1. 使用点收集 + 结算计划推导（同源）：操作数/分支条件记在使用块；phi 臂记在对应前驱
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

                SsaValue? returnValue = null;
                foreach (var inst in block.Instructions)
                {
                    if (inst.Op == SsaOp.Return && returnValue == null)
                        returnValue = inst.Arg0;   // 与 EmitTerminator 一致取首条 Return

                    // 常量模板降级（ArrayTemplate）：元素是编译期数据（构建段直连常量池/模板全局），
                    // 不读槽——不登记使用、不物化块首常量、不进结算计划
                    if (IsTemplateArrayInit(inst))
                        continue;

                    // 登记与计划同点判定：立即数消费不读槽 → 既不登记也不进计划
                    List<SsaValue>? releases = null;
                    if (inst.Arg0 != null && !IsImmediateOperand(inst, inst.Arg0))
                    {
                        RecordRead(inst.Arg0, block);
                        releases = [inst.Arg0];
                    }
                    if (inst.Arg1 != null)
                    {
                        RecordRead(inst.Arg1, block);
                        (releases ??= []).Add(inst.Arg1);
                    }
                    if (inst.ExtraArgs != null)
                        foreach (var a in inst.ExtraArgs)
                        {
                            RecordRead(a, block);
                            (releases ??= []).Add(a);
                        }

                    // 只有直接发射的指令在发射点回放结算；Return 的操作数归终结符计划
                    if (releases != null && IsDirectlyEmitted(inst.Op))
                        _instReleases[inst] = [.. releases];
                }

                // 终结符结算计划：返回值 + 分支条件 + 出边 φ 臂（t==f 只算一条出边，与发射一致）
                var terminatorReleases = new List<SsaValue>();
                if (block.IsReturn && returnValue != null)
                    terminatorReleases.Add(returnValue);
                if (block.BranchCondition != null)
                {
                    RecordRead(block.BranchCondition, block);
                    terminatorReleases.Add(block.BranchCondition);
                }
                foreach (var successor in SuccessorEdges(block))
                {
                    if (successor.Phis.Count == 0 || !successor.Predecessors.Contains(block))
                        continue;
                    var armIdx = successor.Predecessors.IndexOf(block);
                    foreach (var phi in successor.Phis)
                    {
                        if (phi.ExtraArgs == null || armIdx >= phi.ExtraArgs.Count)
                            continue;
                        terminatorReleases.Add(phi.ExtraArgs[armIdx]);   // 死 φ 的臂同样登记过，结算一次
                    }
                }
                if (terminatorReleases.Count > 0)
                    _terminatorReleases[block] = [.. terminatorReleases];
            }

            // 2. 分类：跨块值专用槽；块内值登记池化；常量已按块登记
            bool hasPhi = false, hasNeq = false, hasTpl = false;
            int maxArity = 1;
            foreach (var block in _fn.Blocks)
            {
                var info = Info(block);
                foreach (var phi in block.Phis)
                {
                    if (!_totalReads.ContainsKey(phi))
                        continue;   // 死 φ（防线 1）：全函数无读取——不占槽，前驱边不产生副本（EmitEdgeCopies 跳过）
                    _slots[phi] = _next++;
                    hasPhi = true;
                }
                foreach (var inst in block.Instructions)
                {
                    if (inst.Op is SsaOp.NeqInt or SsaOp.NeqUInt or SsaOp.NeqDouble or SsaOp.NeqUInt64
                        or SsaOp.NeqBool or SsaOp.NeqByte or SsaOp.NeqString or SsaOp.NeqPtr)
                        hasNeq = true;
                    // 常量模板降级：元素不占槽也不进 staging（maxArity 不计入），结果槽照常分类
                    bool templateInit = IsTemplateArrayInit(inst);
                    if (templateInit)
                        hasTpl = true;
                    int arity = templateInit ? 0 : ArityOf(inst);
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
            if (hasTpl)
            {
                _tplGuard = _next++;
                _tplArr = _next++;
                _tplChunk = _next++;
            }
            _stagingBase = _next;
            var stagingCount = maxArity;
            if (hasTpl)
            {
                // 常量模板分块构建：staging 区须容纳一个分块（NewArrV 的 C:8 上限 255 之内取满）
                _tplChunkSize = Math.Min(TemplateChunkSize, 254 - _stagingBase);
                if (_tplChunkSize <= 0)
                    throw Fail("常量模板构建 staging 槽不足（函数局部槽过多）");
                if (stagingCount < _tplChunkSize)
                    stagingCount = _tplChunkSize;
            }
            _receiveSlot = _stagingBase + stagingCount;
            _next += stagingCount + 1;
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

        /// <summary>
        /// 指令是否按普通指令直接发射（否则：终结符/标记由 EmitTerminator 处理，
        /// 常量占位由块首按需物化）。发射遍历与结算计划共用此谓词——指令筛选
        /// 只有一处定义，登记/计划与发射两条遍历不可能在此漂移。
        /// </summary>
        static bool IsDirectlyEmitted(SsaOp op) => op switch
        {
            SsaOp.Phi or SsaOp.CondBranch or SsaOp.Branch or SsaOp.Return or SsaOp.Nop => false,
            SsaOp.ConstBool or SsaOp.ConstByte or SsaOp.ConstInt or SsaOp.ConstUInt
                or SsaOp.ConstUInt64 or SsaOp.ConstDouble or SsaOp.ConstString or SsaOp.ConstPtr => false,
            _ => true,
        };

        /// <summary>块的所有出边（t==f 去重为一条——发射侧对同一后继只发一份副本、结算一次，A-03）。</summary>
        static IEnumerable<SsaBlock> SuccessorEdges(SsaBlock block)
        {
            if (block.BranchCondition != null)
            {
                if (block.TrueSuccessor != null)
                    yield return block.TrueSuccessor;
                if (block.FalseSuccessor != null && !ReferenceEquals(block.FalseSuccessor, block.TrueSuccessor))
                    yield return block.FalseSuccessor;
            }
            else if (block.JumpTarget != null)
            {
                yield return block.JumpTarget;
            }
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
        /// 若用全函数单计数器，首块槽位永远等不到归零（泄漏）。
        /// 只由结算计划回放调用：计划与登记同点推导，立即数消费天然无键，
        /// 不会出现「无读取的消费」抢走同块真实读取登记的失衡。</summary>
        void ReleaseDying(SsaBlock block, SsaValue? v)
        {
            if (v == null || !_pooled.Contains(v))
                return;
            var info = Info(block);
            if (!info.UseCount.TryGetValue(v, out var left))
                return;
            left--;
            info.UseCount[v] = left;
            Debug.Assert(left >= 0, "读取结算次数超过登记次数");
            if (left == 0)
                _poolFree.Add(_slots[v]);
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

        void EmitLoadConst(SsaValue v) => EmitConstToSlot(v, Slot(v));

        /// <summary>常量 → 指定槽（LoadBool/LoadI 短立即数/LoadK 常量池）；块首物化与常量模板构建段共用。</summary>
        void EmitConstToSlot(SsaValue v, int dst)
        {
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