namespace EasyCon.Script.Binding.Ssa;

/// <summary>
/// SSA 操作码：类型特化，每个操作的输入输出类型在编译期确定。
/// </summary>
public enum SsaOp : byte
{
    // ---- 常量 ----
    ConstBool, ConstByte, ConstInt, ConstUInt,
    ConstUInt64, ConstDouble, ConstString, ConstPtr,

    // ---- 加载/存储 ----
    LoadLocal,      // Arg0=null, AuxSymbol=LocalVariableSymbol
    StoreLocal,     // Arg0=值, AuxSymbol=LocalVariableSymbol
    LoadGlobal,     // Arg0=null, AuxSymbol=GlobalVariableSymbol
    StoreGlobal,    // Arg0=值, AuxSymbol=GlobalVariableSymbol

    // ---- 算术（int） ----
    AddInt, SubInt, MulInt, DivInt, ModInt, RoundDivInt,
    // ---- 算术（uint） ----
    AddUInt, SubUInt, MulUInt, DivUInt, ModUInt,
    // ---- 算术（double） ----
    AddDouble, SubDouble, MulDouble, DivDouble,
    // ---- 算术（uint64） ----
    AddUInt64, SubUInt64, MulUInt64, DivUInt64, ModUInt64,

    // ---- 位运算 ----
    AndInt, OrInt, XorInt, ShlInt, ShrInt, NotInt,

    // ---- 比较（int） ----
    EqInt, NeqInt, LtInt, LeqInt, GtInt, GeqInt,
    // ---- 比较（uint） ----
    EqUInt, NeqUInt, LtUInt, LeqUInt, GtUInt, GeqUInt,
    // ---- 比较（double） ----
    EqDouble, NeqDouble, LtDouble, LeqDouble, GtDouble, GeqDouble,
    // ---- 比较（uint64） ----
    EqUInt64, NeqUInt64, LtUInt64, LeqUInt64, GtUInt64, GeqUInt64,
    // ---- 比较（bool/string/ptr/byte） ----
    EqBool, NeqBool, EqString, NeqString,
    EqPtr, NeqPtr,
    EqByte, NeqByte, LtByte, LeqByte, GtByte, GeqByte,

    // ---- 逻辑 ----
    LogicNot,

    // ---- 类型转换 ----
    ConvBoolToInt, ConvByteToInt, ConvIntToUInt,
    ConvIntToUInt64, ConvIntToDouble, ConvIntToByte,
    ConvUIntToUInt64, ConvUInt64ToPtr, ConvPtrToInt,
    ConvDoubleToInt, ConvToString,
    ConvUInt64ToInt,
    ConvToInt,      // 运行时通用转换：调用 Value.ToInt()

    // ---- 控制流 ----
    Phi,            // 参数在 SsaBlock.Phis 的 SsaValue.Arg0..ArgN 中
    CondBranch,     // BranchCondition 指向此值
    Branch,         // 无条件跳转
    Return,         // Arg0=返回值（可 null）

    // ---- 调用 ----
    Call,           // AuxSymbol=FunctionSymbol, Arg0=第一个参数

    // ---- 复合数据 ----
    ArrayInit,      // 创建数组，AuxSymbol=元素类型
    LoadIndex,      // Arg0=容器, Arg1=索引
    StoreIndex,     // Arg0=容器, Arg1=索引 (值通过辅助方式传递)
    Slice,          // Arg0=容器, Arg1=start (End通过Arg方式)
    ArrayLen,       // Arg0=容器
    Contains,       // Arg0=元素, Arg1=容器
    Concat,         // Arg0=左, Arg1=右
    ArrayAppend,    // Arg0=数组, Arg1=值, 返回新数组
    DeepCopy,       // Arg0=源值

    // ---- 结构体 ----
    StructInit,     // AuxSymbol=EcsStructDef
    LoadField,      // Arg0=目标, AuxSymbol=EcsFieldDef
    StoreField,     // Arg0=目标, Arg1=值, AuxSymbol=EcsFieldDef
    LoadFieldIndex, // Arg0=目标, Arg1=索引, AuxSymbol=EcsFieldDef
    StoreFieldIndex,// Arg0=目标, Arg1=索引 (值通过辅助方式)

    // ---- 领域操作（原子指令） ----
    KeyAction,
    KeyPress,       // AuxSymbol=GamePadKey, Arg0=duration
    StickAction,
    StickPress,     // Arg0=duration
    Wait,           // Arg0=duration

    // ---- 采集卡打洞函数 ----
    Capture,        // Arg0=x, ExtraArgs=[y, w, h]
    Ocr,            // Arg0=x, ExtraArgs=[y, w, h, lang]
    Roi,            // Arg0=image, ExtraArgs=[x, y, w, h]

    // ---- 运行时 ----
    RuntimeValue,   // AuxSymbol 存 name string（特殊用法）
    ImageLabel,     // AuxSymbol 存 label name

    Nop,
}