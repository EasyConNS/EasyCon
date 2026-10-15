/*
 * ecs_vm.c — VM2 纯 C 精简虚拟机实现——双端语义一致性由 corpus/FullChain 对拍测试锁定。
 *
 * 语义基准：EcxInterpreter（C# 模拟解释器，逐条对照；含 .NET 总序比较/饱和转换/无符号回绕）。
 * 值模型：8 字节标记值（ecs_vm.h）——宽标量 DOUBLE/UINT64/PTR 统一装箱为对象池 8B 标量块
 * （OBJ_KIND_BOX，参与引用计数）；数组内宽元素仍 8B 裸存（装箱只存在于值槽）；
 * 数组宽元素读边界产箱（可 POOL）、写边界解箱（不分配）。
 * 内部五区段：镜像解析（§4.1，ECSC 容器）/ 堆（§4.2，固定对象池）/ 解释器主循环（§4.3，
 * v3 定长取指：4B/8B 定宽指令直接从镜像读取（XIP），取指通路局部化）/
 * 结构体布局（§4.4）/ 核心原生表（§4.5）。
 * C99，仅依赖 libc + libm；malloc 族仅用于**加载期**表分配（运行期零分配——
 * docs/ZeroAllocVm.md P2 帧池 + P3 对象池；加载期表入池为 P3c，暂缓）。
 */
#include "ecs_vm.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <math.h>
#include <inttypes.h>

#ifndef ECS_MAX_CALL_DEPTH
#define ECS_MAX_CALL_DEPTH 64   /* 与 C# EcxInterpreter.MaxCallDepth 对齐（对拍分裂防线） */
#endif

/* ---- 零分配固定池（docs/ZeroAllocVm.md §3/§4；固件常量，参考宿主取宽裕值） ----
 * D-C：单一 arena 切两段（帧段 + 对象段），两段独立判定满溢（ECS_ERR_POOL 分别归因）。
 * 参考宿主帧段须容纳 ECS_MAX_CALL_DEPTH 层典型帧（深递归对拍要求 DEPTH 先于 POOL 触发）；
 * 真实固件按 RAM 调小，深度上界随之由帧段容量自然收窄。 */
#ifndef ECS_ARENA_BYTES
#define ECS_ARENA_BYTES (256 * 1024)
#endif
#ifndef ECS_FRAME_SEG_BYTES
#define ECS_FRAME_SEG_BYTES (ECS_ARENA_BYTES / 2)
#endif
#define ECS_OBJ_SEG_BYTES (ECS_ARENA_BYTES - ECS_FRAME_SEG_BYTES)
#ifndef ECS_OBJ_BLOCK_BYTES
#define ECS_OBJ_BLOCK_BYTES 128
#endif
#ifndef ECS_OBJ_BLOCK_COUNT
#define ECS_OBJ_BLOCK_COUNT (ECS_OBJ_SEG_BYTES / ECS_OBJ_BLOCK_BYTES)
#endif
#ifndef ECS_MAX_FRAMES
#define ECS_MAX_FRAMES (ECS_FRAME_SEG_BYTES / 64)
#endif
/* 宿主容量档案（R-3：容量属宿主不属产物；加载期按档案校验每函数槽位） */
#ifndef ECS_MAX_SLOTS
#define ECS_MAX_SLOTS 255
#endif

#define ECS_DEFAULT_BUDGET 1000000
/* 接收槽恒为寄存器号（无哨兵值） */
#define ECS_IMPORT_FLAG 0x80000000u
#define FIELD_KIND_FIXED_ARRAY 1
#define FIELD_KIND_NESTED 2

/* TOSTR 工作缓冲上限（有界拒绝：超大嵌套结构转串 → ECS_ERR_POOL，不做块链） */
#define ECS_TOSTR_MAX 512

/* Conv 种类（与 C# EcsConvKind 数值一致） */
enum {
    CV_INT_TO_DOUBLE = 0, CV_DOUBLE_TO_INT, CV_INT_TO_UINT, CV_UINT_TO_INT, CV_INT_TO_BYTE,
    CV_BOOL_TO_INT, CV_INT_TO_UINT64, CV_UINT_TO_UINT64, CV_UINT64_TO_INT,
    CV_INT_TO_PTR, CV_PTR_TO_INT, CV_UINT64_TO_PTR, CV_PTR_TO_UINT64,
    CV_DOUBLE_TO_UINT64, CV_UINT64_TO_DOUBLE,
    CV_TOSTR, CV_TOINT,
};

/* ============================================================
 * 内部类型
 * ============================================================ */

typedef struct ecs_obj ecs_obj;
typedef struct ecs_structdef ecs_structdef;

/* ECSC 段 id（与 C# EcsContainer 段注册表一致，docs/SingleStreamFormat.md §3.3） */
enum {
    SEC_END = 0x0000, SEC_MANIFEST = 0x0001, SEC_CONSTANTS = 0x0002, SEC_STRUCTS = 0x0003,
    SEC_GLOBALS = 0x0004, SEC_NATIVES = 0x0005, SEC_FUNCTIONS = 0x0006, SEC_CODE = 0x0007,
    SEC_LINES = 0x0008, SEC_DEBUG_NAMES = 0x0009, SEC_IMPORTS = 0x0010, SEC_EXPORTS = 0x0011,
    SEC_ILNAMES = 0x0012, SEC_IFACE = 0x0013, SEC_LINKFLAGS = 0x0014,
    SEC_OPERAND_FIXUPS = 0x0020, SEC_CRC32 = 0x0030,
};
#define SEC_FLAG_CRITICAL 0x01

/* 对象头（24 B，8 字节对齐；payload 紧随）。单块 = 一对象（单块上限见 §4.2）。
   flags：bit0 = in use；bits1..7 = 元素/标量 tag（数组 D-B 定宽解包、BOX 标量类型依据）。
   sid：结构体对象 = 类型表下标；结构体元素数组的元素宽度来源（NewArrV 无 sid → 0 = 定宽回退）。 */
typedef struct {
    uint8_t  kind;               /* ECS_STRING / ECS_ARRAY / ECS_STRUCT / OBJ_KIND_BOX */
    uint8_t  flags;
    uint16_t len;                /* 字符串 units 数 / 数组元素数 */
    int16_t  rc;
    int16_t  sid;
    int32_t  next_free;          /* 空闲链（in use 时未用） */
    int32_t  view_parent;        /* 嵌套视图：父结构体句柄（0=非视图） */
    int32_t  view_offset;        /* 嵌套视图：本视图槽区在父槽区的起始偏移 */
} obj_hdr;

#define OBJ_HDR_BYTES 24
#define OBJ_PAYLOAD(o) ((uint8_t *)(o) + OBJ_HDR_BYTES)   /* 对象载荷 = 块内紧跟对象头 */
#define OBJ_PAYLOAD_BYTES (ECS_OBJ_BLOCK_BYTES - OBJ_HDR_BYTES)
#define OBJ_IN_USE 0x01u
#define OBJ_ETAG(f) (uint8_t)((f) >> 1)
#define OBJ_SET_ETAG(f, t) (uint8_t)(((f) & OBJ_IN_USE) | ((t) << 1))
#define OBJ_KIND_BOX 0x7Fu           /* 装箱宽标量块（payload = 8B 位模式，无子引用） */

/* C99 编译期布局/容量约束：不能把未对齐的帧或对象解释为含 64 位字段的对象。 */
typedef char ecs_value_size_check[sizeof(ecs_value) == 8 ? 1 : -1];
typedef char ecs_arena_layout_check[(ECS_FRAME_SEG_BYTES > 0 &&
    ECS_FRAME_SEG_BYTES <= ECS_ARENA_BYTES && ECS_FRAME_SEG_BYTES % 8 == 0 &&
    ECS_OBJ_BLOCK_BYTES >= OBJ_HDR_BYTES + 8 && ECS_OBJ_BLOCK_BYTES % 8 == 0) ? 1 : -1];

struct ecs_obj {
    obj_hdr h;
    /* payload：STRING = uint16 units[len]；ARRAY = 按 etag 定宽元素；STRUCT = nslots × ecs_value；
     * BOX = 8B 标量位模式（DOUBLE/UINT64/PTR） */
};

typedef struct {
    char *name;                   /* 调试名（调试段剥离时为 NULL） */
    uint8_t nparams, hasret;
    uint16_t nslots;
    const uint8_t *code;          /* v3 定长指令流：指向镜像缓冲（零拷贝，XIP 原地取指） */
    uint32_t code_bytes;
    uint32_t code_off;            /* 相对 SEC_CODE 起点（= 入口字节地址） */
} ecs_funcdef;

typedef struct {
    char *name;
    uint8_t kind, type, elem;
    uint16_t ext;                 /* FixedArray=元素数；NestedStruct=嵌套 sid */
    int32_t slot_offset;
} ecs_fielddef;

struct ecs_structdef {
    char *name;
    int32_t nfields;
    ecs_fielddef *fields;
    int32_t nslots;               /* 加载期展开 */
};

typedef struct {
    uint8_t tag;
    int64_t i64;
    double f64;
    const uint16_t *units;        /* 字符串：指向镜像缓冲（零拷贝；S-20 pinned 的字符数据源） */
    int32_t units_len;
} const_ent;

typedef struct {
    const ecs_funcdef *fn;
    uint32_t ret_pc;              /* 续跑点 = 函数内字节偏移（定长取指；仅调用边界与让出点写回） */
    uint32_t ret_idx;             /* 续跑点 = 指令下标（error_pc 诊断单位，与 ret_pc 同点写回） */
    uint16_t func_index;          /* 诊断 / 函数表下标（镜像 func_count 为 u16，< 64K 函数） */
    uint8_t  ret_slot;            /* 接收寄存器（恒 ≤ 255 = 指令域上限；0 = RET 固定落点） */
    uint8_t  _pad;
    ecs_value slots[];
} frame;                          /* 帧字节数由 fn->nslots 派生（frame_size），不入帧头 */

/* 帧字节尺寸（8 字节对齐）：push 分配与 pop 回退的唯一消费者——派生消除即帧头省一个 u32 */
static size_t frame_size(const ecs_funcdef *fn)
{
    return (sizeof(frame) + (size_t)fn->nslots * sizeof(ecs_value) + 7u) & ~(size_t)7u;
}

struct ecs_vm {
    ecs_host host;

    const uint8_t *img;           /* 借用或 io 拉取的内部副本（owned_img=1 时 ecs_vm_free 释放） */
    size_t img_len;
    int owned_img;                /* ecs_vm_load_io 拉取路径：镜像缓冲归 VM 所有 */
    const_ent *consts;            int32_t nconsts;
    ecs_structdef *structs;       int32_t nstructs;
    ecs_value *globals;           int32_t nglobals;
    char **natives;               int32_t nnatives;
    ecs_funcdef *funcs;           int32_t nfuncs;
    int32_t entry;

    /* —— 零分配运行时（D-C 单一 arena 两段）—— */
    uint8_t arena[ECS_ARENA_BYTES];
    size_t frame_sp;              /* 帧段 bump 水位（调用/返回严格嵌套，弹帧即回退） */
    uint32_t frame_off[ECS_MAX_FRAMES];   /* 帧表：arena 内偏移（u32 覆盖 PC 256KB arena） */
    int32_t depth;
    int32_t free_head;            /* 对象段空闲块链头（-1 = 空） */

    int32_t steps, budget;
    int cancel_flag;
    int wait_yield;               /* 置位后 WAIT 变让出点：通知宿主时长后返回 ECS_WAITING（固件 tick 模型） */
    int host_error;               /* ret_* 分配失败：CallN 优先传播，不降级为合法空值 */
    int32_t error_func, error_pc;
    int pending_break;            /* FWRITE 行断协议状态（S-14） */
};

/* 帧表回查：frames 表存 arena 偏移（u32），帧指针按需还原 */
static frame *frame_at(ecs_vm *vm, uint32_t off)
{
    return (frame *)(vm->arena + off);
}

/* ============================================================
 * 工具
 * ============================================================ */

/* .NET double.CompareTo 总序：NaN 最大且互等；-0.0 < +0.0（CmpD/LtD 等的语义基准） */
static int cmp_double(double a, double b)
{
    if (isnan(a) && isnan(b)) return 0;
    if (isnan(a)) return 1;
    if (isnan(b)) return -1;
    if (a < b) return -1;
    if (a > b) return 1;
    if (a == b) return 0;
    if (signbit(a)) return signbit(b) ? 0 : -1;
    return 1;
}

static ecs_value void_value(void)
{
    ecs_value v; memset(&v, 0, sizeof(v));
    return v;
}

static ecs_value make_bool(int b)
{
    ecs_value v = void_value(); v.tag = ECS_BOOL; v.i32 = b ? 1 : 0; return v;
}

static ecs_value make_int(int32_t x)
{
    ecs_value v = void_value(); v.tag = ECS_INT; v.i32 = x; return v;
}

/* 容器引用：值槽持句柄、数组元素为 4B 句柄裸存（引用计数对象） */
static int is_ref_tag(uint8_t tag)
{
    return tag == ECS_STRING || tag == ECS_ARRAY || tag == ECS_STRUCT;
}

/* 装箱宽标量：值槽持对象池标量块句柄，随引用计数管理；
 * 数组内宽元素是 8B 裸存、不属于本类（见 arr_get/arr_overwrite 边界）。 */
static int is_boxed_tag(uint8_t tag)
{
    return tag == ECS_DOUBLE || tag == ECS_UINT64 || tag == ECS_PTR;
}

/* 引用计数派发轴（retain/release/store_fresh/move_to/弹帧/兜底 sweep）：容器引用 + 装箱宽标量 */
static int is_heap_tag(uint8_t tag)
{
    return is_ref_tag(tag) || is_boxed_tag(tag);
}

/* 静态（pinned）字符串（契约 S-20 / docs/ZeroAllocVm.md §2）：ECS_STRING 的句柄 bit31 置位时，
   低 31 位是常量池索引——字符数据直接引用镜像缓冲（const_ent.units 已零拷贝指向镜像），
   该字符串是唯一实例、内容不可变、**不参与引用计数**（retain/release/弹帧对其为 no-op）。
   编码天然安全：静态句柄在 int32 下为负，一切 `handle <= 0` 守卫都会拒绝它，
   因此漏改点会「响亮失败」（heap_obj 返回 NULL）而不是读错内存。 */
#define ECS_HANDLE_STATIC 0x80000000u

static int is_static_str(ecs_value v)
{
    return v.tag == ECS_STRING && (v.u32 & ECS_HANDLE_STATIC) != 0;
}

static int32_t static_str_index(ecs_value v)
{
    return (int32_t)(v.u32 & ~ECS_HANDLE_STATIC);
}

static ecs_value make_static_str(int32_t kx)
{
    ecs_value v = void_value();
    v.tag = ECS_STRING;
    v.u32 = ECS_HANDLE_STATIC | (uint32_t)kx;
    return v;
}

/* v3 定长编码：无变长解码原语（varint 基流随 ECSC v1 退役）。 */

static uint16_t rd_u16(const uint8_t *p)
{
    return (uint16_t)((uint32_t)p[0] | (uint32_t)p[1] << 8);
}

static uint32_t rd_u32(const uint8_t *p)
{
    return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24;
}

/* ============================================================
 * §4.2 堆 —— 固定对象池（单块 = 一对象，超限拒绝；docs/ZeroAllocVm.md §4）
 * ============================================================ */

static ecs_obj *obj_at(ecs_vm *vm, int32_t handle)
{
    if (handle <= 0 || handle > ECS_OBJ_BLOCK_COUNT) return NULL;
    return (ecs_obj *)(vm->arena + ECS_FRAME_SEG_BYTES + (size_t)(handle - 1) * ECS_OBJ_BLOCK_BYTES);
}

/* handle <= 0 同时拒掉 0 与静态（pinned）句柄：静态句柄 bit31 置位 ⇒ int32 负数，
   且静态字符串不占对象池（见 is_static_str 注释）。 */
static ecs_obj *heap_obj(ecs_vm *vm, int32_t handle)
{
    ecs_obj *o = obj_at(vm, handle);
    if (!o || !(o->h.flags & OBJ_IN_USE)) return NULL;
    return o;
}

/* 从空闲链取一块；链空 = 对象块耗尽 → -1（调用方报 ECS_ERR_POOL） */
static int32_t pool_alloc(ecs_vm *vm)
{
    int32_t h = vm->free_head;
    if (h < 0)
    {
#ifdef ECS_POOL_DEBUG
        int live = 0;
        for (int32_t i = 1; i <= ECS_OBJ_BLOCK_COUNT; i++)
        {
            ecs_obj *o = obj_at(vm, i);
            if (o->h.flags & OBJ_IN_USE) { live++; fprintf(stderr, "[pool] live h=%d kind=%d rc=%d len=%u\n", i, o->h.kind, o->h.rc, o->h.len); }
        }
        fprintf(stderr, "[pool] exhausted, live=%d\n", live);
#endif
        return -1;
    }
    ecs_obj *o = obj_at(vm, h);
    vm->free_head = o->h.next_free;
    memset(o, 0, ECS_OBJ_BLOCK_BYTES);
    o->h.flags = OBJ_IN_USE;
    o->h.rc = 1;
    return h;
}

static void pool_free(ecs_vm *vm, int32_t handle)
{
    ecs_obj *o = obj_at(vm, handle);
    o->h.flags = 0;
    o->h.next_free = vm->free_head;
    vm->free_head = handle;
}

static void pool_init(ecs_vm *vm)
{
    vm->free_head = -1;
    for (int32_t i = ECS_OBJ_BLOCK_COUNT; i >= 1; i--)
    {
        ecs_obj *o = obj_at(vm, i);
        o->h.next_free = vm->free_head;
        vm->free_head = i;
    }
}

/* 字符串：2 B/unit；超出单块 → 拒绝（ECS_ERR_POOL） */
static int32_t hnew_str(ecs_vm *vm, int32_t len)
{
    if (len < 0 || (size_t)len * 2 > OBJ_PAYLOAD_BYTES) return -1;
    int32_t h = pool_alloc(vm);
    if (h < 0) return -1;
    ecs_obj *o = obj_at(vm, h);
    o->h.kind = ECS_STRING;
    o->h.len = (uint16_t)len;
    return h;
}

static ecs_value new_str_units(ecs_vm *vm, const uint16_t *units, int32_t len)
{
    ecs_value v = void_value();
    int32_t h = hnew_str(vm, len);
    if (h < 0) return v;
    ecs_obj *o = obj_at(vm, h);
    if (len > 0 && units) memcpy(OBJ_PAYLOAD(o), units, (size_t)len * sizeof(uint16_t));
    v.tag = ECS_STRING;
    v.u32 = (uint32_t)h;
    return v;
}

/* ---- 装箱宽标量（DOUBLE/UINT64/PTR）：对象池 8B 标量块，值持块句柄 ----
 * 参与引用计数（retain/release 经 is_heap_tag 与容器同路）；块耗尽 *err=1 → 调用方报
 * ECS_ERR_POOL（LoadK/Conv/宽算术/宽元素读四类原本无失败路径的指令自此有界失败）。 */

static ecs_value box_make(ecs_vm *vm, uint8_t tag, uint64_t bits, int *err)
{
    ecs_value v = void_value();
    *err = 0;
    int32_t h = pool_alloc(vm);
    if (h < 0) { *err = 1; return v; }
    ecs_obj *o = obj_at(vm, h);
    o->h.kind = OBJ_KIND_BOX;
    o->h.flags = OBJ_SET_ETAG(OBJ_IN_USE, tag);
    memcpy(OBJ_PAYLOAD(o), &bits, 8);
    v.tag = tag;
    v.u32 = (uint32_t)h;
    return v;
}

static ecs_value box_double(ecs_vm *vm, double d, int *err)
{
    uint64_t bits;
    memcpy(&bits, &d, 8);
    return box_make(vm, ECS_DOUBLE, bits, err);
}

static ecs_value box_u64(ecs_vm *vm, int64_t x, int *err)
{
    return box_make(vm, ECS_UINT64, (uint64_t)x, err);
}

static ecs_value box_ptr(ecs_vm *vm, int64_t x, int *err)
{
    return box_make(vm, ECS_PTR, (uint64_t)x, err);
}

/* 解箱读：信任值 tag（坏句柄/错 kind → 0，与字节码同一信任面） */
static uint64_t box_bits(ecs_vm *vm, ecs_value v)
{
    ecs_obj *o = heap_obj(vm, (int32_t)v.u32);
    if (!o || o->h.kind != OBJ_KIND_BOX) return 0;
    uint64_t bits;
    memcpy(&bits, OBJ_PAYLOAD(o), 8);
    return bits;
}

static double box_d(ecs_vm *vm, ecs_value v)
{
    uint64_t bits = box_bits(vm, v);
    double d;
    memcpy(&d, &bits, 8);
    return d;
}

static int64_t box_l(ecs_vm *vm, ecs_value v)
{
    return (int64_t)box_bits(vm, v);
}

/* 数组元素定宽（D-B）：4B 标量/句柄、8B 宽类型（裸存，不装箱）、结构体元素 = nslots × ecs_value。
   返回 0 = 单元素字节数；*total = len 元素总载荷；超块返回 -1。 */
static int32_t arr_elem_bytes(const ecs_vm *vm, uint8_t etag, int32_t sid)
{
    switch (etag)
    {
        case ECS_UINT64: case ECS_DOUBLE: case ECS_PTR: return 8;
        case ECS_STRUCT:
            if (sid > 0 && sid < vm->nstructs)
                return vm->structs[sid].nslots * (int32_t)sizeof(ecs_value);
            return (int32_t)sizeof(ecs_value);
        default: return 4;   /* BOOL/BYTE/INT/UINT + 句柄(STRING/ARRAY) */
    }
}

static int32_t hnew_arr(ecs_vm *vm, uint8_t etag, int32_t sid, int32_t len)
{
    if (len < 0 || len > 0xFFFF) return -1;
    int32_t eb = arr_elem_bytes(vm, etag, sid);
    if (eb < 0 || (size_t)len * (size_t)eb > OBJ_PAYLOAD_BYTES) return -1;
    int32_t h = pool_alloc(vm);
    if (h < 0) return -1;
    ecs_obj *o = obj_at(vm, h);
    o->h.kind = ECS_ARRAY;
    o->h.flags = OBJ_SET_ETAG(OBJ_IN_USE, etag);
    o->h.sid = (int16_t)sid;
    o->h.len = (uint16_t)len;
    return h;
}

static int32_t hnew_st(ecs_vm *vm, const ecs_structdef *def)
{
    if ((size_t)def->nslots * sizeof(ecs_value) > OBJ_PAYLOAD_BYTES) return -1;   /* 结构体尺寸编译期可知，超块即拒 */
    int32_t h = pool_alloc(vm);
    if (h < 0) return -1;
    ecs_obj *o = obj_at(vm, h);
    o->h.kind = ECS_STRUCT;
    o->h.sid = (int16_t)(def - vm->structs);
    /* 全零位 = 各类型零值（+0.0 / 句柄 0(null 串) / 整型 0）≈ 参考实现 ZeroFill（pool_alloc 已清零） */
    return h;
}

static void release(ecs_vm *vm, ecs_value v);

/* 数组元素句柄遍历（按元素定宽解包；结构体元素再走槽区） */
static void arr_foreach_handle(ecs_vm *vm, ecs_obj *o, int is_release)
{
    uint8_t et = OBJ_ETAG(o->h.flags);
    int32_t n = o->h.len;
    if (et == ECS_STRUCT)
    {
        int32_t eb = arr_elem_bytes(vm, et, o->h.sid);
        uint8_t *p = OBJ_PAYLOAD(o);
        for (int32_t i = 0; i < n; i++)
        {
            ecs_value *slots = (ecs_value *)(p + (size_t)i * eb);
            const ecs_structdef *def = &vm->structs[o->h.sid];
            for (int32_t k = 0; k < def->nslots; k++)
            {
                if (is_release) release(vm, slots[k]);
            }
        }
        return;
    }
    if (!is_ref_tag(et)) return;
    uint8_t *p = OBJ_PAYLOAD(o);
    for (int32_t i = 0; i < n; i++)
    {
        int32_t h;
        memcpy(&h, p + (size_t)i * 4, 4);
        ecs_value v = void_value();
        v.tag = et;
        v.u32 = (uint32_t)h;
        if (is_release) release(vm, v);
    }
}

static void retain(ecs_vm *vm, ecs_value v)
{
    if (!is_heap_tag(v.tag)) return;
    if (is_static_str(v)) return;   /* S-20：静态值不参与 RC（heap_obj 亦拒负句柄，此处显式化契约） */
    ecs_obj *o = heap_obj(vm, (int32_t)v.u32);
    if (o && o->h.rc < INT16_MAX) o->h.rc++;
}

static void release(ecs_vm *vm, ecs_value v)
{
    if (!is_heap_tag(v.tag)) return;
    if (is_static_str(v)) return;   /* S-20：静态值不参与 RC —— 弹帧/覆写/容器释放对其均为 no-op */
    int32_t h = (int32_t)v.u32;
    ecs_obj *o = heap_obj(vm, h);
    if (!o) return;
    if (--o->h.rc > 0) return;
    switch (o->h.kind)
    {
        case ECS_STRING: break;   /* payload 随块回收 */
        case OBJ_KIND_BOX: break; /* 装箱宽标量：8B 位模式，无子引用 */
        case ECS_ARRAY: arr_foreach_handle(vm, o, 1); break;
        case ECS_STRUCT:
        {
            const ecs_structdef *def = &vm->structs[o->h.sid];
            ecs_value *slots = (ecs_value *)OBJ_PAYLOAD(o);
            for (int32_t i = 0; i < def->nslots; i++)
                release(vm, slots[i]);
            if (o->h.view_parent)
            {
                ecs_value parent = void_value();
                parent.tag = ECS_STRUCT;
                parent.u32 = (uint32_t)o->h.view_parent;
                release(vm, parent);   /* 归还视图持有的父引用 */
            }
            break;
        }
    }
    pool_free(vm, h);
}

static void move_to(ecs_vm *vm, ecs_value *slot, ecs_value v)
{
    /* 同柄自赋值 no-op（对齐 EcxInterpreter.MoveToSlot 槽写规则）：先释放会悬垂——
       槽位已持有该引用，rc 减到 0 会把仍被本槽持有的块打回空闲链 */
    if (is_heap_tag(v.tag) && slot->tag == v.tag && slot->u32 == v.u32)
        return;
    /* 标量快径：release/retain 对标量本为 no-op，用 tag 判别免掉函数调用。
       注意本体不能再拆「慢路径辅助函数」——函数体内一含调用，clang 会对整个
       move_to 拒绝内联（实测回退 20%+），标量热路径必须留在本体内联展开。 */
    if (is_heap_tag(v.tag))
        retain(vm, v);
    /* v 可能是旧槽容器中的子值；先 retain，再释放旧容器，避免子值被提前回收。 */
    if (is_heap_tag(slot->tag))
        release(vm, *slot);
    *slot = v;
}

/* 槽写慢路径（旧值为句柄才走）：release 调用收拢在此。store_fresh 用 always_inline
   强制展开（clang 按调用点×代码尺寸启发式不肯内联叶子，实测采样 ~15-18% 纯调用
   开销；GCC/Clang 族工具链均支持该属性，其余编译器自动退化为普通静态函数）。 */
#if defined(__GNUC__) || defined(__clang__)
#define ECS_STORE_FRESH_INLINE __attribute__((always_inline))
#else
#define ECS_STORE_FRESH_INLINE
#endif

static void store_fresh_slow(ecs_vm *vm, ecs_value *slot, ecs_value v)
{
    release(vm, *slot);
    *slot = v;
}

/* 槽位写入「出生引用」值（新建对象/标量/深拷贝结果）：仅释放旧值、不 retain——
   出生引用即本槽位的引用（对齐 EcxInterpreter.StoreFresh）。 */
ECS_STORE_FRESH_INLINE static inline void store_fresh(ecs_vm *vm, ecs_value *slot, ecs_value v)
{
    if (!is_heap_tag(slot->tag))   /* 标量旧值：免调用快径 */
    {
        *slot = v;
        return;
    }
    store_fresh_slow(vm, slot, v);
}

/* 嵌套视图写穿透（F3 后续项，对齐 EcxInterpreter.WriteStructSlot 的 ViewParent 链回写）：
   视图槽区是父槽切片的拷贝，槽写入后逐级回写父槽区；视图单向持有父引用，无环 */
static void sync_view(ecs_vm *vm, ecs_obj *obj)
{
    while (obj->h.view_parent)
    {
        ecs_obj *parent = heap_obj(vm, obj->h.view_parent);
        if (!parent) break;
        const ecs_structdef *def = &vm->structs[obj->h.sid];
        ecs_value *slots = (ecs_value *)OBJ_PAYLOAD(obj);
        ecs_value *pslots = (ecs_value *)OBJ_PAYLOAD(parent);
        for (int32_t i = 0; i < def->nslots; i++)
        {
            ecs_value *pslot = &pslots[obj->h.view_offset + i];
            release(vm, *pslot);
            *pslot = slots[i];
            retain(vm, *pslot);
        }
        obj = parent;
    }
}

/* 数组元素打包读（D-B）：按 etag 定宽解包为 ecs_value。
 * 宽元素为块内 8B 裸存，读边界装箱产出（出生引用；块耗尽 *err=1，调用方报 POOL）。 */
static ecs_value arr_get(ecs_vm *vm, const ecs_obj *o, int32_t i, int *err)
{
    uint8_t et = OBJ_ETAG(o->h.flags);
    uint8_t *p = OBJ_PAYLOAD((ecs_obj *)o) + (size_t)i * (size_t)arr_elem_bytes(vm, et, o->h.sid);
    ecs_value v = void_value();
    *err = 0;
    switch (et)
    {
        case ECS_UINT64: case ECS_PTR:
        case ECS_DOUBLE:
        {
            uint64_t bits;
            memcpy(&bits, p, 8);
            v = box_make(vm, et, bits, err);
            break;
        }
        case ECS_STRUCT:
            memcpy(&v, p, sizeof(ecs_value));
            break;
        case ECS_STRING: case ECS_ARRAY:
        {
            int32_t h;
            memcpy(&h, p, 4);
            v.tag = et;
            v.u32 = (uint32_t)h;   /* 句柄 int32 符号扩展往返（静态串 bit31 负句柄原样往返） */
            break;
        }
        default:
            v.tag = et;
            memcpy(&v.i32, p, 4);
            break;
    }
    return v;
}

/* 数组元素覆写（SetI 语义）：旧元素释放 + 打包写入 + 容器引用 retain。
 * 旧元素按 etag 就地判定（不经 arr_get，避免宽元素装箱分配）；宽新值在写边界解箱为
 * 8B 裸存（箱的生命期属于值槽，数组不持箱引用 → 不 retain）。本函数不分配、不失败。 */
static void arr_overwrite(ecs_vm *vm, ecs_obj *o, int32_t i, ecs_value v)
{
    uint8_t et = OBJ_ETAG(o->h.flags);
    uint8_t *p = OBJ_PAYLOAD(o) + (size_t)i * (size_t)arr_elem_bytes(vm, et, o->h.sid);
    /* 旧元素释放（与 release 同规则） */
    if (et == ECS_STRUCT)
    {
        const ecs_structdef *def = &vm->structs[o->h.sid];
        ecs_value *old_slots = (ecs_value *)p;
        for (int32_t k = 0; k < def->nslots; k++)
            release(vm, old_slots[k]);
    }
    else if (et == ECS_STRING || et == ECS_ARRAY)
    {
        int32_t h;
        memcpy(&h, p, 4);
        ecs_value old = void_value();
        old.tag = et;
        old.u32 = (uint32_t)h;
        release(vm, old);
    }
    switch (v.tag)
    {
        case ECS_UINT64: case ECS_PTR:
        case ECS_DOUBLE:
        {
            uint64_t bits = box_bits(vm, v);
            memcpy(p, &bits, 8);
            break;
        }
        case ECS_STRUCT:
            memcpy(p, &v, sizeof(ecs_value));
            break;
        case ECS_STRING: case ECS_ARRAY:
        {
            int32_t h = (int32_t)v.u32;
            memcpy(p, &h, 4);
            break;
        }
        default:
            memcpy(p, &v.i32, 4);
            break;
    }
    if (is_ref_tag(v.tag))
        retain(vm, v);
}

/* 数组内部复制宽元素直接搬运 8B 位模式，无需产生中间箱；引用元素维持原 retain 规则。 */
static void arr_copy_item(ecs_vm *vm, ecs_obj *dst, int32_t di, const ecs_obj *src, int32_t si)
{
    uint8_t et = OBJ_ETAG(src->h.flags);
    if (is_boxed_tag(et))
    {
        memcpy(OBJ_PAYLOAD(dst) + (size_t)di * 8, OBJ_PAYLOAD(src) + (size_t)si * 8, 8);
        return;
    }
    int err;
    ecs_value item = arr_get(vm, src, si, &err);
    (void)err;   /* 非宽元素不分配，不可能耗尽对象块。 */
    arr_overwrite(vm, dst, di, item);
}

/* S-01：字符串共享；数组/结构体一层新容器 + 子项 retain */
static ecs_value deep_copy(ecs_vm *vm, ecs_value v)
{
    if (v.tag == ECS_ARRAY)
    {
        ecs_obj *src = heap_obj(vm, (int32_t)v.u32);
        if (!src) return void_value();
        uint8_t et = OBJ_ETAG(src->h.flags);
        int32_t h = hnew_arr(vm, et, src->h.sid, src->h.len);
        if (h < 0) return void_value();
        ecs_obj *dst = obj_at(vm, h);
        if (et == ECS_STRUCT)
        {
            int32_t eb = arr_elem_bytes(vm, et, src->h.sid);
            const ecs_structdef *def = &vm->structs[src->h.sid];
            for (int32_t i = 0; i < src->h.len; i++)
            {
                ecs_value *dst_slots = (ecs_value *)(OBJ_PAYLOAD(dst) + (size_t)i * eb);
                ecs_value *src_slots = (ecs_value *)(OBJ_PAYLOAD(src) + (size_t)i * eb);
                memcpy(dst_slots, src_slots, (size_t)def->nslots * sizeof(ecs_value));
                for (int32_t k = 0; k < def->nslots; k++)
                    retain(vm, dst_slots[k]);
            }
        }
        else
        {
            int32_t eb = arr_elem_bytes(vm, et, src->h.sid);
            memcpy(OBJ_PAYLOAD(dst), OBJ_PAYLOAD(src), (size_t)src->h.len * (size_t)eb);
            /* 容器句柄元素逐个 retain；宽/内联标量元素为裸存无引用（不装箱、不增计数） */
            if (et == ECS_STRING || et == ECS_ARRAY)
                for (int32_t i = 0; i < src->h.len; i++)
                {
                    int err;
                    ecs_value item = arr_get(vm, src, i, &err);
                    (void)err;
                    retain(vm, item);
                }
        }
        ecs_value r = void_value(); r.tag = ECS_ARRAY; r.u32 = (uint32_t)h;
        return r;
    }
    if (v.tag == ECS_STRUCT)
    {
        ecs_obj *src = heap_obj(vm, (int32_t)v.u32);
        if (!src) return void_value();
        int32_t h = hnew_st(vm, &vm->structs[src->h.sid]);
        if (h < 0) return void_value();
        ecs_obj *dst = obj_at(vm, h);
        const ecs_structdef *def = &vm->structs[src->h.sid];
        ecs_value *dslots = (ecs_value *)OBJ_PAYLOAD(dst);
        ecs_value *sslots = (ecs_value *)OBJ_PAYLOAD(src);
        memcpy(dslots, sslots, (size_t)def->nslots * sizeof(ecs_value));
        for (int32_t i = 0; i < def->nslots; i++)
            retain(vm, dslots[i]);
        ecs_value r = void_value(); r.tag = ECS_STRUCT; r.u32 = (uint32_t)h;
        return r;
    }
    ecs_value r = v;
    retain(vm, r);
    return r;
}

/* COW（move-on-unique）：句柄唯一引用（rc==1，恒为出生临时槽持有）时共享移交，跳过容器
   深拷贝；否则维持 S-01 深拷贝。可观察值语义不变——变量/全局槽永不与另一变量/全局共享容器。
   （装箱宽标量值不可变，共享移交与逐位拷贝不可区分，走同一 COW 快径。） */
static ecs_value deep_copy_cow(ecs_vm *vm, ecs_value v)
{
    if (is_heap_tag(v.tag))
    {
        ecs_obj *o = heap_obj(vm, (int32_t)v.u32);
        if (o && o->h.rc == 1)
        {
            retain(vm, v);
            return v;
        }
    }
    return deep_copy(vm, v);
}


/* ---- 字符串工具 ---- */

static void str_or_null(ecs_vm *vm, ecs_value v, const uint16_t **units, int32_t *len)
{
    if (v.tag != ECS_STRING) { *units = NULL; *len = 0; return; }
    if (is_static_str(v))
    {
        /* S-20：pinned 常量串 —— 字符数据在镜像里，零拷贝直接引用（units_len 以字节计） */
        int32_t kx = static_str_index(v);
        if (kx < 0 || kx >= vm->nconsts) { *units = NULL; *len = 0; return; }
        const_ent *k = &vm->consts[kx];
        *units = k->units;
        *len = k->units_len / 2;
        return;
    }
    if (v.u32 == 0) { *units = NULL; *len = 0; return; }
    ecs_obj *o = heap_obj(vm, (int32_t)v.u32);
    if (!o) { *units = NULL; *len = 0; return; }
    *units = (const uint16_t *)OBJ_PAYLOAD(o);
    *len = o->h.len;
}

static int units_equal(const uint16_t *a, int32_t alen, const uint16_t *b, int32_t blen)
{
    if (alen != blen) return 0;
    if (alen == 0) return 1;
    if (!a || !b) return 0;
    return memcmp(a, b, (size_t)alen * sizeof(uint16_t)) == 0;
}

static int units_contains(const uint16_t *hay, int32_t hlen, const uint16_t *needle, int32_t nlen)
{
    if (nlen == 0) return 1;
    if (!hay || !needle || nlen > hlen) return 0;
    for (int32_t i = 0; i <= hlen - nlen; i++)
        if (memcmp(hay + i, needle, (size_t)nlen * sizeof(uint16_t)) == 0)
            return 1;
    return 0;
}

/* ============================================================
 * S-07/S-08 TOSTR 两层上下文（有界工作缓冲：溢出 = ECS_ERR_POOL，不做块链）
 * ============================================================ */

typedef struct { uint16_t units[ECS_TOSTR_MAX]; int32_t len; int overflow; } strbuf;

static int sb_reserve(strbuf *sb, int32_t extra)
{
    if (sb->len + extra > ECS_TOSTR_MAX) { sb->overflow = 1; return 0; }
    return 1;
}

static void sb_ascii(strbuf *sb, const char *s)
{
    while (*s) { if (!sb_reserve(sb, 1)) return; sb->units[sb->len++] = (uint16_t)(unsigned char)*s++; }
}

static void sb_units(strbuf *sb, const uint16_t *u, int32_t len)
{
    if (len > 0 && sb_reserve(sb, len))
    {
        memcpy(sb->units + sb->len, u, (size_t)len * sizeof(uint16_t));
        sb->len += len;
    }
}

/* .NET double.ToString() 最短往返近似 */
static void sb_double(strbuf *sb, double d)
{
    char tmp[64];
    for (int prec = 15; prec <= 17; prec++)
    {
        snprintf(tmp, sizeof(tmp), "%.*g", prec, d);
        if (strtod(tmp, NULL) == d) break;
    }
    sb_ascii(sb, tmp);
}

static void tostring_nested(ecs_vm *vm, strbuf *sb, ecs_value v);

static void tostring_top(ecs_vm *vm, strbuf *sb, ecs_value v)
{
    char tmp[32];
    switch (v.tag)
    {
        case ECS_BOOL: sb_ascii(sb, v.i32 ? "true" : "false"); break;
        case ECS_BYTE:
        case ECS_INT:
        case ECS_UINT: snprintf(tmp, sizeof(tmp), "%d", v.i32); sb_ascii(sb, tmp); break;
        case ECS_UINT64: snprintf(tmp, sizeof(tmp), "%" PRIu64, (uint64_t)box_l(vm, v)); sb_ascii(sb, tmp); break;
        case ECS_PTR: snprintf(tmp, sizeof(tmp), "%" PRId64, box_l(vm, v)); sb_ascii(sb, tmp); break;
        case ECS_DOUBLE: sb_double(sb, box_d(vm, v)); break;
        case ECS_STRING:
        {
            const uint16_t *u; int32_t len;
            str_or_null(vm, v, &u, &len);
            sb_units(sb, u, len);
            break;
        }
        default: tostring_nested(vm, sb, v); break;
    }
}

static void tostring_nested(ecs_vm *vm, strbuf *sb, ecs_value v)
{
    char tmp[40];
    switch (v.tag)
    {
        case ECS_ARRAY:
        {
            ecs_obj *o = heap_obj(vm, (int32_t)v.u32);
            sb_ascii(sb, "[");
            if (o)
                for (int32_t i = 0; i < o->h.len; i++)
                {
                    if (i > 0) sb_ascii(sb, ", ");
                    int err;
                    ecs_value item = arr_get(vm, o, i, &err);
                    if (err) { sb->overflow = 1; return; }   /* 宽元素装箱块耗尽 → 同 TOSTR 有界拒绝 */
                    tostring_nested(vm, sb, item);
                    if (is_boxed_tag(item.tag)) release(vm, item);
                }
            sb_ascii(sb, "]");
            break;
        }
        case ECS_STRUCT:
        {
            ecs_obj *o = heap_obj(vm, (int32_t)v.u32);
            sb_ascii(sb, "struct:");
            if (o && o->h.sid >= 0 && o->h.sid < vm->nstructs && vm->structs[o->h.sid].name)
                sb_ascii(sb, vm->structs[o->h.sid].name);
            break;
        }
        case ECS_PTR: snprintf(tmp, sizeof(tmp), "0x%" PRIX64, box_bits(vm, v)); sb_ascii(sb, tmp); break;
        case ECS_BOOL: sb_ascii(sb, v.i32 ? "true" : "false"); break;
        case ECS_UINT64: snprintf(tmp, sizeof(tmp), "%" PRIu64, (uint64_t)box_l(vm, v)); sb_ascii(sb, tmp); break;
        case ECS_DOUBLE: sb_double(sb, box_d(vm, v)); break;
        case ECS_STRING:
        {
            const uint16_t *u; int32_t len;
            str_or_null(vm, v, &u, &len);
            sb_units(sb, u, len);
            break;
        }
        case ECS_VOID: sb_ascii(sb, "void"); break;
        default: snprintf(tmp, sizeof(tmp), "%d", v.i32); sb_ascii(sb, tmp); break;
    }
}

static ecs_value tostring_value(ecs_vm *vm, ecs_value v, int *pool_full)
{
    strbuf sb; memset(&sb, 0, sizeof(sb));
    tostring_top(vm, &sb, v);
    *pool_full = sb.overflow;
    if (sb.overflow) return void_value();
    ecs_value result = new_str_units(vm, sb.units, sb.len);
    if (result.tag == ECS_VOID) *pool_full = 1;
    return result;
}

/* S-09 元素值相等 */
static int value_equals(ecs_vm *vm, ecs_value x, ecs_value y)
{
    if (x.tag != y.tag) return 0;
    if (x.tag == ECS_STRING)
    {
        const uint16_t *ux, *uy; int32_t lx, ly;
        str_or_null(vm, x, &ux, &lx);
        str_or_null(vm, y, &uy, &ly);
        return units_equal(ux, lx, uy, ly);
    }
    if (x.tag == ECS_DOUBLE)
        return box_d(vm, x) == box_d(vm, y);   /* 值相等，非句柄同一性 */
    if (x.tag == ECS_UINT64 || x.tag == ECS_PTR)
        return box_bits(vm, x) == box_bits(vm, y);   /* 64 位位模式比较，非句柄 */
    return x.u32 == y.u32;   /* 内联标量按位；容器句柄按同一性（与 16B 时代一致） */
}

/* ============================================================
 * §4.1 镜像解析（ECSC 容器）+ §4.4 结构体布局展开
 * ============================================================ */

typedef struct {
    const uint8_t *p;
    size_t len, pos;
    int overflow;
} reader;

static int64_t r_bytes(reader *r, size_t n)
{
    if (r->overflow || r->pos + n > r->len) { r->overflow = 1; return 0; }
    uint64_t v = 0;
    for (size_t i = 0; i < n; i++)
        v |= (uint64_t)r->p[r->pos + i] << (8 * i);
    r->pos += n;
    return (int64_t)v;
}

static char *r_utf8(reader *r)
{
    int64_t len = r_bytes(r, 2);
    if (r->overflow || len < 0 || r->pos + (size_t)len > r->len) { r->overflow = 1; return NULL; }
    char *s = (char *)malloc((size_t)len + 1);
    if (!s) { r->overflow = 1; return NULL; }
    memcpy(s, r->p + r->pos, (size_t)len);
    s[len] = 0;
    r->pos += (size_t)len;
    return s;
}

static int32_t r_count(reader *r)
{
    int64_t v = r_bytes(r, 4);
    if (v > 100000000) { r->overflow = 1; return 0; }
    return (int32_t)v;
}


/* 嵌套展开（§4.4）：Scalar/Boxed=1 槽，FixedArray=count，NestedStruct=嵌套 nslots；环 → -1 */
static int32_t expand_struct_slots(ecs_structdef *defs, int32_t sid, uint8_t *visiting)
{
    if (defs[sid].nslots > 0) return defs[sid].nslots;
    if (visiting[sid]) return -1;
    visiting[sid] = 1;
    int32_t total = 0;
    for (int32_t f = 0; f < defs[sid].nfields; f++)
    {
        ecs_fielddef *fd = &defs[sid].fields[f];
        fd->slot_offset = total;
        int32_t size;
        if (fd->kind == FIELD_KIND_FIXED_ARRAY)
            size = fd->ext;
        else if (fd->kind == FIELD_KIND_NESTED)
        {
            size = expand_struct_slots(defs, fd->ext, visiting);
            if (size < 0) { visiting[sid] = 0; return -1; }
        }
        else                        /* Scalar / Boxed */
            size = 1;
        total += size;
    }
    visiting[sid] = 0;
    defs[sid].nslots = total;
    return total;
}

static int32_t load_image(ecs_vm *vm, const uint8_t *image, size_t len)
{
    if (!vm || !image || len < 36) return ECS_ERR_IMAGE;
    vm->owned_img = 0;
    vm->img = image;
    vm->img_len = len;

    /* ---- ECX1 平铺头（36B）---- */
    if (rd_u32(image) != 0x31584345u) return ECS_ERR_IMAGE;      /* "ECX1" */
    if (rd_u16(image + 4) != 1) return ECS_ERR_IMAGE;            /* format = 1 */
    if (rd_u16(image + 6) != (uint16_t)ECS_ABI_REV) return ECS_ERR_UNSUPPORTED;
    uint16_t feats = rd_u16(image + 8);
    vm->entry = (int16_t)rd_u16(image + 10);
    uint8_t flags = image[12];
    uint8_t name_len = image[13];
    uint16_t func_count = rd_u16(image + 14);
    uint32_t code_size = rd_u32(image + 16);
    uint32_t dbg_size = rd_u32(image + 20);
    if (vm->entry < 0 || 36 + (size_t)name_len > len) return ECS_ERR_IMAGE;

    /* 信任面：不做 CRC / 指令流内容校验——相信宿主提供的字节码
     * （Lua 式最小验证面）；解析期仅保证宿主内存安全（r_* 越界防护）。 */

    /* 特征需求投影（flags.I → FEAT_IL）；strict 语义保留——能力降级为后续契约批次 */
    uint32_t img_feats = feats | ((flags & 0x4) ? ECS_FEAT_IL : 0);
    /* S-21：feats = 元信息，缺省不拒载（运行期按缺省值表降级）；strict_caps 恢复响亮校验 */
    if (vm->host.strict_caps) {
        if (img_feats & ECS_FEAT_IL) return ECS_ERR_IL;
        if (img_feats & ~vm->host.feats) return ECS_ERR_FEAT;
    }

    size_t pos = 36 + name_len;

    /* ---- 常量池（count + entries）---- */
    {
        reader r = { image, len, pos, 0 };
        int32_t nconsts = r_count(&r);
        if (r.overflow) return ECS_ERR_IMAGE;
        vm->consts = (const_ent *)calloc((size_t)(nconsts > 0 ? nconsts : 1), sizeof(const_ent));
        if (!vm->consts) return ECS_ERR_OOM;
        vm->nconsts = nconsts;
        for (int32_t i = 0; i < nconsts; i++)
        {
            const_ent *c = &vm->consts[i];
            c->tag = (uint8_t)r_bytes(&r, 1);
            switch (c->tag)
            {
                case ECS_INT:
                case ECS_UINT:
                    c->i64 = r_bytes(&r, 4);
                    break;
                case ECS_UINT64:
                case ECS_PTR:
                    c->i64 = r_bytes(&r, 8);
                    break;
                case ECS_DOUBLE:
                {
                    uint64_t bits = (uint64_t)r_bytes(&r, 8);
                    memcpy(&c->f64, &bits, sizeof(double));
                    break;
                }
                case ECS_STRING:
                {
                    int64_t units = r_bytes(&r, 2);
                    if (r.overflow || units < 0) return ECS_ERR_IMAGE;
                    c->units_len = (int32_t)units * 2;
                    if (r.pos + (size_t)c->units_len > len) return ECS_ERR_IMAGE;
                    c->units = (const uint16_t *)(const void *)(image + r.pos);
                    r.pos += (size_t)c->units_len;
                    break;
                }
                default:
                    return ECS_ERR_IMAGE;
            }
        }
        if (r.overflow) return ECS_ERR_IMAGE;
        pos = r.pos;
    }

    /* ---- 类型表（count + entries；布局展开）---- */
    {
        reader r = { image, len, pos, 0 };
        int32_t nstructs = r_count(&r);
        if (r.overflow) return ECS_ERR_IMAGE;
        vm->structs = (ecs_structdef *)calloc((size_t)(nstructs > 0 ? nstructs : 1), sizeof(ecs_structdef));
        if (!vm->structs) return ECS_ERR_OOM;
        vm->nstructs = nstructs;
        for (int32_t st = 0; st < nstructs; st++)
        {
            ecs_structdef *def = &vm->structs[st];
            def->name = r_utf8(&r);
            def->nfields = (int32_t)r_bytes(&r, 1);
            def->fields = (ecs_fielddef *)calloc((size_t)(def->nfields > 0 ? def->nfields : 1), sizeof(ecs_fielddef));
            if (!def->name || !def->fields) return r.overflow ? ECS_ERR_IMAGE : ECS_ERR_OOM;
            for (int32_t f = 0; f < def->nfields; f++)
            {
                ecs_fielddef *fd = &def->fields[f];
                fd->name = r_utf8(&r);
                fd->kind = (uint8_t)r_bytes(&r, 1);
                fd->type = (uint8_t)r_bytes(&r, 1);
                fd->elem = (uint8_t)r_bytes(&r, 1);
                fd->ext = (uint16_t)r_bytes(&r, 2);
                if (!fd->name) return ECS_ERR_IMAGE;
            }
        }
        if (r.overflow) return ECS_ERR_IMAGE;
        pos = r.pos;
    }
    {
        uint8_t *visiting = (uint8_t *)calloc((size_t)(vm->nstructs > 0 ? vm->nstructs : 1), 1);
        if (!visiting) return ECS_ERR_OOM;
        for (int32_t st = 0; st < vm->nstructs; st++)
            if (expand_struct_slots(vm->structs, st, visiting) < 0)
            {
                free(visiting);
                return ECS_ERR_IMAGE;   /* 嵌套环 */
            }
        free(visiting);
    }

    /* ---- 全局槽（count + (module_idx u8 + type u8)——VM 只占位不消费名字）---- */
    {
        reader r = { image, len, pos, 0 };
        int32_t nglobals = r_count(&r);
        if (r.overflow) return ECS_ERR_IMAGE;
        vm->globals = (ecs_value *)calloc((size_t)(nglobals > 0 ? nglobals : 1), sizeof(ecs_value));
        if (!vm->globals) return ECS_ERR_OOM;
        vm->nglobals = nglobals;
        for (int32_t g = 0; g < nglobals; g++)
        {
            (void)r_bytes(&r, 2);
            if (r.overflow) return ECS_ERR_IMAGE;
        }
        pos = r.pos;
    }

    /* ---- 原生名表 ---- */
    {
        reader r = { image, len, pos, 0 };
        int32_t nnatives = r_count(&r);
        if (r.overflow) return ECS_ERR_IMAGE;
        vm->natives = (char **)calloc((size_t)(nnatives > 0 ? nnatives : 1), sizeof(char *));
        if (!vm->natives) return ECS_ERR_OOM;
        vm->nnatives = nnatives;
        for (int32_t n = 0; n < nnatives; n++)
        {
            vm->natives[n] = r_utf8(&r);
            if (!vm->natives[n]) return ECS_ERR_IMAGE;
        }
        if (r.overflow) return ECS_ERR_IMAGE;
        pos = r.pos;
    }

    /* ---- 函数表（count + (nslots u16 + code_off u32)）+ .text ---- */
    {
        reader r = { image, len, pos, 0 };
        int32_t nfuncs = r_count(&r);
        if (r.overflow || nfuncs <= 0) return ECS_ERR_IMAGE;
        if ((uint16_t)nfuncs != func_count) return ECS_ERR_IMAGE;   /* 头/表计数一致 */
        vm->funcs = (ecs_funcdef *)calloc((size_t)nfuncs, sizeof(ecs_funcdef));
        if (!vm->funcs) return ECS_ERR_OOM;
        vm->nfuncs = nfuncs;
        for (int32_t f = 0; f < nfuncs; f++)
        {
            ecs_funcdef *fd = &vm->funcs[f];
            fd->nslots = (uint16_t)r_bytes(&r, 2);
            fd->code_off = (uint32_t)r_bytes(&r, 4);
            if (r.overflow) return ECS_ERR_IMAGE;
            if (fd->nslots > ECS_REG_MAX) return ECS_ERR_IMAGE;   /* 帧长上限 = 指令域 u8 */
        }
        if (code_size > len - r.pos) return ECS_ERR_IMAGE;
        const uint8_t *stream = image + r.pos;
        for (int32_t f = 0; f < nfuncs; f++)
        {
            vm->funcs[f].code = stream + vm->funcs[f].code_off;
            uint32_t next_off = (f + 1 < nfuncs) ? vm->funcs[f + 1].code_off : code_size;
            vm->funcs[f].code_bytes = next_off - vm->funcs[f].code_off;
        }
        /* 连续性（第二遍，code_bytes 已知） */
        for (int32_t f = 0; f + 1 < nfuncs; f++)
            if (vm->funcs[f + 1].code_off != vm->funcs[f].code_off + vm->funcs[f].code_bytes)
                return ECS_ERR_IMAGE;
        pos = r.pos + code_size;
    }

    /* ---- 调试块（可选；VM 不消费，跳过）---- */
    if (dbg_size > 0)
    {
        if (dbg_size > len - pos) return ECS_ERR_IMAGE;
        pos += dbg_size;
    }
    if (pos != len) return ECS_ERR_IMAGE;   /* 平铺布局严格等长（fail-closed） */

    if (vm->entry < 0 || vm->entry >= vm->nfuncs) return ECS_ERR_IMAGE;
    return ECS_OK;
}

int ecs_vm_load(ecs_vm *vm, const uint8_t *image, size_t len)
{
    if (!vm) return ECS_ERR_IMAGE;
    return load_image(vm, image, len);
}

int ecs_vm_load_io(ecs_vm *vm, const ecs_io *io)
{
    if (!vm || !io || !io->read || io->size < 36) return ECS_ERR_IMAGE;
    uint8_t *buf = (uint8_t *)malloc(io->size);
    if (!buf) return ECS_ERR_OOM;
    if (io->read(io->ud, 0, buf, io->size) != io->size)
    {
        free(buf);
        return ECS_ERR_IMAGE;   /* 读失败（EEPROM/NVS 短读） */
    }
    int32_t rc = load_image(vm, buf, io->size);
    if (rc != ECS_OK)
    {
        free(buf);
        return rc;
    }
    vm->img = buf;              /* 字符串/名字等 pinned 指向内部副本 */
    vm->img_len = io->size;
    vm->owned_img = 1;
    return ECS_OK;
}

/* ---- 字节码来源抽象（uLua zIO 式）---- */

typedef struct { const uint8_t *buf; uint32_t len; int direct; } mem_io_state;

static uint32_t mem_io_read(void *ud, uint32_t offset, uint8_t *out, uint32_t len)
{
    mem_io_state *m = (mem_io_state *)ud;
    if (offset > m->len) return 0;
    uint32_t n = m->len - offset < len ? m->len - offset : len;
    memcpy(out, m->buf + offset, n);
    return n;
}

void ecs_io_mem(ecs_io *io, const void *buf, uint32_t len, int direct)
{
    static mem_io_state state;   /* 单宿主线程约定（VM 本身非线程安全） */
    state.buf = (const uint8_t *)buf;
    state.len = len;
    state.direct = direct;
    io->ud = &state;
    io->size = len;
    io->read = mem_io_read;
}

/* ============================================================
 * §4.5 核心原生表
 * ============================================================ */

/* ---- 宿主访问 VM 数据的唯一通道（syscall 处理器内使用；uvm32 arg_get* 同构） ---- */

int32_t ecs_vm_arg_str(ecs_vm *vm, ecs_value v, const uint16_t **units)
{
    int32_t len = 0;
    str_or_null(vm, v, units, &len);
    return len;
}

void ecs_vm_ret_str(ecs_vm *vm, ecs_value *ret, const uint16_t *units, int32_t len)
{
    *ret = new_str_units(vm, units, len);
    if (ret->tag == ECS_VOID) vm->host_error = ECS_ERR_POOL;
}

int64_t ecs_vm_arg_wide(ecs_vm *vm, ecs_value v)
{
    if (v.tag == ECS_PTR || v.tag == ECS_UINT64) return box_l(vm, v);
    return v.i32;   /* 内联 tag：i32 位模式符号扩展（宿主按 PTR/INT 语义自行解释） */
}

int ecs_vm_ret_wide(ecs_vm *vm, ecs_value *ret, uint8_t tag, int64_t x)
{
    *ret = void_value();
    if (tag != ECS_PTR && tag != ECS_UINT64)
        return vm->host_error = ECS_ERR_TYPE;
    int err = 0;
    *ret = (tag == ECS_UINT64) ? box_u64(vm, x, &err) : box_ptr(vm, x, &err);
    if (err) return vm->host_error = ECS_ERR_POOL;
    return ECS_OK;
}

double ecs_vm_arg_double(ecs_vm *vm, ecs_value v)
{
    return box_d(vm, v);
}

int ecs_vm_ret_double(ecs_vm *vm, ecs_value *ret, double x)
{
    int err = 0;
    *ret = box_double(vm, x, &err);
    if (err) return vm->host_error = ECS_ERR_POOL;
    return ECS_OK;
}

/* ---- S-21 能力降级缺省值表（与 C# EcsCapabilityDefaults 双生；能力矩阵对拍锁定） ----
 * 宿主回调缺失/未实现时按表返回类型正确的中性值；返回 1=命中缺省，0=表外（保持响亮）。
 * 表外原则：未知 syscall 编号 / 未知原生名不降级（打错名不静默）。 */

static int ecs_cap_syscall_default(ecs_vm *vm, int32_t id, ecs_value *args, ecs_value *ret)
{
    const uint16_t *u = NULL;
    switch (id) {
    case ECS_SYSCALL_FWRITE:   /* PRINT 降级：静默仍返 len（S-14） */
        *ret = (ecs_value){0};
        ret->tag = ECS_INT;
        ret->i32 = ecs_vm_arg_str(vm, args[1], &u);
        return 1;
    case ECS_SYSCALL_FREAD:
    case ECS_SYSCALL_READFILE:
        ecs_vm_ret_str(vm, ret, NULL, 0);
        return 1;
    case ECS_SYSCALL_FOPEN:
    {
        ecs_vm_ret_wide(vm, ret, ECS_PTR, -1);   /* 无效句柄；池耗尽由 CallN 响亮传播 */
        return 1;
    }
    case ECS_SYSCALL_FEOF:
    case ECS_SYSCALL_FILE_EXISTS:
    case ECS_SYSCALL_TIME:
    case ECS_SYSCALL_OCR_CONF:
        *ret = (ecs_value){0};
        ret->tag = ECS_INT;
        ret->i32 = 0;
        return 1;
    case ECS_SYSCALL_FCLOSE:
    case ECS_SYSCALL_WRITEFILE:
    case ECS_SYSCALL_APPENDFILE:
    case ECS_SYSCALL_ALERT:
    case ECS_SYSCALL_BEEP:
    case ECS_SYSCALL_AMIIBO:
        *ret = (ecs_value){0}; /* Void：无接收槽语义，等价 no-op（S-13：任何宿主不写寄存器） */
        return 1;
    case ECS_SYSCALL_ARG:
    case ECS_SYSCALL_ENV:
    case ECS_SYSCALL_APP:
        ecs_vm_ret_str(vm, ret, NULL, 0);
        return 1;
    default:
        return 0;              /* 未知编号不降级 */
    }
}

/* ============================================================
 * 公共 API：创建 / 配置 / 销毁
 * ============================================================ */

ecs_vm *ecs_vm_new(const ecs_host *host)
{
    ecs_vm *vm = (ecs_vm *)calloc(1, sizeof(ecs_vm));
    if (!vm) return NULL;
    if (host) vm->host = *host;
    vm->budget = ECS_DEFAULT_BUDGET;
    vm->error_func = -1;
    vm->error_pc = -1;
    vm->free_head = -1;
    pool_init(vm);
    return vm;
}

void ecs_vm_cancel(ecs_vm *vm) { if (vm) vm->cancel_flag = 1; }

void ecs_vm_set_budget(ecs_vm *vm, int32_t steps_per_yield)
{
    if (vm && steps_per_yield > 0) vm->budget = steps_per_yield;
}

void ecs_vm_set_wait_yield(ecs_vm *vm, int enable)
{
    if (vm) vm->wait_yield = enable ? 1 : 0;
}

void ecs_vm_query_meminfo(ecs_vm_meminfo *out)
{
    if (!out) return;
    out->instance_bytes = sizeof(ecs_vm) - sizeof(ecs_host);
    out->host_bytes = sizeof(ecs_host);
    out->arena_bytes = ECS_ARENA_BYTES;
    out->frame_seg_bytes = ECS_FRAME_SEG_BYTES;
    out->obj_seg_bytes = (size_t)ECS_OBJ_SEG_BYTES;
    out->obj_block_bytes = ECS_OBJ_BLOCK_BYTES;
    out->obj_block_count = ECS_OBJ_BLOCK_COUNT;
    out->value_bytes = (uint32_t)sizeof(ecs_value);
}

void ecs_vm_error_location(const ecs_vm *vm, int32_t *out_func, int32_t *out_pc)
{
    if (out_func) *out_func = vm ? vm->error_func : -1;
    if (out_pc) *out_pc = vm ? vm->error_pc : -1;
}

/* 兜底 sweep 单对象：先摘除 in-use 位（阻断环/重复/悬挂子引用），再递归释放子引用。
   不走 release/引用计数——残余对象可能互相引用且 rc 不归零（§4.2）。 */
static void sweep_obj(ecs_vm *vm, int32_t h)
{
    ecs_obj *o = (h >= 1 && h <= ECS_OBJ_BLOCK_COUNT) ? obj_at(vm, h) : NULL;
    if (!o || !(o->h.flags & OBJ_IN_USE)) return;
    o->h.flags = 0;
    switch (o->h.kind)
    {
        case ECS_STRING: break;
        case OBJ_KIND_BOX: break;
        case ECS_ARRAY:
            arr_foreach_handle(vm, o, 1);
            break;
        case ECS_STRUCT:
        {
            const ecs_structdef *def = &vm->structs[o->h.sid];
            ecs_value *slots = (ecs_value *)OBJ_PAYLOAD(o);
            for (int32_t i = 0; i < def->nslots; i++)
                if (is_heap_tag(slots[i].tag))
                    sweep_obj(vm, (int32_t)slots[i].u32);
            break;
        }
    }
}

void ecs_vm_free(ecs_vm *vm)
{
    if (!vm) return;
    /* 帧槽位先行：错误现场可能仍有活句柄，走引用计数正常回收（arena 属 vm，无需 free） */
    for (int32_t d = 0; d < vm->depth; d++)
    {
        frame *fr = frame_at(vm, vm->frame_off[d]);
        for (int32_t s = 0; s < fr->fn->nslots; s++)
            release(vm, fr->slots[s]);
    }
    /* 全量 sweep：不依赖引用计数正确性兜底（§4.2） */
    for (int32_t h = 1; h <= ECS_OBJ_BLOCK_COUNT; h++)
        sweep_obj(vm, h);
    if (vm->structs)
        for (int32_t i = 0; i < vm->nstructs; i++)
        {
            for (int32_t f = 0; f < vm->structs[i].nfields; f++)
                free(vm->structs[i].fields[f].name);
            free(vm->structs[i].fields);
            free(vm->structs[i].name);
        }
    free(vm->structs);
    if (vm->owned_img)
        free((void *)vm->img);   /* io 拉取路径：镜像内部副本归 VM */
    if (vm->natives)
        for (int32_t i = 0; i < vm->nnatives; i++)
            free(vm->natives[i]);
    free(vm->natives);
    if (vm->funcs)
        for (int32_t i = 0; i < vm->nfuncs; i++)
            free(vm->funcs[i].name);
    free(vm->funcs);
    free(vm->consts);
    free(vm->globals);
    free(vm);
}

/* ============================================================
 * §4.3 主循环辅助：数据/容器/结构体/域操作指令
 * ============================================================ */

/* S-04 饱和转换 */
static int32_t saturate_d2i(double d)
{
    if (isnan(d)) return 0;
    if (d >= 2147483647.0) return 2147483647;
    if (d <= -2147483648.0) return (-2147483647 - 1);
    return (int32_t)d;
}

static int64_t saturate_d2l(double d)
{
    if (isnan(d)) return 0;
    if (d >= 9223372036854775807.0) return (int64_t)9223372036854775807;
    if (d <= -9223372036854775808.0) return (-9223372036854775807 - 1);
    return (int64_t)d;
}

static ecs_value do_conv(ecs_vm *vm, uint32_t kind, ecs_value v, int *err)
{
    ecs_value r = void_value();
    *err = 0;
    int pe = 0;                    /* 装箱块耗尽 → 统一映射 *err = 2（POOL） */
    switch (kind)
    {
        case CV_INT_TO_DOUBLE: r = box_double(vm, (double)v.i32, &pe); if (pe) *err = 2; break;
        case CV_DOUBLE_TO_INT: r = make_int(saturate_d2i(box_d(vm, v))); break;
        case CV_INT_TO_UINT: r.tag = ECS_UINT; r.i32 = v.i32; break;
        case CV_UINT_TO_INT: r.tag = ECS_INT; r.i32 = v.i32; break;
        case CV_INT_TO_BYTE: r.tag = ECS_BYTE; r.i32 = (int32_t)(uint8_t)v.i32; break;
        case CV_BOOL_TO_INT: r.tag = ECS_INT; r.i32 = v.i32; break;
        case CV_INT_TO_UINT64: r = box_u64(vm, (int64_t)v.i32, &pe); if (pe) *err = 2; break;
        case CV_UINT_TO_UINT64: r = box_u64(vm, (int64_t)(uint32_t)v.i32, &pe); if (pe) *err = 2; break;
        case CV_UINT64_TO_INT: r = make_int((int32_t)box_l(vm, v)); break;
        case CV_INT_TO_PTR: r = box_ptr(vm, (int64_t)v.i32, &pe); if (pe) *err = 2; break;
        case CV_PTR_TO_INT: r = make_int((int32_t)box_bits(vm, v)); break;
        case CV_UINT64_TO_PTR: r = box_ptr(vm, box_l(vm, v), &pe); if (pe) *err = 2; break;
        case CV_PTR_TO_UINT64: r = box_u64(vm, box_l(vm, v), &pe); if (pe) *err = 2; break;
        case CV_DOUBLE_TO_UINT64: r = box_u64(vm, saturate_d2l(box_d(vm, v)), &pe); if (pe) *err = 2; break;
        case CV_UINT64_TO_DOUBLE: r = box_double(vm, (double)(uint64_t)box_l(vm, v), &pe); if (pe) *err = 2; break;
        case CV_TOSTR:
        {
            int pool_full = 0;
            r = tostring_value(vm, v, &pool_full);
            if (pool_full) *err = 2;   /* TOSTR 结果超有界缓冲 → ECS_ERR_POOL */
            return r;
        }
        case CV_TOINT:
            if (v.tag == ECS_BOOL || v.tag == ECS_BYTE || v.tag == ECS_INT || v.tag == ECS_UINT)
                { r.tag = ECS_INT; r.i32 = v.i32; }
            else if (v.tag == ECS_DOUBLE)
                { r.tag = ECS_INT; r.i32 = saturate_d2i(box_d(vm, v)); }
            else if (v.tag == ECS_STRING)
                { r.tag = ECS_INT; r.i32 = 0; }   /* 字符串解析为 PC 端实现；MCU 端静默返回 0 */
            else
                *err = 1;
            break;
        default: *err = 1; break;
    }
    return r;
}

/* 帧段 bump 分配（P2，docs/ZeroAllocVm.md §3）：无碎片、O(1)、无元数据；
   深度上界由帧段容量自然给出，ECS_MAX_CALL_DEPTH 仍为先行硬上限（双端对拍 DEPTH 一致性）。 */
static int push_frame(ecs_vm *vm, const ecs_funcdef *fn, int32_t func_index)
{
    if (vm->depth >= ECS_MAX_CALL_DEPTH)
        return ECS_ERR_DEPTH;
    if (vm->depth >= ECS_MAX_FRAMES)
        return ECS_ERR_POOL;   /* 帧段满（帧表容量） */
    size_t need = frame_size(fn);
    if (vm->frame_sp + need > (size_t)ECS_FRAME_SEG_BYTES)
        return ECS_ERR_POOL;   /* 帧段满（字节容量） */
    frame *fr = (frame *)(vm->arena + vm->frame_sp);
    vm->frame_sp += need;
    memset(fr, 0, need);
    fr->fn = fn;
    fr->func_index = (uint16_t)func_index;
    fr->ret_slot = ECS_REG_RET;   /* 入口帧返回落点（顶层 Ret 走 Result 路径，不消费） */
    vm->frame_off[vm->depth++] = (uint32_t)(vm->frame_sp - need);
    return 0;
}

/* 弹帧：槽位释放后 bump 水位回退（调用/返回严格嵌套，无碎片；帧尺寸按 fn 派生回退） */
static void pop_frame(ecs_vm *vm, frame *fr)
{
    for (int32_t s = 0; s < fr->fn->nslots; s++)
        if (is_heap_tag(fr->slots[s].tag))   /* 标量槽免调用快径（对标量 release 本为 no-op） */
            release(vm, fr->slots[s]);
    vm->frame_sp -= frame_size(fr->fn);
    vm->depth--;
}

/* ============================================================
 * §4.5 主派发（直接线程化 / switch 双模）—— 干净重写版
 *
 * gcc/clang：computed goto——每操作码一个标签，dispatch 表按操作码值索引，
 * 取指预检后即跳；handler 按自身格式特化取操作数（FETCH_OPS_*，无二级格式 switch）。
 * 其余编译器（MSVC 等）：宏回退 switch 循环（同一份 handler 体，VM_CASE=case，
 * 操作数由 VM_FETCH 统一解码，FETCH_OPS_* 为空）。
 *
 * 数据操作已自 exec_data_op 扁平化并入本派发（消除每条数据指令的函数调用）。
 * 信任字节码：handler 内除语义必需（除零/越界/类型等脚本可见错误）外无验证。
 * ============================================================ */
#if defined(__GNUC__) && !defined(ECS_NO_DIRECT_THREADING)
#define ECS_DIRECT_THREADED 1
#else
#define ECS_DIRECT_THREADED 0
#endif

#if !ECS_DIRECT_THREADED
/* ---- 回退模式专用：格式类表 + 统一取指宏 ----
 * 类别：ABC=三槽位；ABX=A+Bx16；SBXI=A+sBx16（LoadI）；AJ=A+跳转 s16（Jpt/Jpf）；
 * J=跳转 s24（Jmp）；WJ=iABC+跳转字（ForStep/CmpJ，跳转字延迟读）；WEXT=iABC+数据字。
 * 未列操作码 = FMT_ABC（多数算术/比较/容器族）。与 C# EcsFormat 表逐项对齐。 */
enum { FMT_ABC, FMT_ABX, FMT_SBXI, FMT_AJ, FMT_J, FMT_WJ, FMT_WEXT };
static const uint8_t ecs_op_fmt[] = {
    [OP_LoadI] = FMT_SBXI,
    [OP_LoadK] = FMT_ABX, [OP_LoadG] = FMT_ABX, [OP_StoreG] = FMT_ABX,
    [OP_NewSt] = FMT_ABX, [OP_NewArrE] = FMT_ABX, [OP_Img] = FMT_ABX,
    [OP_Jmp] = FMT_J, [OP_Jpt] = FMT_AJ, [OP_Jpf] = FMT_AJ,
    [OP_Call] = FMT_WEXT, [OP_CallN] = FMT_WEXT, [OP_NewArrV] = FMT_WEXT,
    [OP_Slice] = FMT_WEXT, [OP_GetFI] = FMT_WEXT, [OP_PutFI] = FMT_WEXT,
    [OP_WaitI] = FMT_WEXT, [OP_KeyI] = FMT_WEXT,
    [OP_StickP] = FMT_WEXT, [OP_StickPv] = FMT_WEXT,
    [OP_ForStep] = FMT_WJ, [OP_CmpJ] = FMT_WJ,
};

#define VM_FETCH() do { \
    if (vm->cancel_flag) { vm->steps = steps; fr->ret_pc = pc; fr->ret_idx = idx; return ECS_CANCELLED; } \
    if (++steps >= vm->budget) { vm->steps = 0; fr->ret_pc = pc; fr->ret_idx = idx; return ECS_YIELD; } \
    if (pc >= cend) \
    { \
        vm->steps = steps; \
        vm->error_func = fr->func_index; \
        vm->error_pc = (int32_t)idx; \
        return ECS_ERR_OPCODE; \
    } \
    opv = codebuf[pc]; \
    fmt = ecs_op_fmt[opv]; \
    op_size = (fmt >= FMT_WJ) ? 8 : 4; \
    if (pc + op_size > cend) \
    { \
        vm->steps = steps; \
        vm->error_func = fr->func_index; \
        vm->error_pc = (int32_t)idx; \
        return ECS_ERR_OPCODE; \
    } \
    ext_ = 0; \
    jmp_ = 0; \
    switch (fmt) \
    { \
        case FMT_ABC: \
            a_ = codebuf[pc + 1]; b_ = codebuf[pc + 2]; c_ = codebuf[pc + 3]; \
            break; \
        case FMT_ABX: \
            a_ = codebuf[pc + 1]; b_ = codebuf[pc + 2] | codebuf[pc + 3] << 8; c_ = 0; \
            break; \
        case FMT_SBXI: \
            a_ = codebuf[pc + 1]; b_ = (int16_t)(codebuf[pc + 2] | codebuf[pc + 3] << 8); c_ = 0; \
            break; \
        case FMT_AJ: \
            a_ = codebuf[pc + 1]; jmp_ = (int16_t)(codebuf[pc + 2] | codebuf[pc + 3] << 8); b_ = c_ = 0; \
            break; \
        case FMT_J: \
            jmp_ = (int32_t)((codebuf[pc + 1] | codebuf[pc + 2] << 8 | codebuf[pc + 3] << 16) << 8) >> 8; \
            a_ = b_ = c_ = 0; \
            break; \
        case FMT_WJ: \
            a_ = codebuf[pc + 1]; b_ = codebuf[pc + 2]; c_ = codebuf[pc + 3]; \
            break; /* 跳转字延迟读取：仅转移成立时才碰（热环出口多数不成立） */\
        default: /* FMT_WEXT */ \
            a_ = codebuf[pc + 1]; b_ = codebuf[pc + 2]; c_ = codebuf[pc + 3]; \
            ext_ = rd_u32(codebuf + pc + 4); \
            break; \
    } \
    op_start = pc; \
    op_index = idx; \
    idx++; \
    pc += op_size; \
} while (0)
#endif /* !ECS_DIRECT_THREADED */

/* ---- CmpJ 比较辅助：kind = typeBlock*6 + op（0=Eq,1=Neq,2=Lt,3=Le,4=Gt,5=Ge） ---- */
static inline uint32_t kind_block(int32_t kind) { return (uint32_t)kind / 6u; }
static inline int cmp_kind6_i32(int32_t l, int32_t r, int32_t kind)
{
    switch (kind % 6) {
        case 0: return l == r; case 1: return l != r; case 2: return l < r;
        case 3: return l <= r; case 4: return l > r; default: return l >= r;
    }
}
static inline int cmp_kind6_u32(uint32_t l, uint32_t r, int32_t kind)
{
    switch (kind % 6) {
        case 0: return l == r; case 1: return l != r; case 2: return l < r;
        case 3: return l <= r; case 4: return l > r; default: return l >= r;
    }
}
static inline int cmp_kind6_f64(double l, double r, int32_t kind)
{
    switch (kind % 6) {   /* NaN → 不成立（与 C# cmp_double 语义对齐） */
        case 0: return l == r; case 1: return l != r; case 2: return l < r;
        case 3: return l <= r; case 4: return l > r; default: return l >= r;
    }
}
static inline int cmp_kind6_i64(int64_t l, int64_t r, int32_t kind)
{
    switch (kind % 6) {
        case 0: return l == r; case 1: return l != r; case 2: return l < r;
        case 3: return l <= r; case 4: return l > r; default: return l >= r;
    }
}

int ecs_vm_run(ecs_vm *vm)
{
    if (!vm || vm->nfuncs == 0) return ECS_ERR_IMAGE;
    if (vm->depth == 0)
    {
        int rc = push_frame(vm, &vm->funcs[vm->entry], vm->entry);
        if (rc != 0) return rc;
    }

    /* 取指通路全部局部化：跨指令存活，换帧仅 Call/Ret */
    frame *fr = frame_at(vm, vm->frame_off[vm->depth - 1]);
    const uint8_t *codebuf = fr->fn->code;
    uint32_t cend = fr->fn->code_bytes;
    ecs_value *R = fr->slots;
    uint32_t pc = fr->ret_pc;
    uint32_t idx = fr->ret_idx;
    int32_t steps = vm->steps;
    uint32_t opv = 0, op_start = 0, op_size = 4, op_index = 0;
    int32_t a_ = 0, b_ = 0, c_ = 0, jmp_ = 0;
    uint32_t ext_ = 0;
#if !ECS_DIRECT_THREADED
    uint8_t fmt = FMT_ABC;
#endif

#if ECS_DIRECT_THREADED
    /* 派发表：标签地址按操作码值索引（computed goto，gcc/clang 扩展） */
    static const void *const dispatch[OP_CmpJ + 1] = {
        [OP_Nop] = &&L_OP_Nop, [OP_Halt] = &&L_OP_Halt,
        [OP_LoadI] = &&L_OP_LoadI, [OP_LoadK] = &&L_OP_LoadK, [OP_LoadBool] = &&L_OP_LoadBool,
        [OP_Move] = &&L_OP_Move, [OP_SetVar] = &&L_OP_SetVar,
        [OP_LoadG] = &&L_OP_LoadG, [OP_StoreG] = &&L_OP_StoreG,
        [OP_AddI] = &&L_OP_AddI, [OP_SubI] = &&L_OP_SubI, [OP_MulI] = &&L_OP_MulI,
        [OP_DivI] = &&L_OP_DivI, [OP_ModI] = &&L_OP_ModI, [OP_RDivI] = &&L_OP_RDivI,
        [OP_AddU] = &&L_OP_AddU, [OP_SubU] = &&L_OP_SubU, [OP_MulU] = &&L_OP_MulU,
        [OP_DivU] = &&L_OP_DivU, [OP_ModU] = &&L_OP_ModU,
        [OP_AddL] = &&L_OP_AddL, [OP_SubL] = &&L_OP_SubL, [OP_MulL] = &&L_OP_MulL,
        [OP_DivL] = &&L_OP_DivL, [OP_ModL] = &&L_OP_ModL,
        [OP_AddD] = &&L_OP_AddD, [OP_SubD] = &&L_OP_SubD, [OP_MulD] = &&L_OP_MulD, [OP_DivD] = &&L_OP_DivD,
        [OP_BandI] = &&L_OP_BandI, [OP_BorI] = &&L_OP_BorI, [OP_BxorI] = &&L_OP_BxorI,
        [OP_ShlI] = &&L_OP_ShlI, [OP_ShrI] = &&L_OP_ShrI, [OP_BnotI] = &&L_OP_BnotI,
        [OP_EqI] = &&L_OP_EqI, [OP_LtI] = &&L_OP_LtI, [OP_LeI] = &&L_OP_LeI, [OP_GtI] = &&L_OP_GtI, [OP_GeI] = &&L_OP_GeI,
        [OP_EqU] = &&L_OP_EqU, [OP_LtU] = &&L_OP_LtU, [OP_LeU] = &&L_OP_LeU, [OP_GtU] = &&L_OP_GtU, [OP_GeU] = &&L_OP_GeU,
        [OP_EqD] = &&L_OP_EqD, [OP_LtD] = &&L_OP_LtD, [OP_LeD] = &&L_OP_LeD, [OP_GtD] = &&L_OP_GtD, [OP_GeD] = &&L_OP_GeD,
        [OP_EqL] = &&L_OP_EqL, [OP_LtL] = &&L_OP_LtL, [OP_LeL] = &&L_OP_LeL, [OP_GtL] = &&L_OP_GtL, [OP_GeL] = &&L_OP_GeL,
        [OP_EqS] = &&L_OP_EqS, [OP_EqP] = &&L_OP_EqP,
        [OP_Not] = &&L_OP_Not, [OP_NegI] = &&L_OP_NegI, [OP_NegD] = &&L_OP_NegD, [OP_Conv] = &&L_OP_Conv,
        [OP_Jmp] = &&L_OP_Jmp, [OP_Jpt] = &&L_OP_Jpt, [OP_Jpf] = &&L_OP_Jpf,
        [OP_Call] = &&L_OP_Call, [OP_CallN] = &&L_OP_CallN, [OP_Ret] = &&L_OP_Ret, [OP_Ret0] = &&L_OP_Ret0,
        [OP_NewArrV] = &&L_OP_NewArrV, [OP_NewArrE] = &&L_OP_NewArrE,
        [OP_GetI] = &&L_OP_GetI, [OP_SetI] = &&L_OP_SetI, [OP_Slice] = &&L_OP_Slice,
        [OP_Cont] = &&L_OP_Cont, [OP_Append] = &&L_OP_Append, [OP_Cat] = &&L_OP_Cat, [OP_Len] = &&L_OP_Len,
        [OP_NewSt] = &&L_OP_NewSt, [OP_GetF] = &&L_OP_GetF, [OP_PutF] = &&L_OP_PutF,
        [OP_GetFI] = &&L_OP_GetFI, [OP_PutFI] = &&L_OP_PutFI,
        [OP_WaitI] = &&L_OP_WaitI, [OP_WaitV] = &&L_OP_WaitV,
        [OP_KeyI] = &&L_OP_KeyI, [OP_KeyV] = &&L_OP_KeyV, [OP_KeySt] = &&L_OP_KeySt,
        [OP_StickSet] = &&L_OP_StickSet, [OP_StickP] = &&L_OP_StickP, [OP_StickPv] = &&L_OP_StickPv,
        [OP_Img] = &&L_OP_Img, [OP_Rand] = &&L_OP_Rand,
        [OP_ForStep] = &&L_OP_ForStep, [OP_CmpJ] = &&L_OP_CmpJ,
    };
    /* 取指（线程化）：取消/预算/边界预检 + 操作码与尺寸；操作数由 handler 特化取 */
#define VM_FETCH_TH() do { \
    if (vm->cancel_flag) { vm->steps = steps; fr->ret_pc = pc; fr->ret_idx = idx; return ECS_CANCELLED; } \
    if (++steps >= vm->budget) { vm->steps = 0; fr->ret_pc = pc; fr->ret_idx = idx; return ECS_YIELD; } \
    if (pc >= cend) { vm->steps = steps; vm->error_func = fr->func_index; vm->error_pc = (int32_t)idx; return ECS_ERR_OPCODE; } \
    opv = codebuf[pc]; \
    op_size = (uint32_t)ECS_OP_WORDS(opv) * 4u; \
    if (opv > OP_CmpJ || pc + op_size > cend) { vm->steps = steps; vm->error_func = fr->func_index; vm->error_pc = (int32_t)idx; return ECS_ERR_OPCODE; } \
    op_start = pc; op_index = idx; idx++; pc += op_size; \
} while (0)
#define VM_CASE(op) L_##op:
#define VM_NEXT() do { VM_FETCH_TH(); goto *dispatch[opv]; } while (0)
#else
#define VM_CASE(op) case op:
#define VM_NEXT() break;
#endif

    /* 错误现场统一出口（脚本可见错误：除零/越界/类型等） */
#define VM_FAIL(code) do { vm->steps = steps; vm->error_func = fr->func_index; vm->error_pc = (int32_t)op_index; return (code); } while (0)

    /* 操作数取指（handler 按自身格式特化；回退模式由 VM_FETCH 统一解码，此处为空） */
#if ECS_DIRECT_THREADED
#define FETCH_OPS_ABC() do { a_ = codebuf[op_start + 1]; b_ = codebuf[op_start + 2]; c_ = codebuf[op_start + 3]; } while (0)
#define FETCH_OPS_ABX() do { a_ = codebuf[op_start + 1]; b_ = codebuf[op_start + 2] | codebuf[op_start + 3] << 8; c_ = 0; } while (0)
#define FETCH_OPS_SBXI() do { a_ = codebuf[op_start + 1]; b_ = (int16_t)(codebuf[op_start + 2] | codebuf[op_start + 3] << 8); c_ = 0; } while (0)
#define FETCH_OPS_AJ() do { a_ = codebuf[op_start + 1]; jmp_ = (int16_t)(codebuf[op_start + 2] | codebuf[op_start + 3] << 8); b_ = c_ = 0; } while (0)
#define FETCH_OPS_J() do { jmp_ = (int32_t)((codebuf[op_start + 1] | codebuf[op_start + 2] << 8 | codebuf[op_start + 3] << 16) << 8) >> 8; a_ = b_ = c_ = 0; } while (0)
#define FETCH_OPS_WJ() do { a_ = codebuf[op_start + 1]; b_ = codebuf[op_start + 2]; c_ = codebuf[op_start + 3]; } while (0)
#define FETCH_OPS_WEXT() do { a_ = codebuf[op_start + 1]; b_ = codebuf[op_start + 2]; c_ = codebuf[op_start + 3]; ext_ = rd_u32(codebuf + op_start + 4); } while (0)
#else
#define FETCH_OPS_ABC() do { } while (0)
#define FETCH_OPS_ABX() do { } while (0)
#define FETCH_OPS_SBXI() do { } while (0)
#define FETCH_OPS_AJ() do { } while (0)
#define FETCH_OPS_J() do { } while (0)
#define FETCH_OPS_WJ() do { } while (0)
#define FETCH_OPS_WEXT() do { } while (0)
#endif

#if ECS_DIRECT_THREADED
    VM_NEXT();
#else
    for (;;)
    {
        VM_FETCH();
        switch (opv)
        {
#endif

/* ---- 杂项 ---- */
VM_CASE(OP_Nop)
{
    FETCH_OPS_ABC();
    VM_NEXT();
}
VM_CASE(OP_Halt)
{
    vm->steps = steps;
    vm->error_func = fr->func_index;
    vm->error_pc = (int32_t)op_index;
    return ECS_OK;
}

/* ---- 常量与移动 ---- */
VM_CASE(OP_LoadI)
{
    FETCH_OPS_SBXI();
    store_fresh(vm, &R[a_], make_int(b_));
    VM_NEXT();
}
VM_CASE(OP_LoadK)
{
    FETCH_OPS_ABX();
    const_ent *k = &vm->consts[b_];
    switch (k->tag)
    {
        /* S-20：常量串 → pinned（零拷贝引用镜像、零分配、不参与 RC） */
        case ECS_STRING: store_fresh(vm, &R[a_], make_static_str(b_)); break;
        case ECS_UINT: store_fresh(vm, &R[a_], void_value()); R[a_].tag = ECS_UINT; R[a_].u32 = (uint32_t)k->i64; break;
        case ECS_DOUBLE: case ECS_UINT64: case ECS_PTR:
        {
            /* 宽常量装箱：LoadK 自此有界失败（块耗尽 → POOL） */
            int perr = 0;
            ecs_value v = (k->tag == ECS_DOUBLE) ? box_double(vm, k->f64, &perr)
                        : (k->tag == ECS_UINT64) ? box_u64(vm, k->i64, &perr)
                        : box_ptr(vm, k->i64, &perr);
            if (perr) VM_FAIL(ECS_ERR_POOL);
            store_fresh(vm, &R[a_], v);
            break;
        }
        default: store_fresh(vm, &R[a_], make_int((int32_t)k->i64)); break;
    }
    VM_NEXT();
}
VM_CASE(OP_LoadBool)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(b_ != 0));
    VM_NEXT();
}
VM_CASE(OP_Move)
{
    FETCH_OPS_ABC();
    move_to(vm, &R[a_], R[b_]);
    VM_NEXT();
}
VM_CASE(OP_SetVar)
{
    FETCH_OPS_ABC();
    ecs_value copied = deep_copy_cow(vm, R[b_]);
    if (R[b_].tag != ECS_VOID && copied.tag == ECS_VOID) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], copied);   /* S-01 + COW；出生引用直写 */
    VM_NEXT();
}
VM_CASE(OP_LoadG)
{
    FETCH_OPS_ABX();
    move_to(vm, &R[a_], vm->globals[b_]);
    VM_NEXT();
}
VM_CASE(OP_StoreG)
{
    FETCH_OPS_ABX();
    ecs_value copied = deep_copy_cow(vm, R[a_]);
    if (R[a_].tag != ECS_VOID && copied.tag == ECS_VOID) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &vm->globals[b_], copied);   /* COW；出生引用直写 */
    VM_NEXT();
}

/* ---- 算术（S-02/S-05；无符号回绕对齐 C# unchecked） ---- */
VM_CASE(OP_AddI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_int((int32_t)((uint32_t)R[b_].i32 + (uint32_t)R[c_].i32)));
    VM_NEXT();
}
VM_CASE(OP_SubI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_int((int32_t)((uint32_t)R[b_].i32 - (uint32_t)R[c_].i32)));
    VM_NEXT();
}
VM_CASE(OP_MulI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_int((int32_t)((uint32_t)R[b_].i32 * (uint32_t)R[c_].i32)));
    VM_NEXT();
}
VM_CASE(OP_DivI)
{
    FETCH_OPS_ABC();
    if (R[c_].i32 == 0) VM_FAIL(ECS_ERR_DIVZERO);
    store_fresh(vm, &R[a_], make_int(R[c_].i32 == -1 ? (int32_t)(0u - (uint32_t)R[b_].i32) : R[b_].i32 / R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_ModI)
{
    FETCH_OPS_ABC();
    if (R[c_].i32 == 0) VM_FAIL(ECS_ERR_DIVZERO);
    store_fresh(vm, &R[a_], make_int(R[c_].i32 == -1 ? 0 : R[b_].i32 % R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_RDivI)   /* S-05：`（a + b/2) / b` */
{
    FETCH_OPS_ABC();
    if (R[c_].i32 == 0) VM_FAIL(ECS_ERR_DIVZERO);
    int32_t num = (int32_t)((uint32_t)R[b_].i32 + (uint32_t)(R[c_].i32 / 2));
    store_fresh(vm, &R[a_], make_int(R[c_].i32 == -1 ? (int32_t)(0u - (uint32_t)num) : num / R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_AddU)
{
    FETCH_OPS_ABC();
    ecs_value r = void_value(); r.tag = ECS_UINT; r.u32 = R[b_].u32 + R[c_].u32;
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_SubU)
{
    FETCH_OPS_ABC();
    ecs_value r = void_value(); r.tag = ECS_UINT; r.u32 = R[b_].u32 - R[c_].u32;
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_MulU)
{
    FETCH_OPS_ABC();
    ecs_value r = void_value(); r.tag = ECS_UINT; r.u32 = R[b_].u32 * R[c_].u32;
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_DivU)
{
    FETCH_OPS_ABC();
    if ((uint32_t)R[c_].i32 == 0) VM_FAIL(ECS_ERR_DIVZERO);
    ecs_value r = void_value(); r.tag = ECS_UINT; r.u32 = R[b_].u32 / R[c_].u32;
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_ModU)
{
    FETCH_OPS_ABC();
    if ((uint32_t)R[c_].i32 == 0) VM_FAIL(ECS_ERR_DIVZERO);
    ecs_value r = void_value(); r.tag = ECS_UINT; r.u32 = R[b_].u32 % R[c_].u32;
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_AddL)
{
    FETCH_OPS_ABC();
    int perr = 0;
    ecs_value r = box_u64(vm, (int64_t)((uint64_t)box_l(vm, R[b_]) + (uint64_t)box_l(vm, R[c_])), &perr);
    if (perr) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_SubL)
{
    FETCH_OPS_ABC();
    int perr = 0;
    ecs_value r = box_u64(vm, (int64_t)((uint64_t)box_l(vm, R[b_]) - (uint64_t)box_l(vm, R[c_])), &perr);
    if (perr) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_MulL)
{
    FETCH_OPS_ABC();
    int perr = 0;
    ecs_value r = box_u64(vm, (int64_t)((uint64_t)box_l(vm, R[b_]) * (uint64_t)box_l(vm, R[c_])), &perr);
    if (perr) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_DivL)
{
    FETCH_OPS_ABC();
    int64_t l = box_l(vm, R[b_]), c = box_l(vm, R[c_]);
    if (c == 0) VM_FAIL(ECS_ERR_DIVZERO);
    int perr = 0;
    ecs_value r = box_u64(vm, c == -1 ? (int64_t)(0ULL - (uint64_t)l) : l / c, &perr);
    if (perr) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_ModL)
{
    FETCH_OPS_ABC();
    int64_t l = box_l(vm, R[b_]), c = box_l(vm, R[c_]);
    if (c == 0) VM_FAIL(ECS_ERR_DIVZERO);
    int perr = 0;
    ecs_value r = box_u64(vm, c == -1 ? 0 : l % c, &perr);
    if (perr) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_AddD)
{
    FETCH_OPS_ABC();
    int perr = 0;
    ecs_value r = box_double(vm, box_d(vm, R[b_]) + box_d(vm, R[c_]), &perr);
    if (perr) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_SubD)
{
    FETCH_OPS_ABC();
    int perr = 0;
    ecs_value r = box_double(vm, box_d(vm, R[b_]) - box_d(vm, R[c_]), &perr);
    if (perr) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_MulD)
{
    FETCH_OPS_ABC();
    int perr = 0;
    ecs_value r = box_double(vm, box_d(vm, R[b_]) * box_d(vm, R[c_]), &perr);
    if (perr) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_DivD)
{
    FETCH_OPS_ABC();
    int perr = 0;
    ecs_value r = box_double(vm, box_d(vm, R[b_]) / box_d(vm, R[c_]), &perr);
    if (perr) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}

/* ---- 位运算（S-03 掩码；左移用无符号回绕） ---- */
VM_CASE(OP_BandI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_int(R[b_].i32 & R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_BorI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_int(R[b_].i32 | R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_BxorI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_int(R[b_].i32 ^ R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_ShlI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_int((int32_t)((uint32_t)R[b_].i32 << (R[c_].i32 & 31))));
    VM_NEXT();
}
VM_CASE(OP_ShrI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_int(R[b_].i32 >> (R[c_].i32 & 31)));
    VM_NEXT();
}
VM_CASE(OP_BnotI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_int((int32_t)(~(uint32_t)R[b_].i32)));
    VM_NEXT();
}

/* ---- 比较 ---- */
VM_CASE(OP_EqI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(R[b_].i32 == R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_LtI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(R[b_].i32 < R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_LeI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(R[b_].i32 <= R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_GtI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(R[b_].i32 > R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_GeI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(R[b_].i32 >= R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_EqU)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool((uint32_t)R[b_].i32 == (uint32_t)R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_LtU)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool((uint32_t)R[b_].i32 < (uint32_t)R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_LeU)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool((uint32_t)R[b_].i32 <= (uint32_t)R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_GtU)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool((uint32_t)R[b_].i32 > (uint32_t)R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_GeU)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool((uint32_t)R[b_].i32 >= (uint32_t)R[c_].i32));
    VM_NEXT();
}
VM_CASE(OP_EqD)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(cmp_double(box_d(vm, R[b_]), box_d(vm, R[c_])) == 0));
    VM_NEXT();
}
VM_CASE(OP_LtD)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(cmp_double(box_d(vm, R[b_]), box_d(vm, R[c_])) < 0));
    VM_NEXT();
}
VM_CASE(OP_LeD)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(cmp_double(box_d(vm, R[b_]), box_d(vm, R[c_])) <= 0));
    VM_NEXT();
}
VM_CASE(OP_GtD)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(cmp_double(box_d(vm, R[b_]), box_d(vm, R[c_])) > 0));
    VM_NEXT();
}
VM_CASE(OP_GeD)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(cmp_double(box_d(vm, R[b_]), box_d(vm, R[c_])) >= 0));
    VM_NEXT();
}
VM_CASE(OP_EqL)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(box_l(vm, R[b_]) == box_l(vm, R[c_])));
    VM_NEXT();
}
VM_CASE(OP_LtL)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(box_l(vm, R[b_]) < box_l(vm, R[c_])));
    VM_NEXT();
}
VM_CASE(OP_LeL)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(box_l(vm, R[b_]) <= box_l(vm, R[c_])));
    VM_NEXT();
}
VM_CASE(OP_GtL)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(box_l(vm, R[b_]) > box_l(vm, R[c_])));
    VM_NEXT();
}
VM_CASE(OP_GeL)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(box_l(vm, R[b_]) >= box_l(vm, R[c_])));
    VM_NEXT();
}
VM_CASE(OP_EqS)
{
    FETCH_OPS_ABC();
    const uint16_t *lx, *ly; int32_t llx, lly;
    str_or_null(vm, R[b_], &lx, &llx);
    str_or_null(vm, R[c_], &ly, &lly);
    store_fresh(vm, &R[a_], make_bool(units_equal(lx, llx, ly, lly)));
    VM_NEXT();
}
VM_CASE(OP_EqP)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(box_bits(vm, R[b_]) == box_bits(vm, R[c_])));   /* 64 位值相等，非句柄 */
    VM_NEXT();
}

/* ---- 一元与转换 ---- */
VM_CASE(OP_Not)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_bool(R[b_].i32 == 0));
    VM_NEXT();
}
VM_CASE(OP_NegI)
{
    FETCH_OPS_ABC();
    store_fresh(vm, &R[a_], make_int((int32_t)(0u - (uint32_t)R[b_].i32)));
    VM_NEXT();
}
VM_CASE(OP_NegD)
{
    FETCH_OPS_ABC();
    int perr = 0;
    ecs_value r = box_double(vm, -box_d(vm, R[b_]), &perr);
    if (perr) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}
VM_CASE(OP_Conv)
{
    FETCH_OPS_ABC();
    int err = 0;
    ecs_value r = do_conv(vm, (uint32_t)c_, R[b_], &err);
    if (err == 2) VM_FAIL(ECS_ERR_POOL);
    if (err) VM_FAIL(ECS_ERR_TYPE);
    store_fresh(vm, &R[a_], r);
    VM_NEXT();
}

/* ---- 控制流 ---- */
VM_CASE(OP_Jmp)
{
    FETCH_OPS_J();
    pc = op_start + op_size + (uint32_t)jmp_;
    VM_NEXT();
}
VM_CASE(OP_Jpt)
{
    FETCH_OPS_AJ();
    if (R[a_].i32 != 0) pc = op_start + op_size + (uint32_t)jmp_;
    VM_NEXT();
}
VM_CASE(OP_Jpf)
{
    FETCH_OPS_AJ();
    if (R[a_].i32 == 0) pc = op_start + op_size + (uint32_t)jmp_;
    VM_NEXT();
}

/* ---- 调用（EXT：目标 ID；接收槽 C 恒为寄存器，0 = RET） ---- */
VM_CASE(OP_Call)
{
    FETCH_OPS_WEXT();
    fr->ret_pc = pc;          /* 调用者续跑点（下一条指令）：仅调用边界写回 */
    fr->ret_idx = op_index + 1;
    int rc = push_frame(vm, &vm->funcs[ext_], (int32_t)ext_);
    if (rc != 0)
    {
        vm->steps = steps;
        vm->error_func = fr->func_index; vm->error_pc = (int32_t)op_index;
        return rc;
    }
    frame *nf = frame_at(vm, vm->frame_off[vm->depth - 1]);
    nf->ret_pc = 0;
    nf->ret_idx = 0;
    nf->ret_slot = (uint8_t)c_;
    for (int32_t i = 0; i < b_; i++)
    {
        ecs_value copied = deep_copy_cow(vm, R[a_ + i]);
        if (R[a_ + i].tag != ECS_VOID && copied.tag == ECS_VOID) VM_FAIL(ECS_ERR_POOL);
        store_fresh(vm, &nf->slots[ECS_REG_ARG_BASE + i], copied);   /* S-17 实参深拷贝 → ARG 窗 */
    }
    fr = nf; codebuf = fr->fn->code; cend = fr->fn->code_bytes; R = fr->slots; pc = 0; idx = fr->ret_idx;
    VM_NEXT();
}
VM_CASE(OP_CallN)
{
    FETCH_OPS_WEXT();
    /* 旗标置位 = 编号 syscall（VM2.md §9.1）；否则原生名表索引。
     * S-21：miss → 缺省值表降级，strict_caps 或表外 → 响亮。 */
    /* C99 栈快照按实际参数数分配（0 参数也保留合法数组长度），运行期不申请堆内存。 */
    ecs_value args[b_ > 0 ? b_ : 1];
    for (int32_t i = 0; i < b_; i++)
        args[i] = R[a_ + i];
    int nerr = 0;
    ecs_value ret = void_value();
    vm->host_error = 0;
    if ((ext_ & ECS_SYSCALL_FLAG) != 0)
    {
        int32_t sid = (int32_t)(ext_ & ~ECS_SYSCALL_FLAG);
        int handled = vm->host.syscall && vm->host.syscall(vm->host.ud, sid, args, b_, &ret) == 0;
        if (!handled && !vm->host_error)
        {
            release(vm, ret);
            ret = void_value();
            handled = !vm->host.strict_caps && ecs_cap_syscall_default(vm, sid, args, &ret);
        }
        nerr = !handled;
    }
    else
    {
        if (vm->host.native && vm->host.native(vm->host.ud, vm->natives[ext_], args, b_, &ret) == 0)
            nerr = 0;
        else
            nerr = 1;
    }
    if (vm->host_error || nerr)
    {
        release(vm, ret);
        VM_FAIL(vm->host_error ? vm->host_error : ECS_ERR_NOSUCHNATIVE);
    }
    store_fresh(vm, &R[c_], ret);   /* 接管 ret_* 出生引用；额外 retain 会泄漏每次宿主返回 */
    if (vm->cancel_flag)
    {
        vm->steps = steps;
        fr->ret_pc = pc; fr->ret_idx = op_index + 1;
        return ECS_CANCELLED;   /* 宿主调用后即时取消（PRINT/READ/WAIT 类长延迟感知点） */
    }
    VM_NEXT();
}
VM_CASE(OP_Ret)
{
    FETCH_OPS_ABC();
    /* C API 不导出顶层返回值；无需为将立即丢弃的值创建第二份引用。 */
    if (vm->depth == 1) { pop_frame(vm, fr); vm->steps = steps; return ECS_OK; }
    /* 拷贝到调用者接收槽（S-17；COW：唯一引用移交）；值须跨弹帧持有 */
    ecs_value copied = deep_copy_cow(vm, R[a_]);
    if (R[a_].tag != ECS_VOID && copied.tag == ECS_VOID) VM_FAIL(ECS_ERR_POOL);
    int32_t ret_slot = fr->ret_slot;
    pop_frame(vm, fr);
    if (vm->depth == 0) { vm->steps = steps; return ECS_OK; }
    fr = frame_at(vm, vm->frame_off[vm->depth - 1]);
    codebuf = fr->fn->code; cend = fr->fn->code_bytes; R = fr->slots; pc = fr->ret_pc; idx = fr->ret_idx;
    store_fresh(vm, &R[ret_slot], copied);   /* 出生引用直写（对齐 EcxInterpreter.Ret） */
    VM_NEXT();
}
VM_CASE(OP_Ret0)
{
    pop_frame(vm, fr);
    if (vm->depth == 0) { vm->steps = steps; return ECS_OK; }
    fr = frame_at(vm, vm->frame_off[vm->depth - 1]);
    codebuf = fr->fn->code; cend = fr->fn->code_bytes; R = fr->slots; pc = fr->ret_pc; idx = fr->ret_idx;
    VM_NEXT();
}

/* ---- 数组 / 字符串 ---- */
VM_CASE(OP_NewArrV)
{
    FETCH_OPS_WEXT();
    int32_t h = hnew_arr(vm, (uint8_t)ext_, 0, b_);
    if (h < 0) VM_FAIL(ECS_ERR_POOL);
    ecs_obj *o = obj_at(vm, h);
    for (int32_t i = 0; i < b_; i++)
        arr_overwrite(vm, o, i, R[c_ + i]);
    store_fresh(vm, &R[a_], void_value()); R[a_].tag = ECS_ARRAY; R[a_].u32 = (uint32_t)h;
    VM_NEXT();
}
VM_CASE(OP_NewArrE)
{
    FETCH_OPS_ABX();
    int32_t h = hnew_arr(vm, (uint8_t)b_, 0, 0);
    if (h < 0) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], void_value()); R[a_].tag = ECS_ARRAY; R[a_].u32 = (uint32_t)h;
    VM_NEXT();
}
VM_CASE(OP_GetI)
{
    FETCH_OPS_ABC();
    ecs_value container = R[b_];
    int32_t ix = R[c_].i32;
    if (container.tag == ECS_STRING)
    {
        const uint16_t *u; int32_t len;
        str_or_null(vm, container, &u, &len);
        if (ix < 0 || ix >= len) VM_FAIL(ECS_ERR_INDEX);
        ecs_value result = new_str_units(vm, u ? u + ix : NULL, 1);
        if (result.tag == ECS_VOID) VM_FAIL(ECS_ERR_POOL);
        store_fresh(vm, &R[a_], result);
    }
    else
    {
        ecs_obj *arr = heap_obj(vm, (int32_t)container.u32);
        if (!arr || container.tag != ECS_ARRAY) VM_FAIL(ECS_ERR_TYPE);
        if (ix < 0 || ix >= arr->h.len) VM_FAIL(ECS_ERR_INDEX);
        /* S-10 按 elem tag 归一读（宽元素装箱，块耗尽 → POOL） */
        int aerr = 0;
        ecs_value item = arr_get(vm, arr, ix, &aerr);
        if (aerr) VM_FAIL(ECS_ERR_POOL);
        if (OBJ_ETAG(arr->h.flags) == ECS_BYTE) { item.tag = ECS_BYTE; item.i32 &= 0xFF; }
        else if (OBJ_ETAG(arr->h.flags) == ECS_BOOL) { item = make_bool(item.i32 != 0); }
        if (is_boxed_tag(item.tag)) store_fresh(vm, &R[a_], item);   /* arr_get 创建出生引用 */
        else move_to(vm, &R[a_], item);
    }
    VM_NEXT();
}
VM_CASE(OP_SetI)
{
    FETCH_OPS_ABC();
    ecs_obj *arr = heap_obj(vm, (int32_t)R[b_].u32);
    if (!arr || R[b_].tag != ECS_ARRAY) VM_FAIL(ECS_ERR_TYPE);
    int32_t ix = R[c_].i32;
    if (ix < 0 || ix >= arr->h.len) VM_FAIL(ECS_ERR_INDEX);
    ecs_value v = R[a_];
    if (OBJ_ETAG(arr->h.flags) == ECS_BOOL) v = make_bool(v.i32 != 0);   /* CoerceWrite */
    arr_overwrite(vm, arr, ix, v);
    VM_NEXT();
}
VM_CASE(OP_Slice)
{
    FETCH_OPS_WEXT();
    ecs_value container = R[b_];
    int32_t start = R[c_].i32;
    if (container.tag == ECS_STRING)
    {
        const uint16_t *u; int32_t len;
        str_or_null(vm, container, &u, &len);
        int32_t end = ext_ == 0xFFFFFFFFu ? len : R[(int32_t)ext_].i32;
        if (start < 0 || start > len || end > len || start > end) VM_FAIL(ECS_ERR_INDEX);
        ecs_value result = new_str_units(vm, u ? u + start : NULL, end - start);
        if (result.tag == ECS_VOID) VM_FAIL(ECS_ERR_POOL);
        store_fresh(vm, &R[a_], result);
    }
    else
    {
        ecs_obj *arr = heap_obj(vm, (int32_t)container.u32);
        if (!arr || container.tag != ECS_ARRAY) VM_FAIL(ECS_ERR_TYPE);
        int32_t end = ext_ == 0xFFFFFFFFu ? arr->h.len : R[(int32_t)ext_].i32;
        if (start < 0 || start > arr->h.len || end > arr->h.len || start > end) VM_FAIL(ECS_ERR_INDEX);
        int32_t h = hnew_arr(vm, OBJ_ETAG(arr->h.flags), arr->h.sid, end - start);
        if (h < 0) VM_FAIL(ECS_ERR_POOL);
        ecs_obj *dst = obj_at(vm, h);
        for (int32_t i = start; i < end; i++)
            arr_copy_item(vm, dst, i - start, arr, i);
        store_fresh(vm, &R[a_], void_value()); R[a_].tag = ECS_ARRAY; R[a_].u32 = (uint32_t)h;
    }
    VM_NEXT();
}
VM_CASE(OP_Cont)   /* S-09 */
{
    FETCH_OPS_ABC();
    ecs_value item = R[b_];
    ecs_value container = R[c_];
    if (container.tag == ECS_STRING)
    {
        const uint16_t *hay; int32_t hlen;
        str_or_null(vm, container, &hay, &hlen);
        if (item.tag != ECS_STRING) VM_FAIL(ECS_ERR_TYPE);
        const uint16_t *sub; int32_t slen;
        str_or_null(vm, item, &sub, &slen);
        store_fresh(vm, &R[a_], make_bool(units_contains(hay, hlen, sub, slen)));
    }
    else if (container.tag == ECS_ARRAY)
    {
        ecs_obj *arr = heap_obj(vm, (int32_t)container.u32);
        if (!arr) VM_FAIL(ECS_ERR_TYPE);
        if (item.tag != OBJ_ETAG(arr->h.flags))
            store_fresh(vm, &R[a_], make_bool(0));   /* 元素类型不匹配 → false */
        else
        {
            int found = 0;
            for (int32_t i = 0; i < arr->h.len; i++)
            {
                int aerr = 0;
                ecs_value el = arr_get(vm, arr, i, &aerr);
                if (aerr) VM_FAIL(ECS_ERR_POOL);
                int equal = value_equals(vm, el, item);
                if (is_boxed_tag(el.tag)) release(vm, el);
                if (equal) { found = 1; break; }
            }
            store_fresh(vm, &R[a_], make_bool(found));
        }
    }
    else
        VM_FAIL(ECS_ERR_TYPE);
    VM_NEXT();
}
VM_CASE(OP_Append)
{
    FETCH_OPS_ABC();
    ecs_obj *src = heap_obj(vm, (int32_t)R[b_].u32);
    if (!src || R[b_].tag != ECS_ARRAY) VM_FAIL(ECS_ERR_TYPE);
    int32_t h = hnew_arr(vm, OBJ_ETAG(src->h.flags), src->h.sid, src->h.len + 1);
    if (h < 0) VM_FAIL(ECS_ERR_POOL);
    ecs_obj *dst = obj_at(vm, h);
    for (int32_t i = 0; i < src->h.len; i++)
        arr_copy_item(vm, dst, i, src, i);
    arr_overwrite(vm, dst, src->h.len, R[c_]);
    store_fresh(vm, &R[a_], void_value()); R[a_].tag = ECS_ARRAY; R[a_].u32 = (uint32_t)h;
    VM_NEXT();
}
VM_CASE(OP_Cat)   /* S-11 */
{
    FETCH_OPS_ABC();
    ecs_value l = R[b_], r = R[c_];
    if (l.tag == ECS_STRING || r.tag == ECS_STRING)
    {
        strbuf sb; memset(&sb, 0, sizeof(sb));
        tostring_top(vm, &sb, l);
        tostring_top(vm, &sb, r);
        if (sb.overflow) VM_FAIL(ECS_ERR_POOL);
        ecs_value result = new_str_units(vm, sb.units, sb.len);
        if (result.tag == ECS_VOID) VM_FAIL(ECS_ERR_POOL);
        store_fresh(vm, &R[a_], result);
    }
    else if (l.tag == ECS_ARRAY && r.tag == ECS_ARRAY)
    {
        ecs_obj *la = heap_obj(vm, (int32_t)l.u32);
        ecs_obj *ra = heap_obj(vm, (int32_t)r.u32);
        if (!la || !ra) VM_FAIL(ECS_ERR_TYPE);
        if (OBJ_ETAG(la->h.flags) != OBJ_ETAG(ra->h.flags)) VM_FAIL(ECS_ERR_TYPE);
        int32_t h = hnew_arr(vm, OBJ_ETAG(la->h.flags), la->h.sid, la->h.len + ra->h.len);
        if (h < 0) VM_FAIL(ECS_ERR_POOL);
        ecs_obj *dst = obj_at(vm, h);
        for (int32_t i = 0; i < la->h.len; i++)
            arr_copy_item(vm, dst, i, la, i);
        for (int32_t i = 0; i < ra->h.len; i++)
            arr_copy_item(vm, dst, la->h.len + i, ra, i);
        store_fresh(vm, &R[a_], void_value()); R[a_].tag = ECS_ARRAY; R[a_].u32 = (uint32_t)h;
    }
    else
        VM_FAIL(ECS_ERR_TYPE);
    VM_NEXT();
}
VM_CASE(OP_Len)
{
    FETCH_OPS_ABC();
    if (R[b_].tag == ECS_STRING)
    {
        const uint16_t *u; int32_t len;
        str_or_null(vm, R[b_], &u, &len);
        store_fresh(vm, &R[a_], make_int(len));
    }
    else if (R[b_].tag == ECS_ARRAY)
    {
        ecs_obj *arr = heap_obj(vm, (int32_t)R[b_].u32);
        if (!arr) VM_FAIL(ECS_ERR_TYPE);
        store_fresh(vm, &R[a_], make_int(arr->h.len));
    }
    else VM_FAIL(ECS_ERR_TYPE);
    VM_NEXT();
}

/* ---- 结构体 ---- */
VM_CASE(OP_NewSt)
{
    FETCH_OPS_ABX();
    int32_t sid = b_;
    if (sid >= vm->nstructs) VM_FAIL(ECS_ERR_SLOT);
    int32_t h = hnew_st(vm, &vm->structs[sid]);
    if (h < 0) VM_FAIL(ECS_ERR_POOL);
    store_fresh(vm, &R[a_], void_value()); R[a_].tag = ECS_STRUCT; R[a_].u32 = (uint32_t)h;
    VM_NEXT();
}
VM_CASE(OP_GetF)
{
    FETCH_OPS_ABC();
    ecs_obj *st = heap_obj(vm, (int32_t)R[b_].u32);
    if (!st || R[b_].tag != ECS_STRUCT) VM_FAIL(ECS_ERR_TYPE);
    const ecs_structdef *def = &vm->structs[st->h.sid];
    if (c_ >= def->nfields) VM_FAIL(ECS_ERR_SLOT);
    const ecs_fielddef *f = &def->fields[c_];
    ecs_value *slots = (ecs_value *)OBJ_PAYLOAD(st);
    do
    {
        switch (f->kind)
        {
            case 0:   /* Scalar */
                if (f->type == ECS_BYTE) { store_fresh(vm, &R[a_], slots[f->slot_offset]); R[a_].tag = ECS_BYTE; R[a_].i32 &= 0xFF; }
                else if (f->type == ECS_BOOL) store_fresh(vm, &R[a_], make_bool(slots[f->slot_offset].i32 != 0));
                else { move_to(vm, &R[a_], slots[f->slot_offset]); }
                break;
            case FIELD_KIND_FIXED_ARRAY:
            {
                int32_t h = hnew_arr(vm, f->elem, 0, f->ext);
                if (h < 0) VM_FAIL(ECS_ERR_POOL);
                ecs_obj *dst = obj_at(vm, h);
                for (int32_t i = 0; i < f->ext; i++)
                    arr_overwrite(vm, dst, i, slots[f->slot_offset + i]);
                store_fresh(vm, &R[a_], void_value()); R[a_].tag = ECS_ARRAY; R[a_].u32 = (uint32_t)h;
                break;
            }
            case FIELD_KIND_NESTED:
            {
                const ecs_structdef *nested = &vm->structs[f->ext];
                int32_t h = hnew_st(vm, nested);
                if (h < 0) VM_FAIL(ECS_ERR_POOL);
                ecs_obj *dst = obj_at(vm, h);
                ecs_value *dslots = (ecs_value *)OBJ_PAYLOAD(dst);
                for (int32_t i = 0; i < nested->nslots; i++)
                {
                    dslots[i] = slots[f->slot_offset + i];
                    retain(vm, dslots[i]);
                }
                /* 写穿透视图（对齐 v1 GetNested / 解释器 ViewParent）：记录父链，
                   后续槽写入经 sync_view 逐级回写父槽区 */
                dst->h.view_parent = (int32_t)R[b_].u32;
                dst->h.view_offset = f->slot_offset;
                retain(vm, R[b_]);
                store_fresh(vm, &R[a_], void_value()); R[a_].tag = ECS_STRUCT; R[a_].u32 = (uint32_t)h;
                break;
            }
            default: VM_FAIL(ECS_ERR_TYPE);   /* Boxed 不受支持（S-16） */
        }
    } while (0);   /* 内层 switch：break 语义隔离（线程化模式无外层 switch） */
    VM_NEXT();
}
VM_CASE(OP_PutF)
{
    FETCH_OPS_ABC();
    ecs_obj *st = heap_obj(vm, (int32_t)R[b_].u32);
    if (!st || R[b_].tag != ECS_STRUCT) VM_FAIL(ECS_ERR_TYPE);
    const ecs_structdef *def = &vm->structs[st->h.sid];
    if (c_ >= def->nfields) VM_FAIL(ECS_ERR_SLOT);
    const ecs_fielddef *f = &def->fields[c_];
    ecs_value *slots = (ecs_value *)OBJ_PAYLOAD(st);
    if (f->kind == 0)
    {
        ecs_value v = R[a_];
        if (f->type == ECS_BOOL) v = make_bool(v.i32 != 0);
        move_to(vm, &slots[f->slot_offset], v);
        if (st->h.view_parent) sync_view(vm, st);
    }
    else if (f->kind == FIELD_KIND_NESTED)
    {
        ecs_obj *src = heap_obj(vm, (int32_t)R[a_].u32);
        if (!src || R[a_].tag != ECS_STRUCT) VM_FAIL(ECS_ERR_TYPE);
        /* 对齐校验：src 槽数 vs 字段嵌套类型槽数（与 GetF 取嵌套布局一致） */
        const ecs_structdef *nested_put = &vm->structs[f->ext];
        const ecs_structdef *src_def = &vm->structs[src->h.sid];
        if (src_def->nslots != nested_put->nslots) VM_FAIL(ECS_ERR_TYPE);
        ecs_value *sslots = (ecs_value *)OBJ_PAYLOAD(src);
        for (int32_t i = 0; i < src_def->nslots; i++)
        {
            release(vm, slots[f->slot_offset + i]);
            slots[f->slot_offset + i] = sslots[i];
            retain(vm, slots[f->slot_offset + i]);
        }
        if (st->h.view_parent) sync_view(vm, st);
    }
    else VM_FAIL(ECS_ERR_TYPE);
    VM_NEXT();
}
VM_CASE(OP_GetFI)
{
    FETCH_OPS_WEXT();
    ecs_obj *st = heap_obj(vm, (int32_t)R[b_].u32);
    if (!st || R[b_].tag != ECS_STRUCT) VM_FAIL(ECS_ERR_TYPE);
    const ecs_structdef *def = &vm->structs[st->h.sid];
    if (c_ >= def->nfields) VM_FAIL(ECS_ERR_SLOT);
    const ecs_fielddef *f = &def->fields[c_];
    if (f->kind != FIELD_KIND_FIXED_ARRAY) VM_FAIL(ECS_ERR_TYPE);
    int32_t idx = R[(int32_t)ext_].i32;
    if (idx < 0 || idx >= f->ext) VM_FAIL(ECS_ERR_INDEX);
    ecs_value item = ((ecs_value *)OBJ_PAYLOAD(st))[f->slot_offset + idx];
    if (f->type == ECS_BYTE) { item.tag = ECS_BYTE; item.i32 &= 0xFF; }
    else if (f->type == ECS_BOOL) item = make_bool(item.i32 != 0);
    move_to(vm, &R[a_], item);
    VM_NEXT();
}
VM_CASE(OP_PutFI)
{
    FETCH_OPS_WEXT();
    ecs_obj *st = heap_obj(vm, (int32_t)R[b_].u32);
    if (!st || R[b_].tag != ECS_STRUCT) VM_FAIL(ECS_ERR_TYPE);
    const ecs_structdef *def = &vm->structs[st->h.sid];
    if (c_ >= def->nfields) VM_FAIL(ECS_ERR_SLOT);
    const ecs_fielddef *f = &def->fields[c_];
    if (f->kind != FIELD_KIND_FIXED_ARRAY) VM_FAIL(ECS_ERR_TYPE);
    int32_t idx = R[(int32_t)ext_].i32;
    if (idx < 0 || idx >= f->ext) VM_FAIL(ECS_ERR_INDEX);
    ecs_value v = R[a_];
    if (f->type == ECS_BOOL) v = make_bool(v.i32 != 0);
    ecs_value *slots = (ecs_value *)OBJ_PAYLOAD(st);
    release(vm, slots[f->slot_offset + idx]);
    slots[f->slot_offset + idx] = v;
    retain(vm, v);
    if (st->h.view_parent) sync_view(vm, st);
    VM_NEXT();
}

/* ---- 域操作（宿主回调） ---- */
VM_CASE(OP_WaitI)
{
    FETCH_OPS_WEXT();
    if (vm->host.wait_ms) vm->host.wait_ms(vm->host.ud, (int32_t)ext_);
    if (vm->wait_yield || vm->cancel_flag)   /* 取消优先；续跑点 = 本指令后继 */
    {
        fr->ret_pc = pc;
        fr->ret_idx = op_index + 1;
        vm->steps = 0;
        return vm->cancel_flag ? ECS_CANCELLED : ECS_WAITING;
    }
    VM_NEXT();
}
VM_CASE(OP_WaitV)
{
    FETCH_OPS_ABC();
    if (vm->host.wait_ms) vm->host.wait_ms(vm->host.ud, R[a_].i32);
    if (vm->wait_yield || vm->cancel_flag)
    {
        fr->ret_pc = pc;
        fr->ret_idx = op_index + 1;
        vm->steps = 0;
        return vm->cancel_flag ? ECS_CANCELLED : ECS_WAITING;
    }
    VM_NEXT();
}
VM_CASE(OP_KeyI)
{
    FETCH_OPS_WEXT();
    if (vm->host.key) vm->host.key(vm->host.ud, (uint8_t)a_, (int32_t)ext_);
    VM_NEXT();
}
VM_CASE(OP_KeyV)
{
    FETCH_OPS_ABC();
    if (vm->host.key) vm->host.key(vm->host.ud, (uint8_t)a_, R[b_].i32);
    VM_NEXT();
}
VM_CASE(OP_KeySt)
{
    FETCH_OPS_ABC();
    if (vm->host.key_state) vm->host.key_state(vm->host.ud, (uint8_t)a_, b_);
    VM_NEXT();
}
VM_CASE(OP_StickSet)
{
    FETCH_OPS_ABC();
    if (vm->host.stick_set) vm->host.stick_set(vm->host.ud, (uint8_t)a_, b_, c_);
    VM_NEXT();
}
VM_CASE(OP_StickP)
{
    FETCH_OPS_WEXT();
    if (vm->host.stick_click)
        vm->host.stick_click(vm->host.ud, (uint8_t)a_, b_, c_, (int32_t)ext_);
    VM_NEXT();
}
VM_CASE(OP_StickPv)
{
    FETCH_OPS_WEXT();
    if (vm->host.stick_click)
        vm->host.stick_click(vm->host.ud, (uint8_t)a_, (int32_t)(ext_ & 0xFF), (int32_t)((ext_ >> 16) & 0xFF), R[c_].i32);
    VM_NEXT();
}
VM_CASE(OP_Img)
{
    FETCH_OPS_ABX();
    /* S-21：图像标签能力缺失 → 目标槽 ← -1（与 C# EcxHost 缺省 ImgLabel 一致）；
     * strict_caps 宿主恢复响亮（IL）。 */
    if (vm->host.strict_caps)
        VM_FAIL(ECS_ERR_IL);
    store_fresh(vm, &R[a_], make_int(-1));
    VM_NEXT();
}
VM_CASE(OP_Rand)   /* S-12 */
{
    FETCH_OPS_ABC();
    int32_t max = R[b_].i32;
    if (max < 0) VM_FAIL(ECS_ERR_INDEX);
    int32_t v = 0;
    if (max != 0 && vm->host.rand) v = vm->host.rand(vm->host.ud, max);
    store_fresh(vm, &R[a_], make_int(v));
    VM_NEXT();
}

/* ---- FOR 快速路径 / 比较跳转融合 ---- */
VM_CASE(OP_ForStep)
{
    FETCH_OPS_WJ();
    /* FOR 快速路径：tmp=i+1 写 R[c]；tmp>limit → 跳 exit（数据字）；否则落入后继 */
    int32_t next = (int32_t)((uint32_t)R[a_].i32 + 1u);
    store_fresh(vm, &R[c_], make_int(next));
    if (next > R[b_].i32)
        pc = op_start + op_size + (uint32_t)rd_u32(codebuf + op_start + 4);   /* 出口字延迟读 */
    VM_NEXT();
}
VM_CASE(OP_CmpJ)   /* 比较跳转融合（P2′）：kind=typeBlock*6+op；数据字 = 跳转字节偏移 */
{
    FETCH_OPS_WJ();
    int taken;
    switch (kind_block(c_))
    {
        case 0: { int32_t l = R[a_].i32, r = R[b_].i32; taken = cmp_kind6_i32(l, r, c_); } break;
        case 1: { uint32_t l = (uint32_t)R[a_].i32, r = (uint32_t)R[b_].i32; taken = cmp_kind6_u32(l, r, c_); } break;
        case 2: { double l = box_d(vm, R[a_]), r = box_d(vm, R[b_]); taken = cmp_kind6_f64(l, r, c_); } break;
        default: { int64_t l = box_l(vm, R[a_]), r = box_l(vm, R[b_]); taken = cmp_kind6_i64(l, r, c_); } break;
    }
    if (taken)
        pc = op_start + op_size + (uint32_t)rd_u32(codebuf + op_start + 4);
    VM_NEXT();
}

#if !ECS_DIRECT_THREADED
        default:
            VM_FAIL(ECS_ERR_OPCODE);
        }   /* switch (opv) */
    }   /* for ;; */
#endif

#undef VM_FAIL
#undef VM_NEXT
#undef VM_CASE
#if ECS_DIRECT_THREADED
#undef VM_FETCH_TH
#undef FETCH_OPS_ABC
#undef FETCH_OPS_ABX
#undef FETCH_OPS_SBXI
#undef FETCH_OPS_AJ
#undef FETCH_OPS_J
#undef FETCH_OPS_WJ
#undef FETCH_OPS_WEXT
#endif
}
