/*
 * ecs_main.c — ecs-vm CLI harness（docs/VmSemanticContract.md §四）。
 *
 *   ecs-vm run image.ecx [--trace] [-- arg0 arg1...]
 *     stdout：脚本 PRINT 输出（UTF-16 → UTF-8）
 *     stderr：--trace 时逐条域事件 TSV（格式与 C# EcxHost.EnableRecording 一致，供三方对拍）
 *     退出码：ECS_* 结果码；错误时 stderr 打印 func/pc 现场
 *
 * 宿主 stub：wait=no-op（trace 打印）、rand/time=0、img_label=-1、read_line=空。
 * 输出编码：stdout 经 UTF-16 → UTF-8 转换。
 */
#include "ecs_vm.h"

#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int g_trace = 0;

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
static void h_amiibo(void *ud, int32_t slot) { (void)ud; trace_event("AMIIBO %d", slot); }
static void h_beep(void *ud, int32_t freq, int32_t dur) { (void)ud; trace_event("BEEP %d %d", freq, dur); }
static void h_print(void *ud, const uint16_t *units, int32_t len, int newline) { (void)ud; print_utf16(units, len, newline); }

static int sigint_seen = 0;

static int vm_cancel_requested(void) { return sigint_seen; }

int main(int argc, char **argv)
{
    if (argc < 3 || strcmp(argv[1], "run") != 0)
    {
        fprintf(stderr, "用法: ecs-vm run image.ecx [--trace] [-- arg0...]\n");
        return 2;
    }
    const char *imagePath = argv[2];
    const char *scriptArgs[64];
    int nargs = 0;
    g_trace = 0;
    int pcPrint = 0;   /* PC 验证模式：启用 PRINT 通道（单片机上 print 恒忽略） */
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

    /* 单片机功能范围（约束）：rand/wait/按键；print 忽略；img_label 拒跑（ECS_ERR_IL）；time 恒 0。
       --print 仅为 PC 侧验证通道（对拍测试用），单片机构建不接 print 回调。 */
    ecs_host host;
    memset(&host, 0, sizeof(host));
    host.caps = ECS_CAP_ALL_MCU;
    if (pcPrint)
        host.caps |= ECS_CAP_PRINT;
    host.args = scriptArgs;
    host.nargs = nargs;
    host.wait_ms = h_wait;
    host.key = h_key;
    host.key_state = h_key_state;
    host.stick_set = h_stick_set;
    host.stick_click = h_stick_click;
    host.amiibo = h_amiibo;
    host.beep = h_beep;
    host.print = h_print;
    /* rand/time/img_label/read_line：缺省 stub（=0 / -1 / 空），与 EcxHost 默认一致 */

    ecs_vm *vm = ecs_vm_new(&host);
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
