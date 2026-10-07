#!/bin/sh
# ecs-vm CI 构建脚本（docs/VmDevPlan.md §5 V7；VM-Tiny 门禁 docs/VmTinyPlan.md T5）。
# 用法：build-vm.sh [tiny]
#   缺省      ：全量档 ecs-vm（零警告 -Wall -Wextra）。
#   tiny      ：额外构建 ecs-vm-tiny（-DECS_TINY）并执行容量门禁：
#               ROM：文本段 + 只读数据 ≤ 64KB；RAM：ecs_vm 实例（帧段 + 对象池）≤ 1024B。
# CI 环境可能没有 C 工具链：cc/gcc/clang 全部缺失时跳过（退出 0），有则构建并保持 -Wall -Wextra 零警告。
set -e

CC=""
for c in cc gcc clang; do
    if command -v "$c" >/dev/null 2>&1; then
        CC="$c"
        break
    fi
done

if [ -z "$CC" ]; then
    echo "skip: 未找到 C 编译器（cc/gcc/clang），ecs-vm 构建跳过"
    exit 0
fi

cd "$(dirname "$0")/../src/EasyCon.Vm/native"
"$CC" -O2 -std=c99 -Wall -Wextra -o ecs-vm ecs_vm.c ecs_main.c -lm
echo "built: $(pwd)/ecs-vm (cc=$CC)"

if [ "${1:-}" = "tiny" ]; then
    "$CC" -O2 -std=c99 -Wall -Wextra -DECS_TINY -o ecs-vm-tiny ecs_vm.c ecs_main.c -lm
    echo "built: $(pwd)/ecs-vm-tiny (cc=$CC, ECS_TINY)"

    # ---- RAM 门禁：实例 ≤ 1024B（docs/VmTinyPlan.md §2 预算表） ----
    mem=$(./ecs-vm-tiny --mem)
    echo "tiny mem: $mem"
    instance=$(printf '%s' "$mem" | sed -n 's/.*instance=\([0-9]*\).*/\1/p')
    if [ -z "$instance" ] || [ "$instance" -gt 1024 ]; then
        echo "FAIL: VM-Tiny 实例 ${instance:-?}B 超出 1024B 上限"
        exit 1
    fi

    # ---- ROM 门禁：文本段 ≤ 64KB（加载器/解释器/分配器 + 最小 libc） ----
    # macOS size 输出第 1 列 __TEXT；GNU size 第 1 列 text。两者皆取首列。
    text=$(size ecs-vm-tiny | awk 'NR==2 {print $1}')
    case "$(uname -s)" in
        Darwin) text=$((text)) ;;                     # mach-o __TEXT 字节数
        *) text=$((text)) ;;                         # ELF text 字节数
    esac
    echo "tiny rom: text=${text}B (limit 65536B)"
    if [ -z "$text" ] || [ "$text" -gt 65536 ]; then
        echo "FAIL: VM-Tiny 文本段 ${text:-?}B 超出 64KB 上限"
        exit 1
    fi
    echo "tiny gates: OK (rom ${text}B ≤ 64KB, ram ${instance}B ≤ 1KB)"
fi
