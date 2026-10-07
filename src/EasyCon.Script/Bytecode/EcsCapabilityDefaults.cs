using EasyCon.Script.Symbols;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// S-21 能力降级缺省值表（docs/VmSemanticContract.md S-21 / V23_VM_REDESIGN.md §2）：
/// 宿主能力缺失时，L2 syscall 未实现 / L3 原生无处理器或未知名，按本表返回类型正确的
/// 中性缺省值——双端（C# 解释器 ↔ C VM <c>ecs_cap_syscall_default/ecs_cap_native_default</c>）
/// 对同一缺失能力给出<b>逐字相同</b>的结果。本类为单一事实源，C 侧为双生定义，
/// 漂移由能力矩阵四路对拍（CapabilityMatrixTests）锁定。
///
/// 规则：降级不是旁路——按指令的接收槽语义走正常槽写原语，「换一个结果值」；
/// 表外条目（未知编号 / 未知原生名）不降级，保持 ECS_ERR_NOSUCHNATIVE 响亮失败；
/// L1 域操作（wait/key/stick/rand）不降级；strict_caps 宿主整体绕过本表恢复响亮行为。
/// </summary>
public static class EcsCapabilityDefaults
{
    /// <summary>
    /// L2 syscall 缺省值（宿主 syscall 处理器缺失/返回未实现时）。
    /// 返回 null = 表外（未知编号）→ 调用方按 strict/非 strict 决定报错。
    /// 无接收槽语义的 Void 类 syscall 返回 <see cref="TaggedValue.Void"/>（CallN 对 Void
    /// syscall 恒无接收槽，等价 no-op）。
    /// </summary>
    public static TaggedValue? GetSyscallDefault(int id, TaggedValue[] args, EcxNativeContext ctx)
    {
        switch (id)
        {
            case EcsSyscall.FWrite:    /* PRINT 降级：静默仍返 len（S-14） */
                return TaggedValue.FromInt(ctx.Str(args[1]).Length);
            case EcsSyscall.FRead:
            case EcsSyscall.ReadFile:
                return ctx.Str("");
            case EcsSyscall.FOpen:
                return TaggedValue.FromInt(-1);   /* 无效句柄 */
            case EcsSyscall.FEof:
            case EcsSyscall.FileExists:
                return TaggedValue.FromInt(0);
            case EcsSyscall.FClose:
            case EcsSyscall.WriteFile:
            case EcsSyscall.AppendFile:
            case EcsSyscall.Alert:
            case EcsSyscall.Beep:
            case EcsSyscall.Amiibo:
                return TaggedValue.Void;
            case EcsSyscall.Arg:
            case EcsSyscall.Env:
            case EcsSyscall.App:
                return ctx.Str("");
            case EcsSyscall.Time:
                return TaggedValue.FromInt(0);
            case EcsSyscall.OcrConf:
                return TaggedValue.FromInt(0);
            default:
                return null;   /* 未知编号不降级 */
        }
    }

    /// <summary>
    /// L3 名表原生缺省值（宿主 Native 缺失或对名字返回未实现时）。
    /// 名含 <c>!</c> = EXTERN FFI（"库!导出名"）→ int 0 中性缺省；
    /// 表外未知名返回 null → 调用方报错（打错名不静默）。
    /// </summary>
    public static TaggedValue? GetNativeDefault(string name, EcxNativeContext ctx)
    {
        switch (name)
        {
            case "__CAPTURE__":   /* 采集/ROI 帧 → 空图串 */
            case "__ROI__":
            case "__OCR__":       /* OCR → 空串 */
            case "ENCODE":
            case "JQ":
                return ctx.Str("");
            case "__OCR_INIT__":  /* 初始化失败语义：false */
                return TaggedValue.FromInt(0);
            case "NET_LOAD":      /* ONNX 推理族（VisionInferenceTests 缺能力档同值） */
                return TaggedValue.FromInt(-1);
            case "NET_RUN":
                return TaggedValue.FromInt(0);
            case "NET_OUT":
                return TaggedValue.FromDouble(0.0);
            default:
                break;
        }
        if (name.Contains('!'))   /* EXTERN FFI → int 0（类型经后续 Conv 决定） */
            return TaggedValue.FromInt(0);
        return null;
    }
}