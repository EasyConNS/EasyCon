using EasyCon.Script.Binding;
using EasyCon.Script.Symbols;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// VM2 指令集操作码。与 src/EasyCon.Vm/native/ecs_vm.h 中的枚举一一对应。
/// 编码格式（见 docs/VM2.md §2.1）：
///   iABC : op:8 | A:8 | B:8 | C:8
///   AsBx : op:8 | A:8 | sBx:16（有符号）
///   ABx  : op:8 | A:8 | Bx:16（无符号）
///   IsJ  : op:8 | s24:24（JMP 专用，单位=指令字）
///   EXT  : iABC + 后随 32 位字（CALL/CALLN 的目标 ID、SLICE 的 end 槽）
/// </summary>
public enum EcsOpcode : byte
{
    // ---- 杂项 ----
    Nop = 0,
    Halt,

    // ---- 常量与移动 ----
    LoadI,      // AsBx: R[A] = (int)sBx
    LoadK,      // ABx : R[A] = 常量池[Bx]
    LoadBool,   // iABC: R[A] = (B != 0)
    Move,       // iABC: R[A] = R[B]（浅拷贝，句柄引用计数 +1）
    SetVar,     // iABC: R[A] = 深拷贝(R[B])（变量赋值语义：数组/结构体复制，字符串共享）
    LoadG,      // ABx : R[A] = 全局槽[Bx]（镜像全局表索引）
    StoreG,     // ABx : 全局槽[Bx] = 深拷贝(R[A])

    // ---- 算术（iABC: R[A] = R[B] op R[C]）----
    AddI, SubI, MulI, DivI, ModI, RDivI,     // INT；RDivI 为 `\` 整除
    AddU, SubU, MulU, DivU, ModU,            // UINT
    AddL, SubL, MulL, DivL, ModL,            // UINT64
    AddD, SubD, MulD, DivD,                  // DOUBLE

    // ---- 位运算（INT）----
    BandI, BorI, BxorI, ShlI, ShrI,
    BnotI,      // iABC: R[A] = ~R[B]

    // ---- 比较（iABC: R[A] = R[B] cmp R[C]，结果 BOOL）----
    EqI, LtI, LeI, GtI, GeI,
    EqU, LtU, LeU, GtU, GeU,
    EqD, LtD, LeD, GtD, GeD,
    EqL, LtL, LeL, GtL, GeL,
    EqS,        // 字符串按 code unit 精确比较
    EqP,        // PTR 按 64 位整数比较

    // ---- 一元与转换 ----
    Not,        // iABC: R[A] = (R[B] == 0)
    NegI, NegD, // iABC
    Conv,       // iABC: R[A] = convert(R[B], kind=C)，kind 见 EcsConvKind

    // ---- 控制流 ----
    Jmp,        // IsJ : pc += s24
    Jpt,        // op:8 | A:8 | s16:16，R[A] 真值则跳转（真值 = i32 != 0）
    Jpf,        // 同 Jpt，假值跳转

    // ---- 调用（EXT：后随 32 位目标 ID）----
    Call,       // iABC + ext32：A=首参槽，B=参数个数，C=接收槽（255=无返回值）；实参取 R[A..A+B)，深拷贝进新帧槽 0..B-1；返回值按 RET 语义复制到 R[C]
    CallN,      // 同 Call，目标=原生函数 ID（镜像 native 名表）
    Ret,        // iABC: 返回 R[A]
    Ret0,       // 返回 VOID

    // ---- 数组 / 字符串 ----
    NewArrV,    // iABC + ext32: R[A] = 数组字面量，元素取 R[B..B+C)，ext=元素类型码
    NewArrE,    // ABx : R[A] = 空数组，元素类型码 Bx（EcsTypeCode）
    GetI,       // iABC: R[A] = R[B][R[C]]（数组元素 / 字符串单字符）
    SetI,       // iABC: R[B][R[C]] = R[A]（原地修改）
    Slice,      // iABC + ext32: R[A] = R[B][R[C] .. R[ext]]；0xFFFFFFFF 表示省略端；越界=运行时错误
    Cont,       // iABC: R[A] = R[B] in R[C]（数组元素类型不匹配→false；非法容器→错误）
    Append,     // iABC: R[A] = APPEND(R[B], R[C])（新数组）
    Cat,        // iABC: R[A] = 拼接(R[B], R[C])（任一侧字符串则双侧转字符串）
    Len,        // iABC: R[A] = LEN(R[B])

    // ---- 结构体 ----
    NewSt,      // ABx : R[A] = 新结构体，类型表索引 Bx
    GetF,       // iABC: R[A] = R[B].字段[C]（数组/结构体字段返回副本）
    PutF,       // iABC: R[B].字段[C] = R[A]（句柄深拷贝入字段）
    GetFI,      // iABC + ext32: R[A] = R[B].固定数组字段[C][R[ext]]（ext = 元素索引槽）
    PutFI,      // iABC + ext32: R[B].固定数组字段[C][R[ext]] = R[A]

    // ---- 域操作（本项目核心，落宿主 vtable）----
    WaitI,      // ABx : 延时 Bx 毫秒
    WaitV,      // iABC: 延时 R[A] 毫秒
    KeyI,       // ABx : 点击按键 A 持续 Bx 毫秒（key = GamePadKey 值）
    KeyV,       // iABC: 点击按键 A 持续 R[B] 毫秒
    KeySt,      // iABC: A=按键，B=按下(1)/松开(0)
    StickSet,   // iABC: A=side(0=L,1=R)，B=x 坐标，C=y 坐标（编译期常量，回中 x=y=128）
    StickP,     // iABC + ext32: A=side，B=x，C=y，ext=持续毫秒（立即数）
    StickPv,    // iABC + ext32: A=side，B=0，C=持续毫秒槽位，ext=高16位x|低16位y（packed）
    Img,        // ABx : R[A] = 图像标签匹配置信度，标签名 = 常量池[Bx]
    Rand,       // iABC: R[A] = rand() % R[B]（B=0→0；B<0→错误，对齐 Random.Next）
    ForStep,    // iABC: FOR 快速路径——R[A]==R[B] → 落入下一块（出循环）；否则 R[A]+=1 跳 C 目标（回 body）
    CmpJ,       // iABC+J: 比较跳转融合（P2′，Lua LE/EQI 模式）——R[A] kind R[B] 成立 → 跳 Jump；
                //        C=kind：typeBlock*6+op（0-5 i32 / 6-11 u32 / 12-17 f64 / 18-23 i64；op = Eq,Neq,Lt,Le,Gt,Ge）
}

/// <summary>
/// 平台 syscall 编号 ABI（uvm32 式，docs/VM2.md §9.1）：L2 封闭集合全部编号化。
/// CallN 的 EXT 字带上 0x80000000 旗标时，低 31 位 = 编号，直接双端分发，不经原生名表。
/// 语义在宿主参考实现（C# EcxHost.Syscall / C harness 桩），引擎只做取号分发；
/// 文件实现转发宿主时使用 <see cref="Names"/> 中的规范名（宿主委托签名不变）。
/// ABI 分配规则：新增追加号段、废弃不回收（AbiContractTests 锁定）。
/// </summary>
public static class EcsSyscall
{
    /// <summary>CallN EXT 旗标：置位 = syscall 编号调用（与 Call 的导入标记共用位，操作码不同不冲突）。</summary>
    public const uint CallFlag = 0x80000000u;

    /// <summary>镜像格式修订号（缓存键成分；格式语义变化时改值，无兼容梯——
    /// 全新实现不做历史版本分级）。当前 = 每函数寄存器栈窗口 + 帧内固定 RET/ARG/STAGING 布局
    /// （<see cref="EcsRegisters"/>）+ 定长指令编码 + 无接收哨兵。</summary>
    public const int AbiRevision = 1;

    // ---- 文件族（1..9 连续，IsFileFamily 依赖）----
    public const int FWrite = 1;
    public const int FRead = 2;
    public const int FOpen = 3;
    public const int FClose = 4;
    public const int FEof = 5;
    public const int ReadFile = 6;
    public const int WriteFile = 7;
    public const int AppendFile = 8;
    public const int FileExists = 9;

    // ---- 平台 syscall（10..，只追加不回收）----
    public const int Alert = 10;
    public const int Arg = 11;
    public const int Env = 12;
    public const int App = 13;
    public const int Time = 14;
    public const int Beep = 15;
    public const int Amiibo = 16;        // v2.3：Amiibo 槽位选择（0–19，宿主判越界静默；S-13）
    public const int OcrConf = 17;

    /// <summary>文件族上界（含）。</summary>
    public const int FileFamilyMax = FileExists;

    /// <summary>规范名（转发宿主 / 反汇编渲染用；与 BuiltinFunctions 声明名一致）。</summary>
    public static readonly string[] Names =
    [
        "",
        "FWRITE", "FREAD", "FOPEN", "FCLOSE", "FEOF", "READFILE", "WRITEFILE", "APPENDFILE", "FILE_EXISTS",
        "ALERT", "ARG", "ENV", "APP", "TIME", "BEEP", "AMIIBO", "OCR_CONF",
    ];

    /// <summary>声明名 → syscall 编号（内建符号专用；extern FFI 不走此表）。</summary>
    public static bool TryGetId(string name, out int id)
    {
        for (int i = 1; i < Names.Length; i++)
        {
            if (Names[i] != name)
                continue;
            id = i;
            return true;
        }
        id = 0;
        return false;
    }

    /// <summary>声明名 → CallN EXT 目标字（旗标 | 编号）；非 syscall 族返回 false。</summary>
    public static bool TryGetTarget(string name, out uint target)
    {
        if (TryGetId(name, out var id))
        {
            target = CallFlag | unchecked((uint)id);
            return true;
        }
        target = 0;
        return false;
    }
}

/// <summary>
/// 镜像特征需求掩码（镜像头 feats u16 @0x08，docs/VM2.md §9.1）：表达「本镜像使用到
/// 的高级能力」。S-21 起 feats 为<b>元信息</b>——加载器缺省不据此拒跑，能力缺失按
/// EcsCapabilityDefaults 缺省值表降级；宿主 strict_caps 置位时恢复响亮校验
/// （IL → ECS_ERR_IL，其余缺位 → ECS_ERR_FEAT）。烧录前预检（McuBytecodeDelivery）仍消费本掩码。
/// </summary>
public static class EcsImageFeatures
{
    public const uint Il = 0x1;        // 图像标签（flags.I 同源投影）
    public const uint Capture = 0x2;   // 可达采集洞（__CAPTURE__ 系）
    public const uint Ffi = 0x4;       // EXTERN FFI（"库!导出名" 动态原生）
    public const uint File = 0x8;      // 文件族 syscall
    public const uint Vision = 0x10;   // ONNX 推理函数（NET_LOAD/NET_RUNH/NET_OUT 句柄族）；缺能力宿主按缺省值表降级
}

/// <summary>
/// PC 端转换语义助手：数字字符串 → int 十进制解析
/// （允许前后空白与 +/- 号，System int 范围；无法解析返回 0）。
/// </summary>
public static class EcsConvText
{
    public static int ParseIntLiteral(string? s)
        => int.TryParse(s, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
}

/// <summary>ECS 运行时值标签，与 TaggedValue 常量保持一致。</summary>
public static class EcsTag
{
    public const byte Void = 0;
    public const byte Bool = 1;
    public const byte Byte = 2;
    public const byte Int = 3;
    public const byte UInt = 4;
    public const byte UInt64 = 5;
    public const byte Double = 6;
    public const byte String = 7;
    public const byte Array = 9;
    public const byte Ptr = 10;
    public const byte Struct = 12;
}

/// <summary>ECX 类型码（数组元素/结构体字段/全局变量声明类型）。</summary>
public enum EcsTypeCode : byte
{
    Void = 0, Bool = 1, Byte = 2, Int = 3, UInt = 4, UInt64 = 5,
    Double = 6, String = 7, Array = 8, Ptr = 9, Struct = 10, Any = 11,
}

/// <summary>Conv 指令的转换种类（C 字段，8 位）。</summary>
public enum EcsConvKind : byte
{
    IntToDouble = 0, DoubleToInt, IntToUInt, UIntToInt, IntToByte,
    BoolToInt, IntToUInt64, UIntToUInt64, UInt64ToInt,
    IntToPtr, PtrToInt, UInt64ToPtr, PtrToUInt64,
    DoubleToUInt64, UInt64ToDouble,
    ToStr,      // 通用转字符串（含 BOOL→true/false、数组/结构体格式化）
    ToInt,      // 运行时通用转 int
}

/// <summary>结构体字段的存储种类（复核结论见 VM2.md §14：native 存储不支持动态数组字段的读取）。</summary>
public enum EcsFieldKind : byte
{
    Scalar = 0,         // 基本类型（含 STRING/PTR）：占 1 个值槽
    FixedArray = 1,     // 固定长度数组：占 count 个值槽（内联）
    NestedStruct = 2,   // 内联结构体：按子布局展开占槽（ext=嵌套类型 sid）
    Boxed = 3,          // 动态数组字段：占 1 个值槽；声明合法但访问在两侧运行时均报错（布局兼容）
}

/// <summary>跳转偏移超界时使用的反转模板，见编码器。</summary>
internal static class EcsJump
{
    public const int NoTarget = -1;
}

/// <summary>
/// 帧内寄存器布局约定（与 native/ecs_vm.h 的 ECS_REG_* 一一对应，双端手工镜像）。
/// 帧模型 = Dalvik/Lua 式每函数栈窗口：帧长 = 函数自身寄存器高水位（EcsFunction.NSlots，
/// ≤ <see cref="MaxRegisters"/>，指令域 u8 是唯一上限——项目增长不再牵动寄存器堆），
/// 调用时在共享值栈上按被调方帧长开窗、返回时弹出。
/// 帧内布局：[R0 = RET][R1–R7 = ARG 实参窗][R8–R23 = STAGING 暂存][R24 起 = 分配区
/// （独占：φ/ForStep/循环携带/编码器临时 → 池区）]。RET/ARG/STAGING 全局固定位置，
/// 分配区随函数伸缩；生命周期分配（SlotAllocator）负责池化与循环活性独占。
/// </summary>
public static class EcsRegisters
{
    /// <summary>寄存器号上限（指令 A/B/C 域为 u8；Lua 同款天花板）。帧长 ≤ 此值。</summary>
    public const int MaxRegisters = 255;

    /// <summary>R0 = RET：无显式接收寄存器调用的返回值固定落点（Call 的 C=0）。</summary>
    public const int Ret = 0;

    /// <summary>R1–R7 = ARG 实参窗：被调方参数由 Call 从调用方 STAGING 深拷贝播种于此
    /// （S-17 语义）；函数/调用实参上限 = <see cref="ArgCount"/>（绑定层强制）。</summary>
    public const int ArgBase = 1;
    public const int ArgCount = 7;

    /// <summary>R8–R23 = STAGING 暂存区（固定位置）：调用出站实参（Move 后 Call 从
    /// R[A..A+B) 取）与数组字面量分块构建（NewArrV 基址）。与参数窗/分配区物理隔离——
    /// 出站 Move 不可能覆写存活值，无重叠 Move 冒险；固定位置使链接期内联的重映射可判定。</summary>
    public const int StagingBase = 8;
    public const int StagingCount = 16;

    /// <summary>R24 起 = 分配区起点：独占区（φ/ForStep/循环携带/编码器临时）→ 池区，
    /// 随函数需求伸缩至 <see cref="MaxRegisters"/>。</summary>
    public const int AllocBase = 24;
}