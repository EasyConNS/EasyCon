/* P0 public-ABI probe. Does not include or access ecs_vm.c internals. */
#include "ecs_vm.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <inttypes.h>

typedef struct {
    ecs_vm *vm;
    const char *mode;
    int calls, waits, keys, now, due;
} probe_state;

static int api_checks(void)
{
    ecs_vm_meminfo mi;
    ecs_vm_query_meminfo(&mi);
    if (mi.value_bytes != 8) return 90;
    printf("value=%u pointer=%u instance=%" PRIu64 " blocks=%u\n", mi.value_bytes,
        (unsigned)sizeof(void *), (uint64_t)mi.instance_bytes, mi.obj_block_count);
    for (int i = 0; i < 1000; i++)
    {
        ecs_vm *vm = ecs_vm_new(NULL);
        if (!vm) return 91;
        ecs_value v = {0};
        if (ecs_vm_ret_wide(vm, &v, ECS_PTR, -1) != ECS_OK ||
            ecs_vm_arg_wide(vm, v) != -1) return 92;
        if (ecs_vm_ret_wide(vm, &v, ECS_UINT64, INT64_MIN) != ECS_OK ||
            ecs_vm_arg_wide(vm, v) != INT64_MIN) return 93;
        if (ecs_vm_ret_double(vm, &v, -0.0) != ECS_OK ||
            !signbit(ecs_vm_arg_double(vm, v))) return 94;
        if (mi.obj_block_count == 3 &&
            (ecs_vm_ret_double(vm, &v, 1.0) != ECS_ERR_POOL || v.tag != ECS_VOID)) return 95;
        if (ecs_vm_ret_wide(vm, &v, ECS_DOUBLE, 0) != ECS_ERR_TYPE || v.tag != ECS_VOID) return 96;
        ecs_vm_free(vm);
    }
    double values[] = { NAN, INFINITY, -INFINITY };
    for (int i = 0; i < 3; i++)
    {
        ecs_vm *vm = ecs_vm_new(NULL);
        ecs_value v;
        if (!vm || ecs_vm_ret_double(vm, &v, values[i]) != ECS_OK) return 97;
        double actual = ecs_vm_arg_double(vm, v);
        if (i == 0 ? !isnan(actual) : (actual != values[i])) return 98;
        ecs_vm_free(vm);
    }
    return ECS_OK;
}

static int native_call(void *ud, const char *name, ecs_value *args, int32_t nargs, ecs_value *ret)
{
    probe_state *s = (probe_state *)ud;
    (void)name;
    s->calls++;
    *ret = (ecs_value){0};
    if (strcmp(s->mode, "host-wide") == 0 || strcmp(s->mode, "ignore-pool") == 0)
        ecs_vm_ret_wide(s->vm, ret, ECS_PTR, -1); /* Deliberately ignore status: VM must propagate it. */
    else if (strcmp(s->mode, "host-double") == 0)
        ecs_vm_ret_double(s->vm, ret, 3.5);
    else if (strcmp(s->mode, "host-string") == 0)
    {
        if (nargs > 0)
        {
            const uint16_t *units = NULL;
            int32_t len = ecs_vm_arg_str(s->vm, args[0], &units);
            if (len != 2 || !units || units[0] != 'o' || units[1] != 'k') return 1;
        }
        const uint16_t text[] = { 'o', 'k' };
        ecs_vm_ret_str(s->vm, ret, text, 2);
    }
    else if (strcmp(s->mode, "bad-return") == 0)
        ecs_vm_ret_wide(s->vm, ret, ECS_DOUBLE, 1);
    else if (nargs > 0)
    {
        if (args[0].tag == ECS_STRING)
        {
            const uint16_t *units = NULL;
            int32_t len = ecs_vm_arg_str(s->vm, args[0], &units);
            printf("str:");
            for (int32_t i = 0; i < len; i++) putchar(units[i] < 128 ? (int)units[i] : '?');
            putchar('\n');
            return 0;
        }
        uint64_t bits;
        if (args[0].tag == ECS_DOUBLE)
        {
            double value = ecs_vm_arg_double(s->vm, args[0]);
            memcpy(&bits, &value, sizeof(bits));
        }
        else if (args[0].tag == ECS_PTR || args[0].tag == ECS_UINT64)
            bits = (uint64_t)ecs_vm_arg_wide(s->vm, args[0]);
        else bits = (uint32_t)args[0].u32;
        printf("%u:%016" PRIx64 "\n", args[0].tag, bits);
    }
    return 0;
}

static void wait_ms(void *ud, int32_t ms)
{
    probe_state *s = (probe_state *)ud;
    s->waits++;
    s->due = s->now + (ms > 0 ? ms : 0);
    printf("WAIT %d\n", (int)ms);
    if (strcmp(s->mode, "wait-callback-cancel") == 0) ecs_vm_cancel(s->vm);
}

static void key(void *ud, uint8_t code, int32_t ms)
{
    probe_state *s = (probe_state *)ud;
    s->keys++;
    printf("KEY %u %d\n", code, (int)ms);
}

int main(int argc, char **argv)
{
    if (argc < 2) return 89;
    if (strcmp(argv[1], "api") == 0) return api_checks();
    if (argc < 3) return 88;
    FILE *file = fopen(argv[2], "rb");
    if (!file) return 87;
    fseek(file, 0, SEEK_END);
    long size = ftell(file);
    rewind(file);
    if (size <= 0) { fclose(file); return 86; }
    uint8_t *image = (uint8_t *)malloc((size_t)size);
    if (!image) { fclose(file); return 85; }
    if (fread(image, 1, (size_t)size, file) != (size_t)size)
    { fclose(file); free(image); return 84; }
    fclose(file);
    probe_state s = {0};
    s.mode = argv[1];
    ecs_host host = {0};
    host.ud = &s; host.feats = ECS_FEAT_FFI;
    host.wait_ms = wait_ms; host.key = key;
    if (strcmp(s.mode, "missing") != 0) host.native = native_call;
    s.vm = ecs_vm_new(&host);
    if (!s.vm) { free(image); return 83; }
    int yielding = strncmp(s.mode, "wait", 4) == 0;
    ecs_vm_set_wait_yield(s.vm, yielding);
    if (strcmp(s.mode, "wait-pre-cancel") == 0) ecs_vm_cancel(s.vm);
    int rc = ecs_vm_load(s.vm, image, (size_t)size);
    int turns = 0;
    if (rc == ECS_OK)
    {
        do {
            rc = ecs_vm_run(s.vm);
            if (rc == ECS_WAITING)
            {
                printf("SUSPEND waits=%d keys=%d\n", s.waits, s.keys);
                if (strcmp(s.mode, "wait-cancel") == 0) ecs_vm_cancel(s.vm);
                else s.now = s.due; /* Firmware scheduler resumes only after its deadline. */
            }
        } while ((rc == ECS_YIELD || rc == ECS_WAITING) && ++turns < 10000);
    }
    int func, pc;
    if (rc == ECS_CANCELLED)
    {
        int calls = s.calls, waits = s.waits, keys = s.keys;
        if (ecs_vm_run(s.vm) != ECS_CANCELLED || s.calls != calls || s.waits != waits || s.keys != keys)
            rc = 82;
    }
    ecs_vm_error_location(s.vm, &func, &pc);
    fprintf(stderr, "rc=%d func=%d pc=%d calls=%d waits=%d keys=%d\n",
        rc, func, pc, s.calls, s.waits, s.keys);
    ecs_vm_free(s.vm);
    free(image);
    return rc;
}
