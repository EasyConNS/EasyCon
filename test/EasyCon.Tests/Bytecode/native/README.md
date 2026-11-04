# P0 原生 ABI 验收资产

`p0_probe.c` 只包含公共 ecs_vm.h，不访问 VM 私有结构；由 CvmP0Tests 编译成普通/budget
两档，并分别使用 computed-goto 与 switch 派发。相同测试可由 ECSVM_CC 指定 32/64 位编译器。

`p0-legacy.ecx` 是以 dev `78d294b6` 未改变的 C# ECX1 writer 生成的固定 134B 镜像：
常量为 DOUBLE 1.5、DOUBLE 2.5、UINT64_MAX，12 个槽；执行 `LoadK, LoadK, AddD(目标=左源),
CallN probe, LoadK, CallN probe, Ret0`。期待输出为 DOUBLE 4.0 与 UINT64_MAX 的原始位模式。
它直接交两端执行，不重新编译 ECS，不在测试中重新生成资产。

SHA-256：`681429131EB83F03762413542F2C5B8BC113AA681365C3D4614AB693E0654089`。
该样本仅锁定所含指令与宽常量的兼容性，全部格式/操作码契约仍由现有 Header/ABI/Corpus/FullChain 测试承担。
