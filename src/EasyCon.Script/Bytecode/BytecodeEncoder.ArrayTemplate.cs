using EasyCon.Script.Runtime;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Text;

namespace EasyCon.Script.Bytecode;

public static partial class BytecodeEncoder
{
    /// <summary>
    /// 全常量数组字面量走常量模板降级的最小元素数。低于此值沿用 staging+NewArrV 内联路径
    /// （代码更紧凑，无全局表占用）。
    /// </summary>
    public const int TemplateArrayMinElements = 8;

    /// <summary>常量模板构建的分块元素数（NewArrV 的 C:8 ≤ 255、staging 槽预算内取值）。</summary>
    public const int TemplateChunkSize = 32;

    sealed partial class Encoder
    {
        // ---- 全常量数组字面量的常量模板降级 ----
        //
        // 数组字面量只能以立即数列表初始化，元素编译期完全可见——大数据表（如万级颜色/字库表）
        // 本质是常量数据。内联路径却把每个元素当独立 SSA 常量物化进帧槽（块首全量同时存活）+
        // staging 槽随元素数线性增长，单函数槽位随字面量爆炸（1 万元素 ≈ 2 万槽 > 255 的 ISA 上限）。
        //
        // 降级方案（C 静态初始化模板 + 守卫的同型设计 / Go static composite literal init）：
        //   - 字面量内容以首个初始化点为准构建一次，存入模块级隐藏全局模板（内容键去重）；
        //   - 每个初始化点 = 守卫检查（未构建则构建）+ LoadG + SetVar 深拷贝；
        //     深拷贝保持「每次声明得到独立可变数组」的既有语义（对齐 NewArrV 逐点重建）。
        //   - 仅用既有指令（LoadG/StoreG/Jpf/Jmp/NewArrE/LoadK/LoadI/SetI/SetVar），
        //     ISA、ECX/ECM 冻结格式、C VM 均零改动；普通脚本产物对 MCU 依旧可执行，
        //     巨表产物体积超限则由格式上限天然拒绝（不静默）。

        static bool IsTemplateArrayInit(SsaValue v)
        {
            if (v.Op != SsaOp.ArrayInit)
                return false;
            var elements = TemplateElements(v);
            return elements.Count >= TemplateArrayMinElements && elements.All(e => e.IsConstant);
        }

        static List<SsaValue> TemplateElements(SsaValue v)
        {
            var elements = new List<SsaValue>();
            if (v.Arg0 != null) elements.Add(v.Arg0);
            if (v.ExtraArgs != null) elements.AddRange(v.ExtraArgs);
            return elements;
        }

        /// <summary>模板内容键：元素类型码 + 逐元素（SSA 常量操作码 + 位级值）。</summary>
        string TemplateKey(SsaValue v)
        {
            var sb = new StringBuilder();
            sb.Append(TypeCode(((ArrayType)v.Type).ElementType)).Append(';');
            foreach (var e in TemplateElements(v))
            {
                sb.Append((int)e.Op).Append(':');
                switch (e.Op)
                {
                    case SsaOp.ConstBool:
                        sb.Append(e.Const.GetBool() ? '1' : '0');
                        break;
                    case SsaOp.ConstString:
                        sb.Append(e.ConstString?.Length ?? 0).Append(':').Append(e.ConstString);
                        break;
                    case SsaOp.ConstDouble:
                        sb.Append(BitConverter.DoubleToInt64Bits(e.Const.GetDouble()));
                        break;
                    case SsaOp.ConstUInt64:
                        sb.Append(e.Const.GetUInt64());
                        break;
                    case SsaOp.ConstPtr:
                        sb.Append(e.Const.GetPtr());
                        break;
                    default:
                        sb.Append(e.Const.GetInt());
                        break;
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        /// <summary>
        /// 发射守卫式常量模板初始化（结果槽的分配/结算仍走常规计划）。
        /// 构建段 = 分块装载：每块经 staging 区 NewArrV 成块（元素直写 staging 槽，不占 SSA 帧槽），
        /// 块间 Cat 拼接（数组拼接语义，双端 VM 一致）；单值 Append 会整表复制，O(N²) 不可取。
        /// 守卫为条件跳转仅 s16 偏移，构建段长度任意——用「Jpf(+1) + Jmp(s24)」双跳组合：
        /// 未构建（guard=0）→ Jpf 越过近处的 Jmp 进入构建段；已构建 → Jmp 跳过整段（s24 任意远）。
        /// </summary>
        void EmitTemplateArrayInit(SsaValue v)
        {
            var elemCode = TypeCode(((ArrayType)v.Type).ElementType);
            var slots = _ctx.ArrayTemplate(TemplateKey(v));
            var elements = TemplateElements(v);

            EmitAbx(EcsOpcode.LoadG, _tplGuard, slots.Guard);
            int enter = NewLabel();
            int end = NewLabel();
            EmitFixJpf(_tplGuard, enter);   // guard=0（未构建）→ 越过下面的 Jmp 进入构建段
            EmitJmpToLabel(end);            // guard≠0（已构建）→ s24 跳过整段
            MarkLabel(enter);

            int offset = 0;
            while (offset < elements.Count)
            {
                int count = Math.Min(_tplChunkSize, elements.Count - offset);
                for (int j = 0; j < count; j++)
                    EmitConstToSlot(elements[offset + j], _stagingBase + j);
                if (offset == 0)
                {
                    EmitExt(EcsOpcode.NewArrV, _tplArr, count, _stagingBase, elemCode);
                }
                else
                {
                    EmitExt(EcsOpcode.NewArrV, _tplChunk, count, _stagingBase, elemCode);
                    EmitIabc(EcsOpcode.Cat, _tplArr, _tplArr, _tplChunk);
                }
                offset += count;
            }
            EmitAbx(EcsOpcode.StoreG, _tplArr, slots.Template);
            EmitAsBx(EcsOpcode.LoadI, _tplGuard, 1);
            EmitAbx(EcsOpcode.StoreG, _tplGuard, slots.Guard);

            MarkLabel(end);
            EmitAbx(EcsOpcode.LoadG, _tplArr, slots.Template);
            EmitIabc(EcsOpcode.SetVar, Slot(v), _tplArr, 0);   // 深拷贝：每次初始化得到独立可变数组（语义对齐 NewArrV 逐点重建）
        }

    }
}