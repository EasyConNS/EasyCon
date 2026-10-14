# C VM 值槽、宿主 ABI 与容量预算

## 1. 来源与兼容边界

P0 基于上游 dev `78d294b61fcc0d7e4059c2a6f76d20c6d4e62db5`。
引入 `0000-cvm.patch`，来源提交 `8cded4a0282cd1d8e0f4bdb639a7705c37f5570c`，
作者 jinbo，日期 2026-10-13；补丁 SHA-256：
`F4405009AD39260AE5F0F8993F72630291B7BDA46B51408EA210EC8F08F02647`。

这次改变 C 运行时内存表示与宿主 C ABI，不改变 ECS 源码、标签编号、操作码、ECX1 文件格式
或镜像 `ECS_ABI_REV=1`。C# `TaggedValue` 仍为 16B。所有链接 C VM 的原生宿主必须重新编译。

## 2. 值模型与所有权

- C `ecs_value` 为 8B，BOOL/BYTE/INT/UINT 内联于 32 位载荷。
- DOUBLE/UINT64/PTR 的值槽保存对象池箱句柄；箱中保存 8B 原始位模式。PTR=-1 与 64 位边界不可截断。
- 容器引用和宽值箱参与引用计数；pinned 常量字符串直接借用镜像，不占对象池。
- 数组内宽元素裸存 8B；读到值槽时创建箱；数组内部复制不创建临时箱。
- 宿主读取宽值用 `ecs_vm_arg_wide`；读取 DOUBLE 用 `ecs_vm_arg_double`。
- 返回宽值用 `ecs_vm_ret_wide`，tag 只允许 PTR/UINT64；DOUBLE 用 `ecs_vm_ret_double`。
  字符串用 `ecs_vm_ret_str`。这些辅助函数创建出生引用，CallN 接管；借用的 args 句柄不得直接赋给 ret。
- 宽返回函数返回 ECS_OK/TYPE/POOL；失败时 ret=VOID。VM 同时记录错误，宿主忽略返回码也不能伪造成功。
  缺少能力的合法 -1 哨兵与对象池耗尽必须区分。

## 3. 容量档案

| 项目 | 普通参考宿主 | budget 档 |
|---|---:|---:|
| arena | 256KiB | 768B |
| 帧段 | 128KiB | 384B |
| 对象段 | 128KiB | 384B |
| 对象块 | 128B | 128B |
| 对象块数 | 1024 | 3 |
| 每值槽 | 8B | 8B |
| 派发 | GCC/Clang computed-goto；其他编译器 switch | C99 switch（ECS_NO_DIRECT_THREADING=1） |

对象头为 24B，单块剩余载荷 104B；字符串、数组、结构体和宽值箱共享同一池。
预算档只有 3 个同时存活的对象块，不能承诺任意桌面脚本都能运行。
帧表存 arena 偏移；帧头尺寸仍与目标指针宽度有关。配置必须满足 8B 对齐和块容量约束，编译期检查。

255 个槽是字节码格式上限。384B 帧段无法同时容纳 255 个 8B 值槽；设备适配仍需容量预检。
`ECS_ERR_POOL` 是正常的资源性拒绝，可与无池的 C# 参考解释器发生资源性差异；错误现场必须可定位。

## 4. WAIT 续跑

缺省 `ecs_vm_set_wait_yield(vm, 0)`：WAIT 回调保持桌面阻塞约定。
开启后，WaitI/WaitV 通知宿主时长并返回 `ECS_WAITING=19`，续跑点在 WAIT 后继。
宿主保存截止时间，在 tick 到期后才再次调用 run；VM 不代替硬件时钟安排等待，不能立即循环吸收 WAITING。
取消可立即调用 run，返回 CANCELLED；等待回调中取消优先于 WAITING。结束或错误后释放 VM。

## 5. 构建与门禁

```sh
sh ci/build-vm.sh
sh ci/build-vm.sh budget
ECSVM_REQUIRE_CC=1 dotnet test test/EasyCon.Tests -c Release --filter 'FullyQualifiedName~Bytecode'
```

`ECSVM_CC` 可指定编译器或包装脚本；Linux 32 位 CI 用 `ci/cc32.sh`（需要 gcc-multilib）。
普通/budget 都使用 C99、-O2、-Wall、-Wextra、-Werror。budget 或 ECSVM_REQUIRE_CC=1 缺工具链时失败。
budget 选择现有 switch 派发以控制 ROM；32 位 MinGW GCC 8.1 的 computed-goto 对象文件 text
为 85,472B，同样 -O2 的 switch 为 50,064B。两种派发使用同一 handler，均由 P0 测试执行；
这项选择不承诺运行速度提升，也不能用代理平台 text 数字替代真实 MCU 链接 map。
`ECSVM_SIZE` 可指定 size 工具，默认探测 size/llvm-size。

补丁定义的容量门禁为：`sizeof(ecs_vm)-sizeof(ecs_host) <= 1024B`，
以及 budget 编译出的 `ecs_vm.o` text（size 首列，含该工具归入的只读段）<=65536B。
编译器/架构会影响数值，报告必须带版本和目标架构。

这不是固件总占用：门禁不含宿主回调、解析元数据、镜像副本、线程栈、通信缓冲及外设状态。
`ecs_vm_load` 借用有效镜像，可供 XIP；`ecs_vm_load_io` 加载期分配整镜像副本。
真实 MCU 的链接 map、栈和缓冲占用必须在对应固件工程中测量，本仓库的 PC 代理结果不能替代它。

P0 验收由既有 Corpus/Fuzz/FullChain/ABI/Pool 差分，以及 CvmP0Tests 的公开 ABI 探针共同承担。
新增探针覆盖两种派发、宽值位模式、源/目标重合、3 块池的重复返回与数组操作、资源错误位置、
嵌套视图/深拷贝和 WAIT/取消。CI 显式执行普通与 budget 构建，并执行 32/64 位差分；关键验证不允许无 cc 时跳过。
