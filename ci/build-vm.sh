#!/bin/sh
# ecs-vm CI 构建脚本（docs/VmDevPlan.md §5 V7）。
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
