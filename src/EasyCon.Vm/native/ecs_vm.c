/*
 * ecs_vm.c — VM2 纯 C 精简虚拟机实现—— 语义契约见 docs/VmSemanticContract.md（S-01..S-19）。
 *
 * 语义基准：EcxInterpreter（C# 模拟解释器，逐条对照；含 .NET 总序比较/饱和转换/无符号回绕）。
 * 内部五区段：镜像解析（§4.1）/ 堆（§4.2）/ 解释器主循环（§4.3）/ 结构体布局（§4.4）/ 核心原生表（§4.5）。
 * C99，仅依赖 libc + libm。
 */
#include "ecs_vm.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <math.h>

#ifndef ECS_MAX_CALL_DEPTH
#define ECS_MAX_CALL_DEPTH 512
#endif

#define ECS_DEFAULT_BUDGET 1000000
#define ECS_NO_SLOT (-1)
#define ECS_RECEIVE_NONE 255
#define ECS_IMPORT_FLAG 0x80000000u
#define FIELD_KIND_FIXED_ARRAY 1
#define FIELD_KIND_NESTED 2

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

typedef struct {
    uint8_t etag;
    int32_t len, cap;
    ecs_value *items;
} arr_data;

typedef struct {
    const ecs_structdef *def;
    ecs_value *slots;
    int32_t view_parent;          /* 嵌套视图：父结构体句柄（0=非视图）；句柄 0 保留为 null，无歧义 */
    int32_t view_offset;          /* 嵌套视图：本视图槽区在父槽区的起始偏移 */
} st_data;

struct ecs_obj {
    uint8_t kind;                 /* ECS_STRING / ECS_ARRAY / ECS_STRUCT */
    int32_t rc;
    union {
        struct { uint16_t *units; int32_t len, cap; } str;
        arr_data arr;
        st_data st;
    };
};

typedef struct {
    char *name;                   /* 调试名（调试区剥离时为 NULL） */
    uint8_t nparams, nslots, hasret;
    const uint32_t *code;         /* 指向镜像缓冲（零拷贝，XIP 友好） */
    uint32_t words;
    uint32_t code_off;
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
    const uint16_t *units;        /* 字符串：指向镜像缓冲（零拷贝） */
    int32_t units_len;
} const_ent;

typedef struct {
    const ecs_funcdef *fn;
    int32_t func_index;
    uint32_t ret_pc;
    int32_t ret_slot;             /* ECS_NO_SLOT = 无接收槽 */
    ecs_value slots[];
} frame;

struct ecs_vm {
    ecs_host host;

    const uint8_t *img;           /* 借用；生命周期 = vm */
    size_t img_len;
    const_ent *consts;            int32_t nconsts;
    ecs_structdef *structs;       int32_t nstructs;
    ecs_value *globals;           int32_t nglobals;
    char **natives;               int32_t nnatives;
    ecs_funcdef *funcs;           int32_t nfuncs;
    int32_t entry;

    frame **frames;
    int32_t depth, frames_cap;

    ecs_obj **objs;
    int32_t objs_cap, objs_count;
    int32_t *free_list;
    int32_t free_count, free_cap;

    int32_t steps, budget;
    int cancel_flag;
    int32_t error_func, error_pc;
    int pending_break;            /* FWRITE 行断协议状态（S-14） */
};

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

static int is_handle_tag(uint8_t tag)
{
    return tag == ECS_STRING || tag == ECS_ARRAY || tag == ECS_STRUCT;
}

/* ============================================================
 * §4.2 堆
 * ============================================================ */

static int32_t heap_alloc_slot(ecs_vm *vm)
{
    if (vm->free_count > 0)
        return vm->free_list[--vm->free_count];
    if (vm->objs_count >= vm->objs_cap)
    {
        int32_t cap = vm->objs_cap ? vm->objs_cap * 2 : 64;
        ecs_obj **grown = (ecs_obj **)realloc(vm->objs, (size_t)cap * sizeof(ecs_obj *));
        if (!grown) return -1;
        vm->objs = grown;
        int32_t *grown_free = (int32_t *)realloc(vm->free_list, (size_t)cap * sizeof(int32_t));
        if (!grown_free) { free(grown); return -1; }
        vm->free_list = grown_free;
        vm->free_cap = cap;
        vm->objs_cap = cap;
    }
    int32_t h = vm->objs_count++;
    if (h == 0)
        h = vm->objs_count++;   /* 句柄 0 保留为 null（StrOrNull / release / retain 的零值语义） */
    vm->objs[h] = NULL;
    return h;
}

static ecs_obj *heap_obj(ecs_vm *vm, int32_t handle)
{
    if (handle <= 0 || handle >= vm->objs_count) return NULL;
    return vm->objs[handle];
}

static int32_t hnew_str(ecs_vm *vm, int32_t len)
{
    int32_t h = heap_alloc_slot(vm);
    if (h < 0) return -1;
    ecs_obj *o = (ecs_obj *)calloc(1, sizeof(ecs_obj));
    if (!o) return -1;
    o->kind = ECS_STRING;
    o->rc = 1;
    o->str.cap = len > 0 ? len : 0;
    if (o->str.cap > 0)
    {
        o->str.units = (uint16_t *)malloc((size_t)o->str.cap * sizeof(uint16_t));
        if (!o->str.units) { free(o); return -1; }
    }
    vm->objs[h] = o;
    return h;
}

static ecs_value new_str_units(ecs_vm *vm, const uint16_t *units, int32_t len)
{
    ecs_value v = void_value();
    int32_t h = hnew_str(vm, len);
    if (h < 0) return v;
    ecs_obj *o = vm->objs[h];
    if (len > 0 && units) memcpy(o->str.units, units, (size_t)len * sizeof(uint16_t));
    o->str.len = len;
    v.tag = ECS_STRING;
    v.i64 = h;
    return v;
}


static int32_t hnew_arr(ecs_vm *vm, uint8_t etag, int32_t cap)
{
    int32_t h = heap_alloc_slot(vm);
    if (h < 0) return -1;
    ecs_obj *o = (ecs_obj *)calloc(1, sizeof(ecs_obj));
    if (!o) return -1;
    o->kind = ECS_ARRAY;
    o->rc = 1;
    o->arr.etag = etag;
    o->arr.cap = cap > 0 ? cap : 0;
    if (o->arr.cap > 0)
    {
        o->arr.items = (ecs_value *)malloc((size_t)o->arr.cap * sizeof(ecs_value));
        if (!o->arr.items) { free(o); return -1; }
    }
    vm->objs[h] = o;
    return h;
}

static int32_t hnew_st(ecs_vm *vm, const ecs_structdef *def)
{
    int32_t h = heap_alloc_slot(vm);
    if (h < 0) return -1;
    ecs_obj *o = (ecs_obj *)calloc(1, sizeof(ecs_obj));
    if (!o) return -1;
    o->kind = ECS_STRUCT;
    o->rc = 1;
    o->st.def = def;
    /* 全零位 = 各类型零值（+0.0 / 句柄 0(null 串) / 整型 0）≈ 参考实现 ZeroFill */
    o->st.slots = (ecs_value *)calloc((size_t)(def->nslots > 0 ? def->nslots : 1), sizeof(ecs_value));
    if (!o->st.slots) { free(o); return -1; }
    vm->objs[h] = o;
    return h;
}

static void release(ecs_vm *vm, ecs_value v);
static void retain(ecs_vm *vm, ecs_value v)
{
    if (!is_handle_tag(v.tag)) return;
    ecs_obj *o = heap_obj(vm, (int32_t)v.i64);
    if (o) o->rc++;
}

static void release(ecs_vm *vm, ecs_value v)
{
    if (!is_handle_tag(v.tag)) return;
    ecs_obj *o = heap_obj(vm, (int32_t)v.i64);
    if (!o) return;
    if (--o->rc > 0) return;
    switch (o->kind)
    {
        case ECS_STRING: free(o->str.units); break;
        case ECS_ARRAY:
            for (int32_t i = 0; i < o->arr.len; i++)
                release(vm, o->arr.items[i]);
            free(o->arr.items);
            break;
        case ECS_STRUCT:
            for (int32_t i = 0; i < o->st.def->nslots; i++)
                release(vm, o->st.slots[i]);
            free(o->st.slots);
            if (o->st.view_parent)
            {
                ecs_value parent = void_value();
                parent.tag = ECS_STRUCT;
                parent.i64 = o->st.view_parent;
                release(vm, parent);   /* 归还视图持有的父引用 */
            }
            break;
    }
    vm->objs[(int32_t)v.i64] = NULL;
    vm->free_list[vm->free_count++] = (int32_t)v.i64;
    free(o);
}

static void move_to(ecs_vm *vm, ecs_value *slot, ecs_value v)
{
    release(vm, *slot);
    *slot = v;
    retain(vm, v);
}

/* 槽位写入「出生引用」值（新建对象/标量/深拷贝结果）：仅释放旧值、不 retain——
   出生引用即本槽位的引用（对齐 EcxInterpreter.StoreFresh）。 */
static void store_fresh(ecs_vm *vm, ecs_value *slot, ecs_value v)
{
    release(vm, *slot);
    *slot = v;
}

/* 嵌套视图写穿透（F3 后续项，对齐 EcxInterpreter.WriteStructSlot 的 ViewParent 链回写）：
   视图槽区是父槽切片的拷贝，槽写入后逐级回写父槽区；视图单向持有父引用，无环 */
static void sync_view(ecs_vm *vm, ecs_obj *obj)
{
    while (obj->st.view_parent)
    {
        ecs_obj *parent = heap_obj(vm, obj->st.view_parent);
        if (!parent) break;
        for (int32_t i = 0; i < obj->st.def->nslots; i++)
        {
            ecs_value *pslot = &parent->st.slots[obj->st.view_offset + i];
            release(vm, *pslot);
            *pslot = obj->st.slots[i];
            retain(vm, *pslot);
        }
        obj = parent;
    }
}

/* S-01：字符串共享；数组/结构体一层新容器 + 子项 retain */
static ecs_value deep_copy(ecs_vm *vm, ecs_value v)
{
    if (v.tag == ECS_ARRAY)
    {
        ecs_obj *src = heap_obj(vm, (int32_t)v.i64);
        if (!src) return void_value();
        int32_t h = hnew_arr(vm, src->arr.etag, src->arr.len);
        if (h < 0) return void_value();
        ecs_obj *dst = vm->objs[h];
        for (int32_t i = 0; i < src->arr.len; i++)
        {
            dst->arr.items[i] = src->arr.items[i];
            retain(vm, dst->arr.items[i]);
        }
        dst->arr.len = src->arr.len;
        ecs_value r = void_value(); r.tag = ECS_ARRAY; r.i64 = h;
        return r;
    }
    if (v.tag == ECS_STRUCT)
    {
        ecs_obj *src = heap_obj(vm, (int32_t)v.i64);
        if (!src) return void_value();
        int32_t h = hnew_st(vm, src->st.def);
        if (h < 0) return void_value();
        ecs_obj *dst = vm->objs[h];
        for (int32_t i = 0; i < src->st.def->nslots; i++)
        {
            dst->st.slots[i] = src->st.slots[i];
            retain(vm, dst->st.slots[i]);
        }
        ecs_value r = void_value(); r.tag = ECS_STRUCT; r.i64 = h;
        return r;
    }
    ecs_value r = v;
    retain(vm, r);
    return r;
}

/* COW（move-on-unique）：句柄唯一引用（rc==1，恒为出生临时槽持有）时共享移交，跳过容器
   深拷贝；否则维持 S-01 深拷贝。可观察值语义不变——变量/全局槽永不与另一变量/全局共享容器。 */
static ecs_value deep_copy_cow(ecs_vm *vm, ecs_value v)
{
    if (is_handle_tag(v.tag))
    {
        ecs_obj *o = heap_obj(vm, (int32_t)v.i64);
        if (o && o->rc == 1)
        {
            retain(vm, v);
            return v;
        }
    }
    return deep_copy(vm, v);
}

static void unify(ecs_vm *vm, ecs_value *slot, ecs_value v, int deep)
{
    if (deep)
    {
        ecs_value copied = deep_copy_cow(vm, v);
        move_to(vm, slot, copied);
    }
    else
    {
        move_to(vm, slot, v);
    }
}

/* ---- 字符串工具 ---- */

static void str_or_null(ecs_vm *vm, ecs_value v, const uint16_t **units, int32_t *len)
{
    if (v.tag != ECS_STRING) { *units = NULL; *len = 0; return; }
    if (v.i64 == 0) { *units = NULL; *len = 0; return; }
    ecs_obj *o = heap_obj(vm, (int32_t)v.i64);
    if (!o) { *units = NULL; *len = 0; return; }
    *units = o->str.units;
    *len = o->str.len;
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
 * S-07/S-08 TOSTR 两层上下文
 * ============================================================ */

typedef struct { uint16_t *units; int32_t len, cap; } strbuf;

static int sb_reserve(strbuf *sb, int32_t extra)
{
    if (sb->len + extra <= sb->cap) return 1;
    int32_t cap = sb->cap ? sb->cap : 32;
    while (cap < sb->len + extra) cap *= 2;
    uint16_t *grown = (uint16_t *)realloc(sb->units, (size_t)cap * sizeof(uint16_t));
    if (!grown) return 0;
    sb->units = grown;
    sb->cap = cap;
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
        case ECS_UINT64: snprintf(tmp, sizeof(tmp), "%llu", (unsigned long long)v.i64); sb_ascii(sb, tmp); break;
        case ECS_PTR: snprintf(tmp, sizeof(tmp), "%lld", (long long)v.i64); sb_ascii(sb, tmp); break;
        case ECS_DOUBLE: sb_double(sb, v.f64); break;
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
            ecs_obj *o = heap_obj(vm, (int32_t)v.i64);
            sb_ascii(sb, "[");
            if (o)
                for (int32_t i = 0; i < o->arr.len; i++)
                {
                    if (i > 0) sb_ascii(sb, ", ");
                    tostring_nested(vm, sb, o->arr.items[i]);
                }
            sb_ascii(sb, "]");
            break;
        }
        case ECS_STRUCT:
        {
            ecs_obj *o = heap_obj(vm, (int32_t)v.i64);
            sb_ascii(sb, "struct:");
            if (o && o->st.def && o->st.def->name) sb_ascii(sb, o->st.def->name);
            break;
        }
        case ECS_PTR: snprintf(tmp, sizeof(tmp), "0x%llX", (unsigned long long)v.i64); sb_ascii(sb, tmp); break;
        case ECS_BOOL: sb_ascii(sb, v.i32 ? "true" : "false"); break;
        case ECS_UINT64: snprintf(tmp, sizeof(tmp), "%llu", (unsigned long long)v.i64); sb_ascii(sb, tmp); break;
        case ECS_DOUBLE: sb_double(sb, v.f64); break;
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

static ecs_value tostring_value(ecs_vm *vm, ecs_value v)
{
    strbuf sb; memset(&sb, 0, sizeof(sb));
    tostring_top(vm, &sb, v);
    ecs_value r = new_str_units(vm, sb.units, sb.len);
    free(sb.units);
    return r;
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
        return x.f64 == y.f64;
    return x.i64 == y.i64;
}

/* ============================================================
 * §4.1 镜像解析 + §4.4 结构体布局展开
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

int ecs_vm_load(ecs_vm *vm, const uint8_t *image, size_t len)
{
    if (!vm || !image || len < 0x24) return ECS_ERR_IMAGE;
    vm->img = image;
    vm->img_len = len;

    reader r = { image, len, 0, 0 };
    if ((uint32_t)r_bytes(&r, 4) != 0x32435845u) return ECS_ERR_IMAGE;  /* "ECX2" */
    if (r_bytes(&r, 2) != 1) return ECS_ERR_IMAGE;                      /* format_ver */
    uint32_t flags = (uint32_t)r_bytes(&r, 2);
    (void)r_bytes(&r, 1);                                               /* max_slots */
    (void)r_bytes(&r, 1);                                               /* max_depth */
    /* 特征需求掩码（原保留位 u16 @0x0C，VM2.md §9.1）：IL 由 flags.I 投影（EcmEcxFormat §2.1）。
       加载规则：宿主 feats 缺位 → 拒跑（IL → ECS_ERR_IL 既有码，其余 → ECS_ERR_FEAT）。 */
    uint32_t feats = (uint32_t)r_bytes(&r, 2);
    if ((flags & 0x4) != 0)
        feats |= ECS_FEAT_IL;
    if ((feats & ECS_FEAT_IL) != 0)
        return ECS_ERR_IL;
    if ((feats & ~vm->host.feats) != 0)
        return ECS_ERR_FEAT;
    int32_t nconsts = r_count(&r);
    int32_t nstructs = r_count(&r);
    int32_t nglobals = r_count(&r);
    int32_t nnatives = r_count(&r);
    int32_t nfuncs = r_count(&r);
    int32_t debug_count = r_count(&r);
    if (r.overflow || nfuncs <= 0) return ECS_ERR_IMAGE;

    /* ---- 常量池 ---- */
    vm->consts = (const_ent *)calloc((size_t)(nconsts > 0 ? nconsts : 1), sizeof(const_ent));
    vm->nconsts = nconsts;
    if (!vm->consts) return ECS_ERR_OOM;
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
                if (r.pos + (size_t)c->units_len > r.len) return ECS_ERR_IMAGE;
                c->units = (const uint16_t *)(const void *)(image + r.pos);
                r.pos += (size_t)c->units_len;
                break;
            }
            default:
                return ECS_ERR_IMAGE;
        }
    }
    if (r.overflow) return ECS_ERR_IMAGE;

    /* ---- 类型表（计数消费后统一布局展开，§4.4） ---- */
    vm->structs = (ecs_structdef *)calloc((size_t)(nstructs > 0 ? nstructs : 1), sizeof(ecs_structdef));
    vm->nstructs = nstructs;
    if (!vm->structs) return ECS_ERR_OOM;
    for (int32_t s = 0; s < nstructs; s++)
    {
        ecs_structdef *def = &vm->structs[s];
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
    {
        uint8_t *visiting = (uint8_t *)calloc((size_t)(nstructs > 0 ? nstructs : 1), 1);
        if (!visiting) return ECS_ERR_OOM;
        for (int32_t s = 0; s < nstructs; s++)
            if (expand_struct_slots(vm->structs, s, visiting) < 0)
            {
                free(visiting);
                return ECS_ERR_IMAGE;   /* 嵌套环 */
            }
        free(visiting);
    }

    /* ---- 全局槽 ---- */
    vm->globals = (ecs_value *)calloc((size_t)(nglobals > 0 ? nglobals : 1), sizeof(ecs_value));
    vm->nglobals = nglobals;
    if (!vm->globals) return ECS_ERR_OOM;
    for (int32_t g = 0; g < nglobals; g++)
    {
        char *name = r_utf8(&r);
        free(name);
        (void)r_bytes(&r, 2);   /* 模块 idx + 类型码 */
        if (r.overflow) return ECS_ERR_IMAGE;
    }

    /* ---- 原生名表 ---- */
    vm->natives = (char **)calloc((size_t)(nnatives > 0 ? nnatives : 1), sizeof(char *));
    vm->nnatives = nnatives;
    if (!vm->natives) return ECS_ERR_OOM;
    for (int32_t n = 0; n < nnatives; n++)
    {
        vm->natives[n] = r_utf8(&r);
        if (!vm->natives[n]) return ECS_ERR_IMAGE;
    }

    /* ---- 函数表（11 字节定长）+ 代码区指针 ---- */
    vm->funcs = (ecs_funcdef *)calloc((size_t)nfuncs, sizeof(ecs_funcdef));
    vm->nfuncs = nfuncs;
    if (!vm->funcs) return ECS_ERR_OOM;
    uint32_t last_off = 0, last_words = 0;
    for (int32_t f = 0; f < nfuncs; f++)
    {
        ecs_funcdef *fd = &vm->funcs[f];
        fd->nparams = (uint8_t)r_bytes(&r, 1);
        fd->nslots = (uint8_t)r_bytes(&r, 1);
        fd->hasret = (uint8_t)r_bytes(&r, 1);
        fd->code_off = (uint32_t)r_bytes(&r, 4);
        fd->words = (uint32_t)r_bytes(&r, 4);
        if (r.overflow) return ECS_ERR_IMAGE;
        if (fd->code_off != last_off && fd->code_off != last_off + last_words)
            return ECS_ERR_IMAGE;                    /* 非连续（允许首函数偏移 0） */
        last_off = fd->code_off;
        last_words = fd->words;
    }
    uint64_t code_bytes = (uint64_t)last_off * 4 + (uint64_t)last_words * 4;
    if (r.pos + code_bytes > r.len) return ECS_ERR_IMAGE;
    {
        const uint8_t *code_base = image + r.pos;
        for (int32_t f = 0; f < nfuncs; f++)
            vm->funcs[f].code = (const uint32_t *)(code_base + (size_t)vm->funcs[f].code_off * 4);
        r.pos += (size_t)code_bytes;
    }

    /* ---- 调试名区（可选） ---- */
    if (debug_count > 0)
    {
        if (debug_count != nfuncs) return ECS_ERR_IMAGE;
        for (int32_t f = 0; f < nfuncs; f++)
        {
            vm->funcs[f].name = r_utf8(&r);
            if (r.overflow) return ECS_ERR_IMAGE;
        }
    }

    /* ---- entry 收尾（§2.10：恰剩 4 字节） ---- */
    int32_t entry = (int32_t)r_bytes(&r, 4);
    if (r.overflow || r.pos != r.len) return ECS_ERR_IMAGE;
    if (entry < 0 || entry >= nfuncs) return ECS_ERR_IMAGE;
    vm->entry = entry;
    return ECS_OK;
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
    return vm;
}

void ecs_vm_cancel(ecs_vm *vm) { if (vm) vm->cancel_flag = 1; }

void ecs_vm_set_budget(ecs_vm *vm, int32_t steps_per_yield)
{
    if (vm && steps_per_yield > 0) vm->budget = steps_per_yield;
}

void ecs_vm_error_location(const ecs_vm *vm, int32_t *out_func, int32_t *out_pc)
{
    if (out_func) *out_func = vm ? vm->error_func : -1;
    if (out_pc) *out_pc = vm ? vm->error_pc : -1;
}

/* 兜底 sweep 单对象：先摘除表项（阻断环/重复/悬挂子引用），再递归释放子引用后释放自身。
   不走 release/引用计数——残余对象可能互相引用且 rc 不归零（§4.2）。 */
static void sweep_obj(ecs_vm *vm, int32_t h)
{
    ecs_obj *o = (h >= 1 && h < vm->objs_count) ? vm->objs[h] : NULL;
    if (!o) return;
    vm->objs[h] = NULL;
    switch (o->kind)
    {
        case ECS_STRING: free(o->str.units); break;
        case ECS_ARRAY:
            for (int32_t i = 0; i < o->arr.len; i++)
                if (is_handle_tag(o->arr.items[i].tag))
                    sweep_obj(vm, (int32_t)o->arr.items[i].i64);
            free(o->arr.items);
            break;
        case ECS_STRUCT:
            for (int32_t i = 0; i < o->st.def->nslots; i++)
                if (is_handle_tag(o->st.slots[i].tag))
                    sweep_obj(vm, (int32_t)o->st.slots[i].i64);
            free(o->st.slots);
            break;
    }
    free(o);
}

void ecs_vm_free(ecs_vm *vm)
{
    if (!vm) return;
    /* 帧槽位先行：错误现场可能仍有活句柄，走引用计数正常回收 */
    for (int32_t d = 0; d < vm->depth; d++)
    {
        frame *fr = vm->frames[d];
        for (int32_t s = 0; s < fr->fn->nslots; s++)
            release(vm, fr->slots[s]);
        free(fr);
    }
    free(vm->frames);
    /* 全量 sweep：不依赖引用计数正确性兜底（§4.2） */
    for (int32_t h = 1; h < vm->objs_count; h++)
        sweep_obj(vm, h);
    free(vm->objs);
    free(vm->free_list);
    for (int32_t i = 0; i < vm->nstructs; i++)
    {
        for (int32_t f = 0; f < vm->structs[i].nfields; f++)
            free(vm->structs[i].fields[f].name);
        free(vm->structs[i].fields);
        free(vm->structs[i].name);
    }
    free(vm->structs);
    for (int32_t i = 0; i < vm->nnatives; i++)
        free(vm->natives[i]);
    free(vm->natives);
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
    switch (kind)
    {
        case CV_INT_TO_DOUBLE: r.tag = ECS_DOUBLE; r.f64 = (double)v.i32; break;
        case CV_DOUBLE_TO_INT: r.tag = ECS_INT; r.i32 = saturate_d2i(v.f64); break;
        case CV_INT_TO_UINT: r.tag = ECS_UINT; r.i64 = (uint32_t)v.i32; break;
        case CV_UINT_TO_INT: r.tag = ECS_INT; r.i32 = v.i32; break;
        case CV_INT_TO_BYTE: r.tag = ECS_BYTE; r.i32 = (int32_t)(uint8_t)v.i32; break;
        case CV_BOOL_TO_INT: r.tag = ECS_INT; r.i32 = v.i32; break;
        case CV_INT_TO_UINT64: r.tag = ECS_UINT64; r.i64 = (int64_t)v.i32; break;
        case CV_UINT_TO_UINT64: r.tag = ECS_UINT64; r.i64 = (int64_t)(uint32_t)v.i32; break;
        case CV_UINT64_TO_INT: r.tag = ECS_INT; r.i32 = (int32_t)v.i64; break;
        case CV_INT_TO_PTR: r.tag = ECS_PTR; r.i64 = (int64_t)v.i32; break;
        case CV_PTR_TO_INT: r.tag = ECS_INT; r.i32 = (int32_t)v.i64; break;
        case CV_UINT64_TO_PTR: r.tag = ECS_PTR; r.i64 = v.i64; break;
        case CV_PTR_TO_UINT64: r.tag = ECS_UINT64; r.i64 = v.i64; break;
        case CV_DOUBLE_TO_UINT64: r.tag = ECS_UINT64; r.i64 = saturate_d2l(v.f64); break;
        case CV_UINT64_TO_DOUBLE: r.tag = ECS_DOUBLE; r.f64 = (double)(uint64_t)v.i64; break;
        case CV_TOSTR: return tostring_value(vm, v);
        case CV_TOINT:
            if (v.tag == ECS_BOOL || v.tag == ECS_BYTE || v.tag == ECS_INT || v.tag == ECS_UINT)
                { r.tag = ECS_INT; r.i32 = v.i32; }
            else if (v.tag == ECS_DOUBLE)
                { r.tag = ECS_INT; r.i32 = saturate_d2i(v.f64); }
            else if (v.tag == ECS_STRING)
                { r.tag = ECS_INT; r.i32 = 0; }   /* 字符串解析为 PC 端实现；MCU 端静默返回 0 */
            else
                *err = 1;
            break;
        default: *err = 1; break;
    }
    return r;
}

static int32_t sign16(uint32_t v) { return (int32_t)(int16_t)(uint16_t)v; }
static int32_t sign24(uint32_t v) { return (int32_t)((v & 0xFFFFFFu) ^ 0x800000u) - 0x800000; }

static int push_frame(ecs_vm *vm, const ecs_funcdef *fn, int32_t func_index)
{
    if (vm->depth >= ECS_MAX_CALL_DEPTH)
        return ECS_ERR_DEPTH;
    if (vm->depth >= vm->frames_cap)
    {
        int32_t cap = vm->frames_cap ? vm->frames_cap * 2 : 16;
        frame **grown = (frame **)realloc(vm->frames, (size_t)cap * sizeof(frame *));
        if (!grown) return ECS_ERR_OOM;
        vm->frames = grown;
        vm->frames_cap = cap;
    }
    frame *fr = (frame *)malloc(sizeof(frame) + (size_t)fn->nslots * sizeof(ecs_value));
    if (!fr) return ECS_ERR_OOM;
    memset(fr, 0, sizeof(frame) + (size_t)fn->nslots * sizeof(ecs_value));
    fr->fn = fn;
    fr->func_index = func_index;
    fr->ret_slot = ECS_NO_SLOT;
    vm->frames[vm->depth++] = fr;
    return 0;
}

/*
 * 数据/容器/结构体/域操作指令（主循环 default 分派）。
 * 返回 0 = 成功；非 0 = 错误码（错误现场由主循环设置）。
 */
static int exec_data_op(ecs_vm *vm, uint32_t op, ecs_value *R,
                        int32_t a, int32_t b, int32_t c,
                        frame *fr)
{
    const uint32_t *code = fr->fn->code;
    uint32_t words = fr->fn->words;

#define REQUIRE_EXT() do { if (!ecs_op_has_ext(op) || fr->ret_pc >= words) return ECS_ERR_OPCODE; } while (0)
#define FAIL(code) return (code)

    switch (op)
    {
        /* ---- 算术（S-02/S-05；无符号回绕对齐 C# unchecked） ---- */
        case OP_AddI: store_fresh(vm, &R[a], make_int((int32_t)((uint32_t)R[b].i32 + (uint32_t)R[c].i32))); break;
        case OP_SubI: store_fresh(vm, &R[a], make_int((int32_t)((uint32_t)R[b].i32 - (uint32_t)R[c].i32))); break;
        case OP_MulI: store_fresh(vm, &R[a], make_int((int32_t)((uint32_t)R[b].i32 * (uint32_t)R[c].i32))); break;
        case OP_DivI:
        {
            if (R[c].i32 == 0) FAIL(ECS_ERR_DIVZERO);
            store_fresh(vm, &R[a], make_int(R[c].i32 == -1 ? (int32_t)(0u - (uint32_t)R[b].i32) : R[b].i32 / R[c].i32));
            break;
        }
        case OP_ModI:
        {
            if (R[c].i32 == 0) FAIL(ECS_ERR_DIVZERO);
            store_fresh(vm, &R[a], make_int(R[c].i32 == -1 ? 0 : R[b].i32 % R[c].i32));
            break;
        }
        case OP_RDivI:   /* S-05：`（a + b/2) / b */
        {
            if (R[c].i32 == 0) FAIL(ECS_ERR_DIVZERO);
            int32_t num = (int32_t)((uint32_t)R[b].i32 + (uint32_t)(R[c].i32 / 2));
            store_fresh(vm, &R[a], make_int(R[c].i32 == -1 ? (int32_t)(0u - (uint32_t)num) : num / R[c].i32));
            break;
        }
        case OP_AddU: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT; R[a].i64 = (uint32_t)R[b].i32 + (uint32_t)R[c].i32; break;
        case OP_SubU: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT; R[a].i64 = (uint32_t)R[b].i32 - (uint32_t)R[c].i32; break;
        case OP_MulU: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT; R[a].i64 = (uint32_t)R[b].i32 * (uint32_t)R[c].i32; break;
        case OP_DivU:
        {
            if ((uint32_t)R[c].i32 == 0) FAIL(ECS_ERR_DIVZERO);
            store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT; R[a].i64 = (uint32_t)R[b].i32 / (uint32_t)R[c].i32;
            break;
        }
        case OP_ModU:
        {
            if ((uint32_t)R[c].i32 == 0) FAIL(ECS_ERR_DIVZERO);
            store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT; R[a].i64 = (uint32_t)R[b].i32 % (uint32_t)R[c].i32;
            break;
        }
        case OP_AddL: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT64; R[a].i64 = (int64_t)((uint64_t)R[b].i64 + (uint64_t)R[c].i64); break;
        case OP_SubL: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT64; R[a].i64 = (int64_t)((uint64_t)R[b].i64 - (uint64_t)R[c].i64); break;
        case OP_MulL: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT64; R[a].i64 = (int64_t)((uint64_t)R[b].i64 * (uint64_t)R[c].i64); break;
        case OP_DivL:
        {
            if (R[c].i64 == 0) FAIL(ECS_ERR_DIVZERO);
            store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT64;
            R[a].i64 = R[c].i64 == -1 ? (int64_t)(0ULL - (uint64_t)R[b].i64) : R[b].i64 / R[c].i64;
            break;
        }
        case OP_ModL:
        {
            if (R[c].i64 == 0) FAIL(ECS_ERR_DIVZERO);
            store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT64;
            R[a].i64 = R[c].i64 == -1 ? 0 : R[b].i64 % R[c].i64;
            break;
        }
        case OP_AddD: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_DOUBLE; R[a].f64 = R[b].f64 + R[c].f64; break;
        case OP_SubD: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_DOUBLE; R[a].f64 = R[b].f64 - R[c].f64; break;
        case OP_MulD: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_DOUBLE; R[a].f64 = R[b].f64 * R[c].f64; break;
        case OP_DivD: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_DOUBLE; R[a].f64 = R[b].f64 / R[c].f64; break;

        /* ---- 位运算（S-03 掩码；左移用无符号回绕） ---- */
        case OP_BandI: store_fresh(vm, &R[a], make_int(R[b].i32 & R[c].i32)); break;
        case OP_BorI: store_fresh(vm, &R[a], make_int(R[b].i32 | R[c].i32)); break;
        case OP_BxorI: store_fresh(vm, &R[a], make_int(R[b].i32 ^ R[c].i32)); break;
        case OP_ShlI: store_fresh(vm, &R[a], make_int((int32_t)((uint32_t)R[b].i32 << (R[c].i32 & 31)))); break;
        case OP_ShrI: store_fresh(vm, &R[a], make_int(R[b].i32 >> (R[c].i32 & 31))); break;
        case OP_BnotI: store_fresh(vm, &R[a], make_int((int32_t)(~(uint32_t)R[b].i32))); break;

        /* ---- 比较 ---- */
        case OP_EqI: store_fresh(vm, &R[a], make_bool(R[b].i32 == R[c].i32)); break;
        case OP_LtI: store_fresh(vm, &R[a], make_bool(R[b].i32 < R[c].i32)); break;
        case OP_LeI: store_fresh(vm, &R[a], make_bool(R[b].i32 <= R[c].i32)); break;
        case OP_GtI: store_fresh(vm, &R[a], make_bool(R[b].i32 > R[c].i32)); break;
        case OP_GeI: store_fresh(vm, &R[a], make_bool(R[b].i32 >= R[c].i32)); break;
        case OP_EqU: store_fresh(vm, &R[a], make_bool((uint32_t)R[b].i32 == (uint32_t)R[c].i32)); break;
        case OP_LtU: store_fresh(vm, &R[a], make_bool((uint32_t)R[b].i32 < (uint32_t)R[c].i32)); break;
        case OP_LeU: store_fresh(vm, &R[a], make_bool((uint32_t)R[b].i32 <= (uint32_t)R[c].i32)); break;
        case OP_GtU: store_fresh(vm, &R[a], make_bool((uint32_t)R[b].i32 > (uint32_t)R[c].i32)); break;
        case OP_GeU: store_fresh(vm, &R[a], make_bool((uint32_t)R[b].i32 >= (uint32_t)R[c].i32)); break;
        case OP_EqD: store_fresh(vm, &R[a], make_bool(cmp_double(R[b].f64, R[c].f64) == 0)); break;
        case OP_LtD: store_fresh(vm, &R[a], make_bool(cmp_double(R[b].f64, R[c].f64) < 0)); break;
        case OP_LeD: store_fresh(vm, &R[a], make_bool(cmp_double(R[b].f64, R[c].f64) <= 0)); break;
        case OP_GtD: store_fresh(vm, &R[a], make_bool(cmp_double(R[b].f64, R[c].f64) > 0)); break;
        case OP_GeD: store_fresh(vm, &R[a], make_bool(cmp_double(R[b].f64, R[c].f64) >= 0)); break;
        case OP_EqL: store_fresh(vm, &R[a], make_bool(R[b].i64 == R[c].i64)); break;
        case OP_LtL: store_fresh(vm, &R[a], make_bool(R[b].i64 < R[c].i64)); break;
        case OP_LeL: store_fresh(vm, &R[a], make_bool(R[b].i64 <= R[c].i64)); break;
        case OP_GtL: store_fresh(vm, &R[a], make_bool(R[b].i64 > R[c].i64)); break;
        case OP_GeL: store_fresh(vm, &R[a], make_bool(R[b].i64 >= R[c].i64)); break;
        case OP_EqS:
        {
            const uint16_t *lx, *ly; int32_t llx, lly;
            str_or_null(vm, R[b], &lx, &llx);
            str_or_null(vm, R[c], &ly, &lly);
            store_fresh(vm, &R[a], make_bool(units_equal(lx, llx, ly, lly)));
            break;
        }
        case OP_EqP: store_fresh(vm, &R[a], make_bool(R[b].i64 == R[c].i64)); break;

        /* ---- 一元与转换 ---- */
        case OP_Not: store_fresh(vm, &R[a], make_bool(R[b].i32 == 0)); break;
        case OP_NegI: store_fresh(vm, &R[a], make_int((int32_t)(0u - (uint32_t)R[b].i32))); break;
        case OP_NegD: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_DOUBLE; R[a].f64 = -R[b].f64; break;
        case OP_Conv:
        {
            int err = 0;
            ecs_value r = do_conv(vm, (uint32_t)c, R[b], &err);
            if (err) FAIL(ECS_ERR_TYPE);
            store_fresh(vm, &R[a], r);
            break;
        }

        /* ---- 数组 / 字符串 ---- */
        case OP_NewArrV:
        {
            REQUIRE_EXT();
            uint32_t etag = code[fr->ret_pc++];
            int32_t h = hnew_arr(vm, (uint8_t)etag, b);
            if (h < 0) FAIL(ECS_ERR_OOM);
            ecs_obj *o = vm->objs[h];
            for (int32_t i = 0; i < b; i++)
            {
                o->arr.items[i] = R[c + i];
                retain(vm, o->arr.items[i]);
            }
            o->arr.len = b;
            store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_ARRAY; R[a].i64 = h;
            break;
        }
        case OP_NewArrE:
        {
            int32_t h = hnew_arr(vm, (uint8_t)(b | (c << 8)), 0);
            if (h < 0) FAIL(ECS_ERR_OOM);
            store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_ARRAY; R[a].i64 = h;
            break;
        }
        case OP_GetI:
        {
            ecs_value container = R[b];
            int32_t idx = R[c].i32;
            if (container.tag == ECS_STRING)
            {
                const uint16_t *u; int32_t len;
                str_or_null(vm, container, &u, &len);
                if (idx < 0 || idx >= len) FAIL(ECS_ERR_INDEX);
                store_fresh(vm, &R[a], new_str_units(vm, u ? u + idx : NULL, 1));
            }
            else
            {
                ecs_obj *arr = heap_obj(vm, (int32_t)container.i64);
                if (!arr || container.tag != ECS_ARRAY) FAIL(ECS_ERR_TYPE);
                if (idx < 0 || idx >= arr->arr.len) FAIL(ECS_ERR_INDEX);
                /* S-10 按 elem tag 归一读 */
                ecs_value item = arr->arr.items[idx];
                if (arr->arr.etag == ECS_BYTE) { item.tag = ECS_BYTE; item.i32 &= 0xFF; }
                else if (arr->arr.etag == ECS_BOOL) { item = make_bool(item.i32 != 0); }
                move_to(vm, &R[a], item);
            }
            break;
        }
        case OP_SetI:
        {
            ecs_obj *arr = heap_obj(vm, (int32_t)R[b].i64);
            if (!arr || R[b].tag != ECS_ARRAY) FAIL(ECS_ERR_TYPE);
            int32_t idx = R[c].i32;
            if (idx < 0 || idx >= arr->arr.len) FAIL(ECS_ERR_INDEX);
            ecs_value v = R[a];
            if (arr->arr.etag == ECS_BOOL) v = make_bool(v.i32 != 0);   /* CoerceWrite */
            release(vm, arr->arr.items[idx]);
            arr->arr.items[idx] = v;
            retain(vm, v);
            break;
        }
        case OP_Slice:
        {
            REQUIRE_EXT();
            uint32_t ext = code[fr->ret_pc++];
            ecs_value container = R[b];
            int32_t start = R[c].i32;
            if (container.tag == ECS_STRING)
            {
                const uint16_t *u; int32_t len;
                str_or_null(vm, container, &u, &len);
                int32_t end = ext == 0xFFFFFFFFu ? len : R[(int32_t)ext].i32;
                if (start < 0 || start > len || end > len || start > end) FAIL(ECS_ERR_INDEX);
                store_fresh(vm, &R[a], new_str_units(vm, u ? u + start : NULL, end - start));
            }
            else
            {
                ecs_obj *arr = heap_obj(vm, (int32_t)container.i64);
                if (!arr || container.tag != ECS_ARRAY) FAIL(ECS_ERR_TYPE);
                int32_t end = ext == 0xFFFFFFFFu ? arr->arr.len : R[(int32_t)ext].i32;
                if (start < 0 || start > arr->arr.len || end > arr->arr.len || start > end) FAIL(ECS_ERR_INDEX);
                int32_t h = hnew_arr(vm, arr->arr.etag, end - start);
                if (h < 0) FAIL(ECS_ERR_OOM);
                ecs_obj *dst = vm->objs[h];
                for (int32_t i = start; i < end; i++)
                {
                    dst->arr.items[dst->arr.len] = arr->arr.items[i];
                    retain(vm, dst->arr.items[dst->arr.len]);
                    dst->arr.len++;
                }
                store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_ARRAY; R[a].i64 = h;
            }
            break;
        }
        case OP_Cont:   /* S-09 */
        {
            ecs_value item = R[b];
            ecs_value container = R[c];
            if (container.tag == ECS_STRING)
            {
                const uint16_t *hay; int32_t hlen;
                str_or_null(vm, container, &hay, &hlen);
                if (item.tag != ECS_STRING) FAIL(ECS_ERR_TYPE);
                const uint16_t *sub; int32_t slen;
                str_or_null(vm, item, &sub, &slen);
                store_fresh(vm, &R[a], make_bool(units_contains(hay, hlen, sub, slen)));
            }
            else if (container.tag == ECS_ARRAY)
            {
                ecs_obj *arr = heap_obj(vm, (int32_t)container.i64);
                if (!arr) FAIL(ECS_ERR_TYPE);
                if (item.tag != arr->arr.etag)
                    store_fresh(vm, &R[a], make_bool(0));   /* 元素类型不匹配 → false */
                else
                {
                    int found = 0;
                    for (int32_t i = 0; i < arr->arr.len; i++)
                        if (value_equals(vm, arr->arr.items[i], item)) { found = 1; break; }
                    store_fresh(vm, &R[a], make_bool(found));
                }
            }
            else
                FAIL(ECS_ERR_TYPE);
            break;
        }
        case OP_Append:
        {
            ecs_obj *src = heap_obj(vm, (int32_t)R[b].i64);
            if (!src || R[b].tag != ECS_ARRAY) FAIL(ECS_ERR_TYPE);
            int32_t h = hnew_arr(vm, src->arr.etag, src->arr.len + 1);
            if (h < 0) FAIL(ECS_ERR_OOM);
            ecs_obj *dst = vm->objs[h];
            for (int32_t i = 0; i < src->arr.len; i++)
            {
                dst->arr.items[i] = src->arr.items[i];
                retain(vm, dst->arr.items[i]);
            }
            dst->arr.items[src->arr.len] = R[c];
            retain(vm, R[c]);
            dst->arr.len = src->arr.len + 1;
            store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_ARRAY; R[a].i64 = h;
            break;
        }
        case OP_Cat:   /* S-11 */
        {
            ecs_value l = R[b], r = R[c];
            if (l.tag == ECS_STRING || r.tag == ECS_STRING)
            {
                strbuf sb; memset(&sb, 0, sizeof(sb));
                tostring_top(vm, &sb, l);
                tostring_top(vm, &sb, r);
                store_fresh(vm, &R[a], new_str_units(vm, sb.units, sb.len));
                free(sb.units);
            }
            else if (l.tag == ECS_ARRAY && r.tag == ECS_ARRAY)
            {
                ecs_obj *la = heap_obj(vm, (int32_t)l.i64);
                ecs_obj *ra = heap_obj(vm, (int32_t)r.i64);
                if (!la || !ra) FAIL(ECS_ERR_TYPE);
                if (la->arr.etag != ra->arr.etag) FAIL(ECS_ERR_TYPE);
                int32_t h = hnew_arr(vm, la->arr.etag, la->arr.len + ra->arr.len);
                if (h < 0) FAIL(ECS_ERR_OOM);
                ecs_obj *dst = vm->objs[h];
                for (int32_t i = 0; i < la->arr.len; i++)
                {
                    dst->arr.items[i] = la->arr.items[i];
                    retain(vm, dst->arr.items[i]);
                }
                for (int32_t i = 0; i < ra->arr.len; i++)
                {
                    dst->arr.items[la->arr.len + i] = ra->arr.items[i];
                    retain(vm, dst->arr.items[la->arr.len + i]);
                }
                dst->arr.len = la->arr.len + ra->arr.len;
                store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_ARRAY; R[a].i64 = h;
            }
            else
                FAIL(ECS_ERR_TYPE);
            break;
        }
        case OP_Len:
        {
            if (R[b].tag == ECS_STRING)
            {
                const uint16_t *u; int32_t len;
                str_or_null(vm, R[b], &u, &len);
                store_fresh(vm, &R[a], make_int(len));
            }
            else if (R[b].tag == ECS_ARRAY)
            {
                ecs_obj *arr = heap_obj(vm, (int32_t)R[b].i64);
                if (!arr) FAIL(ECS_ERR_TYPE);
                store_fresh(vm, &R[a], make_int(arr->arr.len));
            }
            else FAIL(ECS_ERR_TYPE);
            break;
        }

        /* ---- 结构体 ---- */
        case OP_NewSt:
        {
            int32_t sid = b | (c << 8);
            if (sid >= vm->nstructs) FAIL(ECS_ERR_SLOT);
            int32_t h = hnew_st(vm, &vm->structs[sid]);
            if (h < 0) FAIL(ECS_ERR_OOM);
            store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_STRUCT; R[a].i64 = h;
            break;
        }
        case OP_GetF:
        {
            ecs_obj *st = heap_obj(vm, (int32_t)R[b].i64);
            if (!st || R[b].tag != ECS_STRUCT) FAIL(ECS_ERR_TYPE);
            if (c >= st->st.def->nfields) FAIL(ECS_ERR_SLOT);
            const ecs_fielddef *f = &st->st.def->fields[c];
            switch (f->kind)
            {
                case 0:   /* Scalar */
                    if (f->type == ECS_BYTE) { store_fresh(vm, &R[a], st->st.slots[f->slot_offset]); R[a].tag = ECS_BYTE; R[a].i32 &= 0xFF; }
                    else if (f->type == ECS_BOOL) store_fresh(vm, &R[a], make_bool(st->st.slots[f->slot_offset].i32 != 0));
                    else { move_to(vm, &R[a], st->st.slots[f->slot_offset]); }
                    break;
                case FIELD_KIND_FIXED_ARRAY:
                {
                    int32_t h = hnew_arr(vm, f->elem, f->ext);
                    if (h < 0) FAIL(ECS_ERR_OOM);
                    ecs_obj *dst = vm->objs[h];
                    for (int32_t i = 0; i < f->ext; i++)
                    {
                        dst->arr.items[i] = st->st.slots[f->slot_offset + i];
                        retain(vm, dst->arr.items[i]);
                    }
                    dst->arr.len = f->ext;
                    store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_ARRAY; R[a].i64 = h;
                    break;
                }
                case FIELD_KIND_NESTED:
                {
                    const ecs_structdef *nested = &vm->structs[f->ext];
                    int32_t h = hnew_st(vm, nested);
                    if (h < 0) FAIL(ECS_ERR_OOM);
                    ecs_obj *dst = vm->objs[h];
                    for (int32_t i = 0; i < nested->nslots; i++)
                    {
                        dst->st.slots[i] = st->st.slots[f->slot_offset + i];
                        retain(vm, dst->st.slots[i]);
                    }
                    /* 写穿透视图（对齐 v1 GetNested / 解释器 ViewParent）：记录父链，
                       后续槽写入经 sync_view 逐级回写父槽区 */
                    dst->st.view_parent = (int32_t)R[b].i64;
                    dst->st.view_offset = f->slot_offset;
                    retain(vm, R[b]);
                    store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_STRUCT; R[a].i64 = h;
                    break;
                }
                default: FAIL(ECS_ERR_TYPE);   /* Boxed 不受支持（S-16） */
            }
            break;
        }
        case OP_PutF:
        {
            ecs_obj *st = heap_obj(vm, (int32_t)R[b].i64);
            if (!st || R[b].tag != ECS_STRUCT) FAIL(ECS_ERR_TYPE);
            if (c >= st->st.def->nfields) FAIL(ECS_ERR_SLOT);
            const ecs_fielddef *f = &st->st.def->fields[c];
            if (f->kind == 0)
            {
                ecs_value v = R[a];
                if (f->type == ECS_BOOL) v = make_bool(v.i32 != 0);
                move_to(vm, &st->st.slots[f->slot_offset], v);
                if (st->st.view_parent) sync_view(vm, st);
            }
            else if (f->kind == FIELD_KIND_NESTED)
            {
                ecs_obj *src = heap_obj(vm, (int32_t)R[a].i64);
                if (!src || R[a].tag != ECS_STRUCT) FAIL(ECS_ERR_TYPE);
                /* 对齐校验：src 槽数 vs 字段嵌套类型槽数（与 GetF 取嵌套布局一致） */
                const ecs_structdef *nested_put = &vm->structs[f->ext];
                if (src->st.def->nslots != nested_put->nslots) FAIL(ECS_ERR_TYPE);
                for (int32_t i = 0; i < src->st.def->nslots; i++)
                {
                    release(vm, st->st.slots[f->slot_offset + i]);
                    st->st.slots[f->slot_offset + i] = src->st.slots[i];
                    retain(vm, st->st.slots[f->slot_offset + i]);
                }
                if (st->st.view_parent) sync_view(vm, st);
            }
            else FAIL(ECS_ERR_TYPE);
            break;
        }
        case OP_GetFI:
        {
            REQUIRE_EXT();
            uint32_t ext = code[fr->ret_pc++];
            ecs_obj *st = heap_obj(vm, (int32_t)R[b].i64);
            if (!st || R[b].tag != ECS_STRUCT) FAIL(ECS_ERR_TYPE);
            if (c >= st->st.def->nfields) FAIL(ECS_ERR_SLOT);
            const ecs_fielddef *f = &st->st.def->fields[c];
            if (f->kind != FIELD_KIND_FIXED_ARRAY) FAIL(ECS_ERR_TYPE);
            int32_t idx = R[(int32_t)ext].i32;
            if (idx < 0 || idx >= f->ext) FAIL(ECS_ERR_INDEX);
            ecs_value item = st->st.slots[f->slot_offset + idx];
            if (f->type == ECS_BYTE) { item.tag = ECS_BYTE; item.i32 &= 0xFF; }
            else if (f->type == ECS_BOOL) item = make_bool(item.i32 != 0);
            move_to(vm, &R[a], item);
            break;
        }
        case OP_PutFI:
        {
            REQUIRE_EXT();
            uint32_t ext = code[fr->ret_pc++];
            ecs_obj *st = heap_obj(vm, (int32_t)R[b].i64);
            if (!st || R[b].tag != ECS_STRUCT) FAIL(ECS_ERR_TYPE);
            if (c >= st->st.def->nfields) FAIL(ECS_ERR_SLOT);
            const ecs_fielddef *f = &st->st.def->fields[c];
            if (f->kind != FIELD_KIND_FIXED_ARRAY) FAIL(ECS_ERR_TYPE);
            int32_t idx = R[(int32_t)ext].i32;
            if (idx < 0 || idx >= f->ext) FAIL(ECS_ERR_INDEX);
            ecs_value v = R[a];
            if (f->type == ECS_BOOL) v = make_bool(v.i32 != 0);
            release(vm, st->st.slots[f->slot_offset + idx]);
            st->st.slots[f->slot_offset + idx] = v;
            retain(vm, v);
            if (st->st.view_parent) sync_view(vm, st);
            break;
        }

        /* ---- 域操作 ---- */
        case OP_WaitI: if (vm->host.wait_ms) vm->host.wait_ms(vm->host.ud, b | (c << 8)); break;
        case OP_WaitV: if (vm->host.wait_ms) vm->host.wait_ms(vm->host.ud, R[a].i32); break;
        case OP_KeyI: if (vm->host.key) vm->host.key(vm->host.ud, (uint8_t)a, b | (c << 8)); break;
        case OP_KeyV: if (vm->host.key) vm->host.key(vm->host.ud, (uint8_t)a, R[b].i32); break;
        case OP_KeySt: if (vm->host.key_state) vm->host.key_state(vm->host.ud, (uint8_t)a, b); break;
        case OP_StickSet: if (vm->host.stick_set) vm->host.stick_set(vm->host.ud, (uint8_t)a, b, c); break;
        case OP_StickP:
        {
            REQUIRE_EXT();
            uint32_t ext = code[fr->ret_pc++];
            if (vm->host.stick_click)
                vm->host.stick_click(vm->host.ud, (uint8_t)a, b, c, (int32_t)ext);
            break;
        }
        case OP_StickPv:
        {
            REQUIRE_EXT();
            uint32_t ext = code[fr->ret_pc++];
            if (vm->host.stick_click)
                vm->host.stick_click(vm->host.ud, (uint8_t)a, (int32_t)(ext & 0xFF), (int32_t)((ext >> 16) & 0xFF), R[c].i32);
            break;
        }
        case OP_Img:
            /* 单片机约束：图像标签镜像已在加载期拒绝（NeedIL → ECS_ERR_IL）；
               运行期兜底防御（镜像标志被篡改的场景） */
            FAIL(ECS_ERR_IL);
        case OP_Rand:   /* S-12 */
        {
            int32_t max = R[b].i32;
            if (max < 0) FAIL(ECS_ERR_INDEX);
            int32_t v = 0;
            if (max != 0 && vm->host.rand) v = vm->host.rand(vm->host.ud, max);
            store_fresh(vm, &R[a], make_int(v));
            break;
        }

        default:
            FAIL(ECS_ERR_OPCODE);
    }
    return 0;
#undef FAIL
#undef REQUIRE_EXT
}

/* ============================================================
 * §4.3 主循环
 * ============================================================ */

int ecs_vm_run(ecs_vm *vm)
{
    if (!vm || vm->nfuncs == 0) return ECS_ERR_IMAGE;
    if (vm->depth == 0)
    {
        int rc = push_frame(vm, &vm->funcs[vm->entry], vm->entry);
        if (rc != 0) return rc;
    }

    for (;;)
    {
        if (vm->cancel_flag) return ECS_CANCELLED;
        if (++vm->steps >= vm->budget)
        {
            vm->steps = 0;
            return ECS_YIELD;   /* 可再次 Run 继续 */
        }

        frame *fr = vm->frames[vm->depth - 1];
        const uint32_t *code = fr->fn->code;
        uint32_t words = fr->fn->words;
        if (fr->ret_pc >= words)
        {
            vm->error_func = fr->func_index;
            vm->error_pc = (int32_t)fr->ret_pc;
            return ECS_ERR_OPCODE;
        }
        uint32_t ins = code[fr->ret_pc++];
        uint32_t op = ins & 0xFF;
        int32_t a = (int32_t)((ins >> 8) & 0xFF);
        int32_t b = (int32_t)((ins >> 16) & 0xFF);
        int32_t c = (int32_t)((ins >> 24) & 0xFF);
        ecs_value *R = fr->slots;

        switch (op)
        {
            case OP_Nop: break;
            case OP_Halt:
                vm->error_func = fr->func_index;
                vm->error_pc = (int32_t)fr->ret_pc - 1;
                return ECS_OK;

            case OP_LoadI: store_fresh(vm, &R[a], make_int(sign16(ins >> 16))); break;
            case OP_LoadK:
            {
                int32_t bx = b | (c << 8);
                if (bx >= vm->nconsts) { vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1; return ECS_ERR_SLOT; }
                const_ent *k = &vm->consts[bx];
                switch (k->tag)
                {
                    case ECS_STRING: store_fresh(vm, &R[a], new_str_units(vm, k->units, k->units_len / 2)); break;
                    case ECS_DOUBLE: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_DOUBLE; R[a].f64 = k->f64; break;
                    case ECS_UINT64: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT64; R[a].i64 = k->i64; break;
                    case ECS_PTR: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_PTR; R[a].i64 = k->i64; break;
                    case ECS_UINT: store_fresh(vm, &R[a], void_value()); R[a].tag = ECS_UINT; R[a].i64 = (uint32_t)k->i64; break;
                    default: store_fresh(vm, &R[a], make_int((int32_t)k->i64)); break;
                }
                break;
            }
            case OP_LoadBool: store_fresh(vm, &R[a], make_bool(b != 0)); break;
            case OP_Move: move_to(vm, &R[a], R[b]); break;
            case OP_SetVar: unify(vm, &R[a], R[b], 1); break;
            case OP_LoadG:
            {
                int32_t gx = b | (c << 8);
                if (gx >= vm->nglobals) { vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1; return ECS_ERR_SLOT; }
                move_to(vm, &R[a], vm->globals[gx]);
                break;
            }
            case OP_StoreG:
            {
                int32_t gx = b | (c << 8);
                if (gx >= vm->nglobals) { vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1; return ECS_ERR_SLOT; }
                unify(vm, &vm->globals[gx], R[a], 1);
                break;
            }

            case OP_Jmp: fr->ret_pc = (uint32_t)((int32_t)fr->ret_pc + sign24(ins >> 8)); break;
            case OP_Jpt: if (R[a].i32 != 0) fr->ret_pc = (uint32_t)((int32_t)fr->ret_pc + sign16(ins >> 16)); break;
            case OP_Jpf: if (R[a].i32 == 0) fr->ret_pc = (uint32_t)((int32_t)fr->ret_pc + sign16(ins >> 16)); break;

            case OP_Call:
            {
                if (!ecs_op_has_ext(op) || (uint32_t)fr->ret_pc >= words)
                {
                    vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1;
                    return ECS_ERR_OPCODE;
                }
                uint32_t target = code[fr->ret_pc++];
                if (target & ECS_IMPORT_FLAG || target >= (uint32_t)vm->nfuncs)
                {
                    vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1;
                    return ECS_ERR_OPCODE;   /* 链接后不应残留导入标记 */
                }
                int rc = push_frame(vm, &vm->funcs[target], (int32_t)target);
                if (rc != 0)
                {
                    vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1;
                    return rc;
                }
                frame *nf = vm->frames[vm->depth - 1];
                nf->ret_pc = 0;
                nf->ret_slot = (c == ECS_RECEIVE_NONE) ? ECS_NO_SLOT : c;
                for (int32_t i = 0; i < b; i++)
                {
                    if (a + i >= fr->fn->nslots)
                    {
                        vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1;
                        return ECS_ERR_SLOT;
                    }
                    unify(vm, &nf->slots[i], R[a + i], 1);   /* S-17 实参深拷贝 */
                }
                break;
            }
            case OP_CallN:
            {
                if (!ecs_op_has_ext(op) || (uint32_t)fr->ret_pc >= words)
                {
                    vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1;
                    return ECS_ERR_OPCODE;
                }
                uint32_t target = code[fr->ret_pc++];
                if (b > 8)
                {
                    vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1;
                    return ECS_ERR_NOSUCHNATIVE;
                }
                /* 旗标置位 = 文件族 syscall 编号（VM2.md §9.1）；否则原生名表索引 */
                if ((target & ECS_SYSCALL_FLAG) == 0 && target >= (uint32_t)vm->nnatives)
                {
                    vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1;
                    return ECS_ERR_NOSUCHNATIVE;
                }
                ecs_value args[8];
                for (int32_t i = 0; i < b; i++)
                {
                    if (a + i >= fr->fn->nslots)
                    {
                        vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1;
                        return ECS_ERR_SLOT;
                    }
                    args[i] = R[a + i];
                }
                int nerr = 0;
                ecs_value ret = void_value();
                if ((target & ECS_SYSCALL_FLAG) != 0)
                {
                    /* L2：编号 syscall，语义在宿主参考实现（VM 核纯调度） */
                    if (!vm->host.syscall || vm->host.syscall(vm->host.ud, (int32_t)(target & ~ECS_SYSCALL_FLAG), args, b, &ret) != 0)
                        nerr = 1;
                }
                else
                {
                    /* L3：名表动态原生（采集洞 / EXTERN FFI / ENCODE / JQ） */
                    if (!vm->host.native || vm->host.native(vm->host.ud, vm->natives[target], args, b, &ret) != 0)
                        nerr = 1;
                }
                if (nerr)
                {
                    vm->error_func = fr->func_index; vm->error_pc = (int32_t)fr->ret_pc - 1;
                    return ECS_ERR_NOSUCHNATIVE;
                }
                if (c != ECS_RECEIVE_NONE)
                    move_to(vm, &R[c], ret);
                break;
            }
            case OP_Ret:
            {
                /* 拷贝到调用者接收槽（S-17；COW：唯一引用移交）；值须跨弹帧持有 */
                ecs_value copied = deep_copy_cow(vm, R[a]);
                int32_t ret_slot = fr->ret_slot;
                const ecs_funcdef *done = fr->fn;
                (void)done;
                vm->depth--;
                for (int32_t s = 0; s < fr->fn->nslots; s++)
                    release(vm, fr->slots[s]);
                free(fr);
                if (vm->depth == 0) return ECS_OK;
                if (ret_slot != ECS_NO_SLOT)
                    move_to(vm, &vm->frames[vm->depth - 1]->slots[ret_slot], copied);
                else
                    release(vm, copied);
                break;
            }
            case OP_Ret0:
            {
                vm->depth--;
                for (int32_t s = 0; s < fr->fn->nslots; s++)
                    release(vm, fr->slots[s]);
                free(fr);
                if (vm->depth == 0) return ECS_OK;
                break;
            }

            default:
            {
                int rc = exec_data_op(vm, op, R, a, b, c, fr);
                if (rc != 0)
                {
                    vm->error_func = fr->func_index;
                    vm->error_pc = (int32_t)fr->ret_pc - 1;
                    return rc;
                }
                break;
            }
        }
    }
}
