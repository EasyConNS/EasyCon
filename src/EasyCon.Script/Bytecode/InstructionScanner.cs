namespace EasyCon.Script.Bytecode;

/// <summary>
/// ECX 代码线性扫描器（回调式，链接器各分析/重写 pass 共享）。步进由
/// <see cref="EcsFormat.ExtWords"/> 驱动——EXT 后随字是「数据」，数值可能恰好等于某个
/// 操作码，扫描器保证它永不被误读为指令（历史 F4 缺陷形态）。
/// 新增分析/重写 pass 只写回调，不再复制指令遍历骨架；重写型 pass 经
/// <c>code[w] = …</c> 原地修改（IList 视图）。
/// </summary>
internal static class InstructionScanner
{
    public sealed class Callbacks
    {
        /// <summary>置 true 后扫描在当前指令处理完即停止（早退：可达性命中等）。</summary>
        public bool Stop;

        /// <summary>每条指令（含 EXT 首字）——ABx 类原地重写（LoadG/StoreG/NewSt/LoadK/Img）用。</summary>
        public Action<IList<uint>, int, EcsOpcode>? OnInstruction;
        /// <summary>Call：ext = 目标 fid（链接前模块局部/导入标记，链接后镜像全局）。</summary>
        public Action<IList<uint>, int, uint>? OnCall;
        /// <summary>CallN：ext = 原生名表 nid。</summary>
        public Action<IList<uint>, int, uint>? OnCallN;
        /// <summary>LoadK/Img：Bx = 模块/镜像常量池索引（ABx，无 EXT 后随字）。</summary>
        public Action<IList<uint>, int, int>? OnConstRef;
        /// <summary>其余 EXT 指令：ext 为数据字（语义见 EcsFormat.ExtKind）。</summary>
        public Action<IList<uint>, int, EcsOpcode, uint>? OnExt;
    }

    public static void Scan(IList<uint> code, Callbacks cb)
    {
        for (int w = 0; w < code.Count; w++)
        {
            var op = (EcsOpcode)(code[w] & 0xFF);
            cb.OnInstruction?.Invoke(code, w, op);
            switch (op)
            {
                case EcsOpcode.Call:
                    cb.OnCall?.Invoke(code, w, code[w + 1]);
                    break;
                case EcsOpcode.CallN:
                    cb.OnCallN?.Invoke(code, w, code[w + 1]);
                    break;
                case EcsOpcode.LoadK or EcsOpcode.Img:
                    cb.OnConstRef?.Invoke(code, w, (int)((code[w] >> 16) & 0xFFFF));
                    break;
                default:
                    if (EcsFormat.ExtWords(op) > 0)
                        cb.OnExt?.Invoke(code, w, op, code[w + 1]);
                    break;
            }
            if (cb.Stop)
                return;
            w += EcsFormat.ExtWords(op);   // EXT 后随字是数据：步进跳过
        }
    }
}