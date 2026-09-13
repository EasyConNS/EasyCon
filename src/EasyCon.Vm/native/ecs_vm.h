/*
 * ecs_vm.h — VM2 纯 C 精简虚拟机（ECS 字节码，docs/VM2.md）
 *
 * C99，仅依赖 libc（malloc/free/realloc/memcpy/memset/snprintf/strtod/getenv）。
 * 嵌入式移植：把本文件与 ecs_vm.c 加入工程，或经宏重定向内存函数。
 */
#ifndef ECS_VM_H
#define ECS_VM_H

#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

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
    OP_Img, OP_Rand, OP_Time, OP_Beep, OP_Amiibo,
};

/* 有 32 位 EXT 后随数据字的操作码集合——单一事实源，与 C# EcsFormat 表 / docs/VM2.md §4.1
 * 对齐（新增 EXT 指令双端各改一处表 + 执行/发射语义）。EXT 后随字是「数据」（Call/CallN
 * 的目标 ID、Slice 的 end 槽、StickP/StickPv 的时长/坐标等），其数值可能恰好等于某个操作码：
 * 线性扫描代码必须按本表步进跳过，否则会把数据误读为指令；执行侧读 EXT 前以本表自检。 */
static inline int ecs_op_has_ext(uint32_t op)
{
    switch (op) {
    case OP_Call: case OP_CallN: case OP_NewArrV: case OP_Slice:
    case OP_GetFI: case OP_PutFI: case OP_StickP: case OP_StickPv:
        return 1;
    default:
        return 0;
    }
}
#define ECS_OP_HAS_EXT(op) ecs_op_has_ext(op)

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
    ECS_ERR_IL = 13,            /* 脚本携带图像标签/采集依赖，本平台禁止执行（单片机约束） */
};

/* ---- 平台能力位（MODULE_DESIGN.md §5 平台抽象层模式：
        宿主声明本平台已实现的能力；VM 只提供已实现能力，
        未实现的原生/域操作静默忽略并返回默认值，不报错） ---- */
enum {
    ECS_CAP_PRINT   = 1 << 0,   /* FWRITE stdout / host print */
    ECS_CAP_ALERT   = 1 << 1,
    ECS_CAP_BEEP    = 1 << 2,
    ECS_CAP_FILE    = 1 << 3,   /* FOPEN/FREAD/... 文件句柄族 */
    ECS_CAP_CAPTURE = 1 << 4,   /* __CAPTURE__/__OCR__/__ROI__ 采集卡洞 */
    ECS_CAP_FFI     = 1 << 5,   /* EXTERN FFI */
    ECS_CAP_STDIN   = 1 << 6,   /* FREAD stdin */
};
#define ECS_CAP_ALL_DESKTOP (ECS_CAP_PRINT | ECS_CAP_ALERT | ECS_CAP_BEEP | \
                             ECS_CAP_FILE | ECS_CAP_CAPTURE | ECS_CAP_FFI | ECS_CAP_STDIN)
#define ECS_CAP_ALL_MCU     0   /* 单片机：全部忽略，脚本副作用仅按键/摇杆/延时 */

/* ---- 宿主接口（依赖注入；任何回调可为 NULL → 对应功能 no-op 或报错） ---- */
typedef struct ecs_host {
    void *ud;
    uint32_t caps;              /* 平台能力位，见 ECS_CAP_* */
    const char *const *args;    /* ARG(i) */
    int32_t nargs;
    const uint16_t *app_dir;    /* APP */
    /* 核心域操作 */
    void    (*wait_ms)(void *ud, int32_t ms);
    void    (*key)(void *ud, uint8_t key, int32_t dur_ms);           /* 点击 */
    void    (*key_state)(void *ud, uint8_t key, int down);           /* 按住/松开 */
    void    (*stick_set)(void *ud, uint8_t side, int32_t x, int32_t y);
    void    (*stick_click)(void *ud, uint8_t side, int32_t x, int32_t y, int32_t dur_ms);
    int32_t (*img_label)(void *ud, const uint16_t *name, int32_t units); /* 置信度；无匹配 → -1 */
    int32_t (*rand)(void *ud, int32_t max);                          /* [0, max)；max<=0 → 0 */
    int32_t (*time_ms)(void *ud);
    void    (*beep)(void *ud, int32_t freq, int32_t dur_ms);
    void    (*amiibo)(void *ud, int32_t slot);
    /* 输出/输入（FWRITE stdout 行断协议的落点） */
    void    (*print)(void *ud, const uint16_t *units, int32_t len, int newline);
    int32_t (*read_line)(void *ud, uint16_t *buf, int32_t cap);      /* 返回长度，EOF → -1 */
    /* 文件系统（FOPEN/FREAD/FWRITE/FCLOSE/FEOF 文件句柄转发） */
    void   *(*f_open)(void *ud, const uint16_t *path, const uint16_t *mode);
    int32_t (*f_read)(void *ud, void *h, uint16_t *buf, int32_t cap);
    int32_t (*f_write)(void *ud, void *h, const uint16_t *buf, int32_t len);
    void    (*f_close)(void *ud, void *h);
    int32_t (*f_eof)(void *ud, void *h);
    /* 扩展原生（ENCODE/JQ/READFILE/采集卡洞/EXTERN FFI 等按名分发）；返回 0=成功 */
    int     (*native)(void *ud, const char *name, ecs_value *args, int32_t nargs, ecs_value *ret);
} ecs_host;

/* ---- VM 实例 ---- */
typedef struct ecs_vm ecs_vm;

ecs_vm *ecs_vm_new(const ecs_host *host);
/* 加载并校验镜像；image 缓冲区须在 vm 生命周期内保持有效（不拷贝） */
int     ecs_vm_load(ecs_vm *vm, const uint8_t *image, size_t len);
/* 运行：ECS_OK / ECS_YIELD（步数预算耗尽，可再次调用继续）/ 负面错误码 */
int     ecs_vm_run(ecs_vm *vm);
void    ecs_vm_cancel(ecs_vm *vm);
void    ecs_vm_set_budget(ecs_vm *vm, int32_t steps_per_yield);
/* 最近一次错误的现场（函数索引 + 函数内指令字偏移） */
void    ecs_vm_error_location(const ecs_vm *vm, int32_t *out_func, int32_t *out_pc);
void    ecs_vm_free(ecs_vm *vm);

#ifdef __cplusplus
}
#endif
#endif /* ECS_VM_H */
