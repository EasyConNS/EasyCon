using EasyCon.Script.Symbols;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// S-21 能力降级缺省值表（契约见 docs/VM2.md §6.3）——
/// <b>桌面宿主专属</b>：可选能力（推理/OCR/文件/FFI）未装配时，L2 syscall 未实现 /
/// L3 原生缺失按本表返回类型正确的中性缺省值，脚本不响亮失败。
/// C/MCU 侧没有本表（MCU 不支持桌面原生与 FFI）：C VM native 缺失一律
/// ECS_ERR_NOSUCHNATIVE 响亮失败；仅 L2 syscall 保留双端锁步的参考桩降级
/// （C ecs_cap_syscall_default，CapabilityMatrixTests 锁定）。
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
                return TaggedValue.FromPtr(-1);   /* 无效句柄（PTR 类型——AsPtr 消费方契约） */
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
            case "__TESSERACT__": /* Tesseract OCR → 空串 */
            case "__CTC_FOLD__":  /* CTC 折叠 → 空串 */
            case "__ENCODE__":
            case "__JQ__":
                return ctx.Str("");
            case "__TESSERACT_INIT__":  /* 初始化失败语义：false */
                return TaggedValue.FromInt(0);
            case "NET_LOAD":      /* 会话句柄：PTR -1 = 失败（VisionInferenceTests 缺能力档同值） */
                return TaggedValue.FromPtr(-1);
            case "__NET_IMAGE__": /* 张量句柄：PTR 0 = 无效句柄（与各 Impl 的 null-inference 分支同值） */
            case "NET_SCALE":
            case "__NET_IMAGE_FILE__":
            case "NET_NORM":
                return TaggedValue.FromPtr(0);
            case "NET_RUNH":      /* 输出长度：INT */
            case "NET_ROWS":
            case "NET_FREE":      /* 状态：INT */
            case "NET_UNLOAD":
                return TaggedValue.FromInt(0);
            case "NET_ARGMAX":
                return TaggedValue.FromInt(-1);
            case "NET_OUT":
                return TaggedValue.FromDouble(0.0);
            case "NET_LABELS":    /* 标签表：加载失败语义 -1（与 Impl 读失败同值） */
                return TaggedValue.FromInt(-1);
            case "NET_LABEL":     /* 未加载/越界 → 空串 */
                return ctx.Str("");
            case "__YOLO_DECODE__": /* YOLO 解码：无推理能力 → 0 命中（无目标语义） */
            case "YOLO_COUNT":
            case "__DET_BOXES__":   /* det 找框：无推理能力 → 0 框（同无目标语义） */
            case "DET_COUNT":
                return TaggedValue.FromInt(0);
            case "YOLO_CLS":        /* 访问器越界语义：-1（与 NET_ARGMAX 同约定） */
            case "YOLO_X1":
            case "YOLO_Y1":
            case "YOLO_X2":
            case "YOLO_Y2":
            case "DET_X":
            case "DET_Y":
            case "DET_W":
            case "DET_H":
                return TaggedValue.FromInt(-1);
            case "YOLO_SCORE":
                return TaggedValue.FromDouble(-1.0);
            default:
                break;
        }
        if (name.Contains('!'))   /* EXTERN FFI → int 0（类型经后续 Conv 决定） */
            return TaggedValue.FromInt(0);
        return null;
    }
}