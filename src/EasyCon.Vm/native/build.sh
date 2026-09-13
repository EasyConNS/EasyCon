#!/bin/sh
# ecs-vm 构建脚本（无 cc 时 CI 跳过）
set -e
cd "$(dirname "$0")"
cc -O2 -std=c99 -Wall -Wextra -o ecs-vm ecs_vm.c ecs_main.c -lm
echo "built: $(pwd)/ecs-vm"
