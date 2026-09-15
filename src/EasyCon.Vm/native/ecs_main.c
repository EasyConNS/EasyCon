/*
 * ecs_main.c — ecs-vm CLI harness（docs/VmSemanticContract.md §四）。
 * 单片机参考宿主：L2 平台 syscall 的语义参考实现 + L1 域操作回调 + L3 无（按需接入）。
 *
 *   ecs-vm run image.ecx [--trace] [--print] [-- arg0 arg1...]
 *     stdout：脚本 PRINT 输出（UTF-16 → UTF-8；--print 通道开）
 *     stderr：--trace 时逐条域事件 TSV（格式与 C# EcxHost.EnableRecording 一致，供三方对拍）
 *     退出码：ECS_* 结果码；错误时 stderr 打印 func/pc 现场
 *
 * 参考桩行为（MCU 功能范围约束，与桌面参考宿主 C# EcxHost 的差异即能力差异）：
 *   FWRITE：S-14 行断协议；stdout 通道 = --print 开（单片机构建缺省关，静默仍返 len）；
 *           文件句柄（>2）→ -1。FREAD：stdin/文件均无 → 空串。
 *   ALERT/BEEP/AMIIBO：no-op（AMIIBO n>9 静默，S-13）。TIME：恒 0。
 *   ARG：harness args。ENV：getenv。APP：无 app_dir → 空串。
 *   OCR_CONF / 文件族打开类：未实现 → ECS_ERR_NOSUCHNATIVE。
 * 特征位：feats = ECS_FEAT_FILE（桩覆盖文件族 syscall 的"静默/默认值"行为）。
 */
#include "ecs_vm.h"

#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int g_trace = 0;
static int g_print_channel = 0;     /* --print：stdout 输出通道（PC 侧验证用） */
static int g_pending_break = 0;     /* S-14 行断协议状态（宿主所有） */
static ecs_vm *g_vm = NULL;         /* syscall 处理器借用的堆访问上下文 */
static const char *const *g_args = NULL;
static int32_t g_nargs = 0;

/* ---- UTF-16 → UTF-8 ---- */

static void print_utf16(const uint16_t *units, int32_t len, int newline)
{
    char buf[4096];
    int32_t out = 0;
    for (int32_t i = 0; i < len; i++)
    {
        uint32_t c = units[i];
        if (out > (int32_t)sizeof(buf) - 8) { fwrite(buf, 1, (size_t)out, stdout); out = 0; }
        if (c < 0x80)
            buf[out++] = (char)c;
        else if (c < 0x800)
        {
            buf[out++] = (char)(0xC0 | (c >> 6));
            buf[out++] = (char)(0x80 | (c & 0x3F));
        }
        else /* BMP */
        {
            buf[out++] = (char)(0xE0 | (c >> 12));
            buf[out++] = (char)(0x80 | ((c >> 6) & 0x3F));
            buf[out++] = (char)(0x80 | (c & 0x3F));
        }
    }
    if (out > 0) fwrite(buf, 1, (size_t)out, stdout);
    if (newline) fputc('\n', stdout);
}

/* ---- 事件 TSV（与 C# EcxHost.EnableRecording 格式一致） ---- */

static void trace_event(const char *fmt, ...)
{
    if (!g_trace) return;
    va_list ap;
    va_start(ap, fmt);
    vfprintf(stderr, fmt, ap);
    va_end(ap);
    fputc('\n', stderr);
}

static void h_wait(void *ud, int32_t ms) { (void)ud; trace_event("WAIT %d", ms); }
static void h_key(void *ud, uint8_t key, int32_t dur) { (void)ud; trace_event("KEY %d %d", key, dur); }
static void h_key_state(void *ud, uint8_t key, int down) { (void)ud; trace_event("KEYST %d %d", key, down); }
static void h_stick_set(void *ud, uint8_t side, int32_t x, int32_t y) { (void)ud; trace_event("STICK %d %d %d", side, x, y); }
static void h_stick_click(void *ud, uint8_t side, int32_t x, int32_t y, int32_t dur)
{
    (void)ud;
    trace_event("STICKC %d %d %d %d", side, x, y, dur);
}

/* ---- L2 平台 syscall 参考实现（编号语义的唯一 MCU 权威；与 C# EcxHost.Syscall 对齐） ---- */

static void ret_str_ascii(ecs_value *ret, const char *s)
{
    uint16_t buf[512];
    int32_t n = 0;
    for (; s && s[n] != 0 && n < 511; n++)
        buf[n] = (uint16_t)(unsigned char)s[n];
    ecs_vm_ret_str(g_vm, ret, buf, n);
}

static int h_syscall(void *ud, int32_t id, ecs_value *args, int32_t nargs, ecs_value *ret)
{
    (void)ud;
    (void)nargs;
    *ret = (ecs_value){0};

    switch (id)
    {
        case ECS_SYSCALL_FWRITE:
        {
            /* S-14：句柄 1=stdout 行断协议；0=no-op 返 len；2=print(newline)；>2=文件族 → -1 */
            int64_t handle = args[0].i64;
            const uint16_t *u = NULL;
            int32_t len = ecs_vm_arg_str(g_vm, args[1], &u);
            if (handle == 1)
            {
                int ends_break = len > 0 && u && u[len - 1] == (uint16_t)'\\';
                int32_t out_len = ends_break ? len - 1 : len;
                if (g_print_channel)
                    print_utf16(u, out_len, !g_pending_break);
                g_pending_break = ends_break;
                ret->tag = ECS_INT;
                ret->i32 = len;
                return 0;
            }
            if (handle == 2)
            {
                if (g_print_channel)
                    print_utf16(u, len, 1);
                ret->tag = ECS_INT;
                ret->i32 = len;
                return 0;
            }
            ret->tag = ECS_INT;
            ret->i32 = (handle == 0) ? len : -1;
            return 0;
        }
        case ECS_SYSCALL_FREAD:
        {
            /* stdin/文件均未实现 → 空串 */
            ecs_vm_ret_str(g_vm, ret, NULL, 0);
            return 0;
        }
        case ECS_SYSCALL_ALERT:
            return 0;   /* no-op（静默忽略） */
        case ECS_SYSCALL_ARG:
        {
            int32_t i = args[0].i32;
            ret_str_ascii(ret, (i >= 0 && i < g_nargs && g_args) ? g_args[i] : "");
            return 0;
        }
        case ECS_SYSCALL_ENV:
        {
            const uint16_t *u = NULL;
            int32_t len = ecs_vm_arg_str(g_vm, args[0], &u);
            char key[512];
            int32_t n = 0;
            if (u)
                for (; n < len && n < 511; n++)
                    key[n] = u[n] < 128 ? (char)u[n] : '?';
            key[n] = 0;
            ret_str_ascii(ret, getenv(key) ? getenv(key) : "");
            return 0;
        }
        case ECS_SYSCALL_APP:
            ret_str_ascii(ret, "");   /* 无 app_dir → 空串 */
            return 0;
        case ECS_SYSCALL_TIME:
            ret->tag = ECS_INT;
            ret->i32 = 0;   /* 单片机：恒 0 */
            return 0;
        case ECS_SYSCALL_BEEP:
            return 0;   /* no-op（静默忽略） */
        case ECS_SYSCALL_AMIIBO:
            if (args[0].i32 <= 9)   /* S-13：n>9 静默忽略 */
                trace_event("AMIIBO %d", (int)args[0].i32);
            return 0;
        case ECS_SYSCALL_OCR_CONF:
        case ECS_SYSCALL_FOPEN:
        case ECS_SYSCALL_FCLOSE:
        case ECS_SYSCALL_FEOF:
        case ECS_SYSCALL_READFILE:
        case ECS_SYSCALL_WRITEFILE:
        case ECS_SYSCALL_APPENDFILE:
        case ECS_SYSCALL_FILE_EXISTS:
            return 1;   /* 未实现 → ECS_ERR_NOSUCHNATIVE */
        default:
            return 1;
    }
}

static int sigint_seen = 0;

static int vm_cancel_requested(void) { return sigint_seen; }

int main(int argc, char **argv)
{
    if (argc < 3 || strcmp(argv[1], "run") != 0)
    {
        fprintf(stderr, "用法: ecs-vm run image.ecx [--trace] [--print] [-- arg0...]\n");
        return 2;
    }
    const char *imagePath = argv[2];
    const char *scriptArgs[64];
    int nargs = 0;
    g_trace = 0;
    int pcPrint = 0;   /* PC 验证模式：启用 PRINT 输出通道（单片机构建缺省关，静默返 len） */
    for (int i = 3; i < argc; i++)
    {
        if (strcmp(argv[i], "--trace") == 0)
            g_trace = 1;
        else if (strcmp(argv[i], "--print") == 0)
            pcPrint = 1;
        else if (strcmp(argv[i], "--") == 0)
        {
            for (int j = i + 1; j < argc && nargs < 64; j++)
                scriptArgs[nargs++] = argv[j];
            break;
        }
    }

    FILE *f = fopen(imagePath, "rb");
    if (!f)
    {
        fprintf(stderr, "无法打开镜像: %s\n", imagePath);
        return 2;
    }
    fseek(f, 0, SEEK_END);
    long len = ftell(f);
    fseek(f, 0, SEEK_SET);
    uint8_t *image = (uint8_t *)malloc(len > 0 ? (size_t)len : 1);
    if (!image || fread(image, 1, (size_t)len, f) != (size_t)len)
    {
        fprintf(stderr, "镜像读取失败\n");
        fclose(f);
        free(image);
        return 2;
    }
    fclose(f);

    /* 参考宿主装配：L1 域操作回调（wait/key/stick + trace）+ L2 syscall 桩 + feats。 */
    ecs_host host;
    memset(&host, 0, sizeof(host));
    host.feats = ECS_FEAT_FILE;     /* 桩覆盖文件族 syscall 的"静默/默认值"行为 */
    host.args = scriptArgs;
    host.nargs = nargs;
    host.wait_ms = h_wait;
    host.key = h_key;
    host.key_state = h_key_state;
    host.stick_set = h_stick_set;
    host.stick_click = h_stick_click;
    host.syscall = h_syscall;
    /* native（L3 采集洞/FFI/ENCODE/JQ）：缺省 NULL → 未实现 */

    g_print_channel = pcPrint;
    g_args = scriptArgs;
    g_nargs = nargs;

    ecs_vm *vm = ecs_vm_new(&host);
    g_vm = vm;
    int rc = ecs_vm_load(vm, image, (size_t)len);
    if (rc != ECS_OK)
    {
        fprintf(stderr, "镜像校验失败: ECS_ERR=%d\n", rc);
        ecs_vm_free(vm);
        free(image);
        return rc;
    }

    /* 步数预算耗尽（ECS_YIELD）时续跑，长脚本完整执行；CANCELLED/错误即出 */
    do
    {
        rc = ecs_vm_run(vm);
    } while (rc == ECS_YIELD && !vm_cancel_requested());
    if (rc != ECS_OK && rc != ECS_YIELD && rc != ECS_CANCELLED)
    {
        int32_t ef, pc;
        ecs_vm_error_location(vm, &ef, &pc);
        fprintf(stderr, "执行错误: ECS_ERR=%d func=%d pc=%d\n", rc, ef, pc);
    }

    ecs_vm_free(vm);
    free(image);
    return rc;
}
