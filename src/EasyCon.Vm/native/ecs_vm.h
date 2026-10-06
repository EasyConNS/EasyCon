/*
 * ecs_vm.h — VM2 纯 C 精简虚拟机（ECS 字节码，docs/VM2.md）
 *
 * C99，仅依赖 libc（malloc/free/realloc/memcpy/memset/snprintf/strtod/getenv——
 * 仅用于加载期表分配与镜像读入；运行期零分配，见 docs/ZeroAllocVm.md P2/P3）。
 * 嵌入式移植：把本文件与 ecs_vm.c 加入工程，或经宏重定向内存函数。
 * 容器格式：ECSC v2（docs/SingleStreamFormat.md §3）；指令流 = v3 定长编码——
 * 4B（Iabc/ABx/AsBx/IsJ）或 8B（Ext/IabcJ = iABC + u32 数据字），尺寸由 ecs_op_words 给出，
 * 跳转偏移单位 = 字节（目标 = 指令起始 + 本指令字节数 + delta）。
 */
#ifndef ECS_VM_H
#define ECS_VM_H

#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ---- 容器 ABI（与 C# EcsSyscall.AbiRevision / ECSC manifest.abi_rev 对齐） ---- */
#define ECS_ABI_REV 3        /* v3 = 定长指令编码（与 C# EcsSyscall.AbiRevision 对齐） */
#define ECSC_MAGIC  0x43534345u   /* "ECSC" */
#define ECSC_VERSION 2        /* v2 = 定长指令编码（v1 = varint 基流，互不兼容） */
#define ECSC_KIND_IMAGE 1

/* ---- 操作码（与 C# EcsOpcode / docs/VM2.md §4.2 数值与语义一致） ---- */
enum {
    OP_Nop = 0, OP_Halt,
    OP_LoadI, OP_LoadK, OP_LoadBool, OP_Move, OP_SetVar, OP_LoadG, OP_StoreG,
    OP_AddI, OP_SubI, OP_MulI, OP_DivI, OP_ModI, OP_RDivI,
    OP_AddU, OP_SubU, OP_MulU, OP_DivU, OP_ModU,
    OP_AddL, OP_SubL, OP_MulL, OP_DivL, OP_ModL,
    OP_AddD, OP_SubD, OP_MulD, OP_DivD,
    OP_BandI, OP_BorI, OP_BxorI, OP_ShlI, OP_ShrI, OP_BnotI,
    OP_EqI, OP_LtI, OP_LeI, OP_GtI, OP_GeI,
    OP_EqU, OP_LtU, OP_LeU, OP_GtU, OP_GeU,
    OP_EqD, OP_LtD, OP_LeD, OP_GtD, OP_GeD,
    OP_EqL, OP_LtL, OP_LeL, OP_GtL, OP_GeL,
    OP_EqS, OP_EqP,
    OP_Not, OP_NegI, OP_NegD, OP_Conv,
    OP_Jmp, OP_Jpt, OP_Jpf,
    OP_Call, OP_CallN, OP_Ret, OP_Ret0,
    OP_NewArrV, OP_NewArrE, OP_GetI, OP_SetI, OP_Slice, OP_Cont, OP_Append, OP_Cat, OP_Len,
    OP_NewSt, OP_GetF, OP_PutF, OP_GetFI, OP_PutFI,
    OP_WaitI, OP_WaitV, OP_KeyI, OP_KeyV, OP_KeySt, OP_StickSet, OP_StickP, OP_StickPv,
    OP_Img, OP_Rand,
    OP_ForStep,  /* FOR 快速路径：R[A]==R[B] → 落入下一块（出循环）；否则 R[A]+=1 跳 C 目标 */
    OP_CmpJ,     /* 比较跳转融合（P2′）：R[A] kind R[B] 成立 → 跳 Jump；C=kind=typeBlock*6+op
                  *（typeBlock：0=i32,1=u32,2=f64,3=i64；op：0=Eq,1=Neq,2=Lt,3=Le,4=Gt,5=Ge）*/
};

/* ---- 文件族 syscall 编号（uvm32 式编号 ABI，与 C# EcsSyscall 一致，docs/VM2.md §9.1） ----
 * CallN 的 EXT 字旗标置位时，低 31 位 = 编号，直接引擎内建分发，不经原生名表。
 * 文件实现仍转发 host->native（传规范名）；caps 缺失行为与名表时代逐字一致。 */
#define ECS_SYSCALL_FLAG     0x80000000u
/* ECS_SYSCALL_AMIIBO = 16：v2.3 Amiibo 槽位选择（0–19，宿主判越界静默；S-13） */
enum {
    ECS_SYSCALL_FWRITE = 1, ECS_SYSCALL_FREAD,
    ECS_SYSCALL_FOPEN, ECS_SYSCALL_FCLOSE, ECS_SYSCALL_FEOF,
    ECS_SYSCALL_READFILE, ECS_SYSCALL_WRITEFILE, ECS_SYSCALL_APPENDFILE, ECS_SYSCALL_FILE_EXISTS,
    ECS_SYSCALL_ALERT, ECS_SYSCALL_ARG, ECS_SYSCALL_ENV, ECS_SYSCALL_APP,
    ECS_SYSCALL_TIME, ECS_SYSCALL_BEEP, ECS_SYSCALL_AMIIBO, ECS_SYSCALL_OCR_CONF,
};

/* ---- 镜像特征需求掩码（ECX1 头 feats，与 C# EcsImageFeatures 一致，VM2.md §9.1） ----
 * S-21 起为元信息：缺省不拒载，能力缺失按 ecs_cap_*_default 缺省值表降级；
 * host->strict_caps 置位时恢复响亮校验（IL → ECS_ERR_IL，其余缺位 → ECS_ERR_FEAT）。 */
#define ECS_FEAT_IL       0x1u
#define ECS_FEAT_CAPTURE  0x2u
#define ECS_FEAT_FFI      0x4u
#define ECS_FEAT_FILE     0x8u

/* 指令字数（1 = 4B，2 = 8B 带 u32 数据字）——单一事实源，与 C# EcsFormat.WordCount /
 * docs/VM2.md §4.1 对齐（新增指令双端各改一处表 + 执行/发射语义）。数据字是「数据」：
 * EXT 的目标 ID/槽位/时长/坐标，ForStep/CmpJ 的跳转偏移，WaitI/KeyI 的时长——
 * 数值可能恰好等于某个操作码，一切线性扫描必须按字数步进跳过（历史 F4 缺陷铁律）。 */
static inline uint32_t ecs_op_words(uint32_t op)
{
    switch (op) {
    case OP_Call: case OP_CallN: case OP_NewArrV: case OP_Slice:
    case OP_GetFI: case OP_PutFI: case OP_WaitI: case OP_KeyI:
    case OP_StickP: case OP_StickPv: case OP_ForStep: case OP_CmpJ:
        return 2;
    default:
        return 1;
    }
}
#define ECS_OP_WORDS(op) ecs_op_words(op)

/* ---- 值标签（与 docs/VM2.md §3.1 / C# TaggedValue 对齐） ---- */
enum {
    ECS_VOID = 0, ECS_BOOL = 1, ECS_BYTE = 2, ECS_INT = 3, ECS_UINT = 4,
    ECS_UINT64 = 5, ECS_DOUBLE = 6, ECS_STRING = 7, ECS_ARRAY = 9,
    ECS_PTR = 10, ECS_STRUCT = 12,
};

/* 16 字节 tagged value：tag 在 0 偏移，载荷 8 字节对齐 */
typedef struct ecs_value {
    uint8_t  tag;
    uint8_t  _pad[7];
    union {
        int32_t i32;      /* BOOL(0/1) / BYTE / INT / UINT（位模式共用） */
        int64_t i64;      /* UINT64 / PTR / 堆句柄（低 32 位） */
        double  f64;
    };
} ecs_value;

/* ---- 结果码 ---- */
enum {
    ECS_OK = 0, ECS_YIELD = 1, ECS_CANCELLED = 2,
    ECS_ERR_IMAGE = 3, ECS_ERR_OPCODE = 4, ECS_ERR_SLOT = 5, ECS_ERR_TYPE = 6,
    ECS_ERR_INDEX = 7, ECS_ERR_DIVZERO = 8, ECS_ERR_DEPTH = 9,
    ECS_ERR_NOSUCHNATIVE = 10, ECS_ERR_HOST = 11, ECS_ERR_OOM = 12,
    ECS_ERR_IL = 13,            /* 镜像需要图像标签能力（FEAT_IL），本宿主不提供 */
    ECS_ERR_FEAT = 14,          /* 镜像特征需求超出宿主提供（FEAT_CAPTURE/FFI/FILE 缺位） */
    ECS_ERR_SECTION = 15,       /* ECSC 段流非法：critical 段缺失/重复段/SEC_END 后有残余 */
    ECS_ERR_UNSUPPORTED = 16,   /* 未知 critical 段/端序不符/逃生舱段存在/ABI 修订不符 */
    ECS_ERR_CRC = 17,           /* SEC_CRC32 校验失败 */
    ECS_ERR_POOL = 18,          /* 固定池耗尽：帧段满/对象块满/单对象超块（可预期的正常失败） */
};

/* ---- 宿主接口（依赖注入；回调缺失 → L2/L3 按 S-21 缺省值表降级，L1 域操作不降级） ----
 * 分层（docs/VM2.md §1）：L1 域操作回调（wait/key/stick/rand）+ L2 syscall 处理器（编号，
 * 语义在宿主）+ L3 动态原生（按名）。特征掩码 feats = 本宿主提供的高级能力（ECS_FEAT_*）：
 * S-21 起为元信息，缺省不拒载；strict_caps 置位恢复响亮（加载期校验 + miss 即
 * ECS_ERR_NOSUCHNATIVE，图像标签执行期 ECS_ERR_IL）。 */
typedef struct ecs_host {
    void *ud;
    uint32_t feats;             /* 本宿主提供的特征位（ECS_FEAT_*）；strict_caps 时加载期校验用 */
    int strict_caps;            /* S-21：0 = 缺省值降级（缺省）；1 = 恢复响亮拒跑/报错 */
    const char *const *args;    /* ECS_SYSCALL_ARG */
    int32_t nargs;
    const uint16_t *app_dir;    /* ECS_SYSCALL_APP */
    /* L1 核心域操作 */
    void    (*wait_ms)(void *ud, int32_t ms);
    void    (*key)(void *ud, uint8_t key, int32_t dur_ms);           /* 点击 */
    void    (*key_state)(void *ud, uint8_t key, int down);           /* 按住/松开 */
    void    (*stick_set)(void *ud, uint8_t side, int32_t x, int32_t y);
    void    (*stick_click)(void *ud, uint8_t side, int32_t x, int32_t y, int32_t dur_ms);
    int32_t (*rand)(void *ud, int32_t max);                          /* [0, max)；max<=0 → 0 */
    /* L2 平台 syscall（ECS_SYSCALL_*，语义在宿主参考实现；返回 0=成功，非 0=ECS_ERR_NOSUCHNATIVE） */
    int     (*syscall)(void *ud, int32_t id, ecs_value *args, int32_t nargs, ecs_value *ret);
    /* L3 动态原生（采集洞/EXTERN FFI/ENCODE/JQ 按名分发）；返回 0=成功，非 0=ECS_ERR_NOSUCHNATIVE */
    int     (*native)(void *ud, const char *name, ecs_value *args, int32_t nargs, ecs_value *ret);
} ecs_host;


/* ---- VM 实例 ---- */
typedef struct ecs_vm ecs_vm;
/* ---- 宿主访问 VM 数据的唯一通道（syscall 处理器内使用；uvm32 arg_get* 同构） ----
 * 读实参字符串（VM 堆句柄解引用）：返回长度，非串/空串 → 0（越界访问不可能逃逸） */
int32_t ecs_vm_arg_str(ecs_vm *vm, ecs_value v, const uint16_t **units);
/* 分配 VM 堆字符串作为返回值（UTF-16 units；units=NULL 或 len<=0 → 空串） */
void    ecs_vm_ret_str(ecs_vm *vm, ecs_value *ret, const uint16_t *units, int32_t len);

ecs_vm *ecs_vm_new(const ecs_host *host);
/* 加载并校验 ECSC 镜像；image 缓冲区须在 vm 生命周期内保持有效（不拷贝，可 XIP） */
int     ecs_vm_load(ecs_vm *vm, const uint8_t *image, size_t len);
/* 运行：ECS_OK / ECS_YIELD（步数预算耗尽，可再次调用继续）/ 负面错误码 */
int     ecs_vm_run(ecs_vm *vm);
void    ecs_vm_cancel(ecs_vm *vm);
void    ecs_vm_set_budget(ecs_vm *vm, int32_t steps_per_yield);
/* 最近一次错误的现场（函数索引 + 函数内指令下标；与 C# EcxInterpreter.ErrorPc 同单位） */
void    ecs_vm_error_location(const ecs_vm *vm, int32_t *out_func, int32_t *out_pc);
void    ecs_vm_free(ecs_vm *vm);

#ifdef __cplusplus
}
#endif
#endif /* ECS_VM_H */
