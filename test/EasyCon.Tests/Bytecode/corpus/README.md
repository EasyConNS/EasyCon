# ECS 双实现对拍语料

每用例 = `<名>.ecs` + `<名>.expected`（期望输出行，逐行）+（可选）`<名>.events`（域事件）。
执行器：`CorpusCrossValidationTests`（数据驱动，每个 .ecs 一个测试用例）。

## 文件格式

- `.ecs`：ECS 源码（单文件；CompileSource 路径，无模块导入）。
- `.expected`：期望 stdout 行（每行一条，FWRITE 行断已拼行；纯事件用例可省略）。
- `.events`：期望域事件（可选）。行格式与 `EcxHost.EnableRecording` 一致：
  `KEY <键码> <时长>` / `KEYST <键码> <0|1>` / `STICK <侧> <x> <y>` /
  `STICKC <侧> <x> <y> <时长>` / `WAIT <毫秒>` / `AMIIBO <槽>` / `BEEP <频率> <时长>`。
  按**标签分组**存储：KEY → KEYST → STICK → STICKC → WAIT → AMIIBO → BEEP
  （与 C VM stderr TSV 的分组对拍语义一致）。

## 对拍语义

1. `CompileSource → EcxImage → EcxInterpreter`：输出/事件必须与期望逐行一致；
2. 有 cc 时同一镜像交 C VM（`ci/build-vm.sh` 同机制现场编译）三方对拍；无 cc 跳过 C VM 侧。

## 新增语义用例的流程

1. 加两个文件：`<名>.ecs` + `<名>.expected`（+ 可选 `<名>.events`），跑一次测试；
2. 双端不一致时**先判定哪端错**：修实现而非改期望（**期望即规格**）。
   判定参照：`docs/VM2.md` §3 语义规格表（S-01..S-18）与 `EcxInterpreter` 注释；
3. 禁止为让测试通过而放宽断言（如排序、过滤行）；确属规格演进时先改文档再改期望并注明依据。

## 覆盖映射（迁移自）

| 用例 | 迁移自 |
|------|--------|
| arith | CvmCrossValidationTests.TwoWay_Arithmetic_ControlFlow_Recursion |
| strings_arrays | CvmCrossValidationTests.TwoWay_StringsArrays |
| structs | CvmCrossValidationTests.TwoWay_Structs |
| events | CvmCrossValidationTests.TwoWay_DomainEvents |
| mixed_semantics | PipelineUnificationTests.SingleFile_MixedSemantics |
| key_events | PipelineUnificationTests.KeyEvents |

结构断言（产物体积、模块清单、缓存统计、错误码、NeedIL 拒绝、调用深度上限）保留在原测试文件，不强行语料化。
