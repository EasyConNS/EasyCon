namespace EasyCon.Script.Bytecode;

/// <summary>
/// 指令流线性扫描器（回调式，链接器各分析/重写 pass 共享）。
/// 扫描对象是解码形态 <see cref="EcsInstruction"/>——单流化（docs/SingleStreamFormat.md §2）后
/// 不再有「按 ExtWords 步进猜字长」的字流扫描：EXT 数据字是指令内字段，天然不被误读为指令
///（历史 F4 缺陷形态随双流一起消失）。重写型 pass 经返回的新指令原地替换。
/// </summary>
internal static class InstructionScanner
{
    public sealed class Callbacks
    {
        /// <summary>置 true 后扫描在当前指令处理完即停止（早退：可达性命中等）。</summary>
        public bool Stop;

        /// <summary>每条指令（含返回的替换指令；null = 保持原指令）。ABx 类重写（LoadG/StoreG/NewSt/LoadK/Img）用。</summary>
        public Func<EcsInstruction, EcsOpcode, EcsInstruction?>? OnInstruction;
        /// <summary>Call：ext = 目标 fid（链接前模块局部/导入标记，链接后镜像全局）。</summary>
        public Func<uint, uint?>? OnCall;
        /// <summary>CallN：ext = 原生名表 nid / syscall 编号。</summary>
        public Func<uint, uint?>? OnCallN;
        /// <summary>LoadK/Img：Bx = 模块/镜像常量池索引。</summary>
        public Action<int>? OnConstRef;
        /// <summary>其余 EXT 指令：ext 为数据字（语义见 EcsFormat.ExtKind）。</summary>
        public Action<EcsOpcode, uint>? OnExt;
    }

    public static void Scan(List<EcsInstruction> code, Callbacks cb)
    {
        for (int i = 0; i < code.Count; i++)
        {
            var ins = code[i];
            var replaced = cb.OnInstruction?.Invoke(ins, ins.Op);
            if (replaced != null)
                code[i] = ins = replaced.Value;
            switch (ins.Op)
            {
                case EcsOpcode.Call:
                    if (cb.OnCall?.Invoke(ins.Ext) is { } callExt)
                        code[i] = ins = ins with { Ext = callExt };
                    break;
                case EcsOpcode.CallN:
                    if (cb.OnCallN?.Invoke(ins.Ext) is { } callNExt)
                        code[i] = ins = ins with { Ext = callNExt };
                    break;
                case EcsOpcode.LoadK or EcsOpcode.Img:
                    cb.OnConstRef?.Invoke(ins.B);
                    break;
                default:
                    if (ins.HasExt)
                        cb.OnExt?.Invoke(ins.Op, ins.Ext);
                    break;
            }
            if (cb.Stop)
                return;
        }
    }
}
