#!/bin/sh
# ecs-vm CI 构建脚本（docs/VmDevPlan.md §5 V7；容量门禁 docs/VmBudget.md B5）。
# 用法：build-vm.sh [budget]
#   缺省      ：全量档 ecs-vm（零警告 -Wall -Wextra）。
#   budget    ：额外构建 ecs-vm-budget（C99 switch 派发，arena=768 / frame_seg=384，
#               MCU 1KB 预算档，见 docs/VmBudget.md §预算表）并执行容量门禁：
#               RAM：ecs_vm 实例（instance = sizeof(ecs_vm) − sizeof(ecs_host)）≤ 1024B；
#               ROM：文本段 + 只读数据 ≤ 64KB。
# budget / ECSVM_REQUIRE_CC=1 不允许缺编译器时跳过；ECSVM_CC 可指定编译器或包装脚本。
set -e

case "${1:-}" in ""|budget) ;; *) echo "unknown profile: $1" >&2; exit 1 ;; esac
CC="${ECSVM_CC:-${CC:-}}"
if [ -z "$CC" ]; then
    for c in cc gcc clang; do
        if command -v "$c" >/dev/null 2>&1; then CC="$c"; break; fi
    done
fi

if [ -z "$CC" ]; then
    if [ "${1:-}" = "budget" ] || [ "${ECSVM_REQUIRE_CC:-0}" = "1" ]; then
        echo "FAIL: 未找到 C99 编译器，不能跳过原生容量/语义验证" >&2
        exit 1
    fi
    echo "skip: 未找到 C 编译器（cc/gcc/clang），ecs-vm 构建跳过"
    exit 0
fi

cd "$(dirname "$0")/../src/EasyCon.Vm/native"
"$CC" -O2 -std=c99 -Wall -Wextra -Werror -o ecs-vm ecs_vm.c ecs_main.c -lm
echo "built: $(pwd)/ecs-vm (cc=$CC)"

if [ "${1:-}" = "budget" ]; then
    "$CC" -O2 -std=c99 -Wall -Wextra -Werror -DECS_NO_DIRECT_THREADING=1 \
        -DECS_ARENA_BYTES=768 -DECS_FRAME_SEG_BYTES=384 \
        -o ecs-vm-budget ecs_vm.c ecs_main.c -lm
    echo "built: $(pwd)/ecs-vm-budget (cc=$CC, MCU 1KB 预算档)"

    # ---- RAM 门禁：instance ≤ 1024B（docs/VmBudget.md §预算表；跨指针宽度可移植口径） ----
    mem=$(./ecs-vm-budget --mem)
    echo "budget mem: $mem"
    instance=$(printf '%s' "$mem" | sed -n 's/.*instance=\([0-9]*\).*/\1/p')
    if [ -z "$instance" ] || [ "$instance" -gt 1024 ]; then
        echo "FAIL: VM 预算档实例 ${instance:-?}B 超出 1024B 上限"
        exit 1
    fi

    # ---- ROM 门禁：ecs_vm.c 纯文本段 ≤ 64KB（固件只编入 VM 本体；ecs_main.c 是 PC 参考宿主，
    #      其 printf/CLI 不代表 MCU 占用，故以目标文件为口径）。x86-64 gcc -O2 为代理平台。 ----
    "$CC" -O2 -std=c99 -Wall -Wextra -Werror -DECS_NO_DIRECT_THREADING=1 \
        -DECS_ARENA_BYTES=768 -DECS_FRAME_SEG_BYTES=384 \
        -c ecs_vm.c -o ecs-vm-budget.o
    trap 'rm -f ecs-vm-budget.o' 0
    SIZE="${ECSVM_SIZE:-}"
    if [ -z "$SIZE" ]; then
        for s in size llvm-size; do
            if command -v "$s" >/dev/null 2>&1; then SIZE="$s"; break; fi
        done
    fi
    if [ -z "$SIZE" ]; then echo "FAIL: 未找到 size/llvm-size" >&2; exit 1; fi
    text=$("$SIZE" ecs-vm-budget.o | awk 'NR==2 {print $1}')
    rm -f ecs-vm-budget.o
    echo "budget rom: ecs_vm text=${text}B (limit 65536B)"
    if [ -z "$text" ] || [ "$text" -gt 65536 ]; then
        echo "FAIL: VM 预算档文本段 ${text:-?}B 超出 64KB 上限"
        exit 1
    fi
    echo "budget gates: OK (rom ${text}B ≤ 64KB, ram ${instance}B ≤ 1KB)"
fi
