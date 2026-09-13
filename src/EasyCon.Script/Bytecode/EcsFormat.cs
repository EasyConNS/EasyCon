namespace EasyCon.Script.Bytecode;

/// <summary>指令编码格式（docs/VM2.md §4.1）。</summary>
public enum EcsInsFormat : byte
{
    /// <summary>op:8 | A:8 | B:8 | C:8（4 字节）</summary>
    Iabc,
    /// <summary>op:8 | A:8 | sBx:16（4 字节，有符号）</summary>
    AsBx,
    /// <summary>op:8 | A:8 | Bx:16（4 字节，无符号）</summary>
    ABx,
    /// <summary>op:8 | s24:24（4 字节，有符号，单位=指令字）</summary>
    IsJ,
    /// <summary>iABC + 后随 ext:32（8 字节）</summary>
    Ext,
}

/// <summary>EXT 后随 32 位数据字的语义（docs/VM2.md §4.1/§4.2）。</summary>
public enum EcsExtKind : byte
{
    /// <summary>非 EXT 指令：无后随数据字。</summary>
    None = 0,
    /// <summary>Call：目标函数 ID（链接前=模块局部 fid/导入标记，链接后=镜像全局 fid）。</summary>
    CallTarget,
    /// <summary>CallN：原生函数名表 ID。</summary>
    NativeId,
    /// <summary>NewArrV：数组字面量元素类型码。</summary>
    ElemTypeCode,
    /// <summary>Slice：end 槽位号（0xFFFFFFFF=省略端）。</summary>
    SliceEndSlot,
    /// <summary>GetFI/PutFI：固定数组字段的元素索引槽。</summary>
    FieldElemSlot,
    /// <summary>StickP：持续毫秒立即数。</summary>
    StickDuration,
    /// <summary>StickPv：高16位x | 低16位y 的 packed 坐标。</summary>
    StickXY,
}

/// <summary>结果槽写集（docs/VM2.md §4.2）：指令执行后写回的帧槽位字段。
/// 访存型写（SetI/PutF/PutFI 写容器元素、StoreG 写全局槽）与副作用/控制流不写结果槽，归 None。</summary>
public enum EcsResultSlot : byte
{
    /// <summary>不写帧结果槽（访存/副作用/转移/返回类）。</summary>
    None = 0,
    /// <summary>写 R[A]（取值/运算/比较/构造类，含 SetVar 的深拷贝写）。</summary>
    A,
    /// <summary>写 R[C]（Call/CallN 的接收槽，C=255 表示无接收）。</summary>
    C,
}

/// <summary>
/// 指令格式单一事实源（docs/VM2.md §4.1/§4.2）：每操作码恰一行的编码格式、EXT 数据字语义、
/// 结果槽写集。发射侧（BytecodeEncoder.Emit* 自检）、扫描侧（EcxLinker 各 pass 经 ExtWords
/// 步进、InstructionScanner）、反汇编（EcxDisassembler）、体积分析（BytecodeSizeAnalysisTests）
/// 与 C VM 的 ecs_op_has_ext 表（ecs_vm.c，紧邻 OP_ 枚举）均以本表为对齐基准。
/// 关键不变量：EXT 后随字是「数据」，其数值可能恰好等于某个操作码（如元素类型码 3 == LoadK），
/// 一切线性扫描必须按 ExtWords 步进跳过，否则把数据误读为指令（轻则静默改坏 Slice 槽位/
/// StickPv 时长，重则越界崩溃——历史 F4 缺陷即此类）。
/// 新增指令：EcsOpcode 枚举加值 + 本表加一行（漏登会在 BuildTable 完整性自检抛出）+
/// 双端执行/发射语义 + C 侧表同步；全部扫描/步进/反汇编消费点自动正确。
/// </summary>
public static class EcsFormat
{
    public static EcsInsFormat Get(EcsOpcode op) => Table[(int)op].Format;

    public static EcsExtKind ExtKind(EcsOpcode op) => Table[(int)op].Ext;

    /// <summary>结果槽写集（执行语义的编码侧投影，见 EcsResultSlot）。</summary>
    public static EcsResultSlot ResultSlot(EcsOpcode op) => Table[(int)op].Result;

    /// <summary>指令总字数（4 字节字为单位；EXT=2，其余=1）。</summary>
    public static int WordCount(EcsOpcode op) => Get(op) == EcsInsFormat.Ext ? 2 : 1;

    /// <summary>EXT 后随 32 位数据字个数（非 EXT 指令为 0）。线性扫描器的唯一步进依据。</summary>
    public static int ExtWords(EcsOpcode op) => Get(op) == EcsInsFormat.Ext ? 1 : 0;

    struct InsInfo
    {
        public EcsInsFormat Format;
        public EcsExtKind Ext;
        public EcsResultSlot Result;
    }

    static readonly InsInfo[] Table = BuildTable();

    static InsInfo[] BuildTable()
    {
        var t = new InsInfo[256];
        int registered = 0;
        void F(EcsOpcode op, EcsInsFormat format, EcsResultSlot result = EcsResultSlot.A, EcsExtKind ext = EcsExtKind.None)
        {
            t[(int)op] = new InsInfo { Format = format, Ext = ext, Result = result };
            registered++;
        }

        // ---- 杂项 ----
        F(EcsOpcode.Nop, EcsInsFormat.Iabc, EcsResultSlot.None);
        F(EcsOpcode.Halt, EcsInsFormat.Iabc, EcsResultSlot.None);

        // ---- 常量与移动 ----
        F(EcsOpcode.LoadI, EcsInsFormat.AsBx);
        F(EcsOpcode.LoadK, EcsInsFormat.ABx);
        F(EcsOpcode.LoadBool, EcsInsFormat.Iabc);
        F(EcsOpcode.Move, EcsInsFormat.Iabc);
        F(EcsOpcode.SetVar, EcsInsFormat.Iabc);
        F(EcsOpcode.LoadG, EcsInsFormat.ABx);
        F(EcsOpcode.StoreG, EcsInsFormat.ABx, EcsResultSlot.None);

        // ---- 算术 ----
        F(EcsOpcode.AddI, EcsInsFormat.Iabc);
        F(EcsOpcode.SubI, EcsInsFormat.Iabc);
        F(EcsOpcode.MulI, EcsInsFormat.Iabc);
        F(EcsOpcode.DivI, EcsInsFormat.Iabc);
        F(EcsOpcode.ModI, EcsInsFormat.Iabc);
        F(EcsOpcode.RDivI, EcsInsFormat.Iabc);
        F(EcsOpcode.AddU, EcsInsFormat.Iabc);
        F(EcsOpcode.SubU, EcsInsFormat.Iabc);
        F(EcsOpcode.MulU, EcsInsFormat.Iabc);
        F(EcsOpcode.DivU, EcsInsFormat.Iabc);
        F(EcsOpcode.ModU, EcsInsFormat.Iabc);
        F(EcsOpcode.AddL, EcsInsFormat.Iabc);
        F(EcsOpcode.SubL, EcsInsFormat.Iabc);
        F(EcsOpcode.MulL, EcsInsFormat.Iabc);
        F(EcsOpcode.DivL, EcsInsFormat.Iabc);
        F(EcsOpcode.ModL, EcsInsFormat.Iabc);
        F(EcsOpcode.AddD, EcsInsFormat.Iabc);
        F(EcsOpcode.SubD, EcsInsFormat.Iabc);
        F(EcsOpcode.MulD, EcsInsFormat.Iabc);
        F(EcsOpcode.DivD, EcsInsFormat.Iabc);

        // ---- 位运算 ----
        F(EcsOpcode.BandI, EcsInsFormat.Iabc);
        F(EcsOpcode.BorI, EcsInsFormat.Iabc);
        F(EcsOpcode.BxorI, EcsInsFormat.Iabc);
        F(EcsOpcode.ShlI, EcsInsFormat.Iabc);
        F(EcsOpcode.ShrI, EcsInsFormat.Iabc);
        F(EcsOpcode.BnotI, EcsInsFormat.Iabc);

        // ---- 比较 ----
        F(EcsOpcode.EqI, EcsInsFormat.Iabc);
        F(EcsOpcode.LtI, EcsInsFormat.Iabc);
        F(EcsOpcode.LeI, EcsInsFormat.Iabc);
        F(EcsOpcode.GtI, EcsInsFormat.Iabc);
        F(EcsOpcode.GeI, EcsInsFormat.Iabc);
        F(EcsOpcode.EqU, EcsInsFormat.Iabc);
        F(EcsOpcode.LtU, EcsInsFormat.Iabc);
        F(EcsOpcode.LeU, EcsInsFormat.Iabc);
        F(EcsOpcode.GtU, EcsInsFormat.Iabc);
        F(EcsOpcode.GeU, EcsInsFormat.Iabc);
        F(EcsOpcode.EqD, EcsInsFormat.Iabc);
        F(EcsOpcode.LtD, EcsInsFormat.Iabc);
        F(EcsOpcode.LeD, EcsInsFormat.Iabc);
        F(EcsOpcode.GtD, EcsInsFormat.Iabc);
        F(EcsOpcode.GeD, EcsInsFormat.Iabc);
        F(EcsOpcode.EqL, EcsInsFormat.Iabc);
        F(EcsOpcode.LtL, EcsInsFormat.Iabc);
        F(EcsOpcode.LeL, EcsInsFormat.Iabc);
        F(EcsOpcode.GtL, EcsInsFormat.Iabc);
        F(EcsOpcode.GeL, EcsInsFormat.Iabc);
        F(EcsOpcode.EqS, EcsInsFormat.Iabc);
        F(EcsOpcode.EqP, EcsInsFormat.Iabc);

        // ---- 一元与转换 ----
        F(EcsOpcode.Not, EcsInsFormat.Iabc);
        F(EcsOpcode.NegI, EcsInsFormat.Iabc);
        F(EcsOpcode.NegD, EcsInsFormat.Iabc);
        F(EcsOpcode.Conv, EcsInsFormat.Iabc);

        // ---- 控制流 ----
        F(EcsOpcode.Jmp, EcsInsFormat.IsJ, EcsResultSlot.None);
        F(EcsOpcode.Jpt, EcsInsFormat.AsBx, EcsResultSlot.None);
        F(EcsOpcode.Jpf, EcsInsFormat.AsBx, EcsResultSlot.None);

        // ---- 调用 ----
        F(EcsOpcode.Call, EcsInsFormat.Ext, EcsResultSlot.C, EcsExtKind.CallTarget);
        F(EcsOpcode.CallN, EcsInsFormat.Ext, EcsResultSlot.C, EcsExtKind.NativeId);
        F(EcsOpcode.Ret, EcsInsFormat.Iabc, EcsResultSlot.None);
        F(EcsOpcode.Ret0, EcsInsFormat.Iabc, EcsResultSlot.None);

        // ---- 数组 / 字符串 ----
        F(EcsOpcode.NewArrV, EcsInsFormat.Ext, ext: EcsExtKind.ElemTypeCode);
        F(EcsOpcode.NewArrE, EcsInsFormat.ABx);
        F(EcsOpcode.GetI, EcsInsFormat.Iabc);
        F(EcsOpcode.SetI, EcsInsFormat.Iabc, EcsResultSlot.None);
        F(EcsOpcode.Slice, EcsInsFormat.Ext, ext: EcsExtKind.SliceEndSlot);
        F(EcsOpcode.Cont, EcsInsFormat.Iabc);
        F(EcsOpcode.Append, EcsInsFormat.Iabc);
        F(EcsOpcode.Cat, EcsInsFormat.Iabc);
        F(EcsOpcode.Len, EcsInsFormat.Iabc);

        // ---- 结构体 ----
        F(EcsOpcode.NewSt, EcsInsFormat.ABx);
        F(EcsOpcode.GetF, EcsInsFormat.Iabc);
        F(EcsOpcode.PutF, EcsInsFormat.Iabc, EcsResultSlot.None);
        F(EcsOpcode.GetFI, EcsInsFormat.Ext, ext: EcsExtKind.FieldElemSlot);
        F(EcsOpcode.PutFI, EcsInsFormat.Ext, EcsResultSlot.None, EcsExtKind.FieldElemSlot);

        // ---- 域操作 ----
        F(EcsOpcode.WaitI, EcsInsFormat.ABx, EcsResultSlot.None);
        F(EcsOpcode.WaitV, EcsInsFormat.Iabc, EcsResultSlot.None);
        F(EcsOpcode.KeyI, EcsInsFormat.ABx, EcsResultSlot.None);
        F(EcsOpcode.KeyV, EcsInsFormat.Iabc, EcsResultSlot.None);
        F(EcsOpcode.KeySt, EcsInsFormat.Iabc, EcsResultSlot.None);
        F(EcsOpcode.StickSet, EcsInsFormat.Iabc, EcsResultSlot.None);
        F(EcsOpcode.StickP, EcsInsFormat.Ext, EcsResultSlot.None, EcsExtKind.StickDuration);
        F(EcsOpcode.StickPv, EcsInsFormat.Ext, EcsResultSlot.None, EcsExtKind.StickXY);
        F(EcsOpcode.Img, EcsInsFormat.ABx);
        F(EcsOpcode.Rand, EcsInsFormat.Iabc);
        F(EcsOpcode.Time, EcsInsFormat.Iabc);
        F(EcsOpcode.Beep, EcsInsFormat.Iabc, EcsResultSlot.None);
        F(EcsOpcode.Amiibo, EcsInsFormat.Iabc, EcsResultSlot.None);

        // 完整性自检：每个枚举值必须显式登记。新增 EcsOpcode 值漏登时在此抛出
        //（而不是静默按 Iabc 编码/扫描——那正是历史 EXT 误读缺陷的成因形态）。
        if (registered != Enum.GetValues<EcsOpcode>().Length)
            throw new InvalidOperationException(
                $"EcsFormat 表登记 {registered} 行，EcsOpcode 枚举有 {Enum.GetValues<EcsOpcode>().Length} 值：存在漏登操作码");
        return t;
    }
}