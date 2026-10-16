# Maa P1 底座：Windows x64 软件验收（2026-10-16）

基线：上游 dev `d00b9284b8d835f91166c96a393031e3d7ff06c1`。范围为实施任务书 2026-10-16 更新后的 P1-01～04 与 P2 接口预留；架构和复验命令见 [P1.md](P1.md)。

## 已完成

- 手柄与采集两个平级模块分别持有物理连接、代次和使用租约。Maa 绑定只借用固定端口；独立只读绑定的停止不释放输入。
- GUI、CLI、FlowService、SDL、AI 能力装配与虚拟手柄入口统一接到公开门面。纯计算不连接硬件，单侧使用不要求另一侧连接。
- 保留原串口协议，取消先停止旧运行，再确认中性 HID 写出、排空回调并归还 epoch。迟到的旧连接事件、KeyUp/Inactive 和采集帧不能跨代次作用。
- 来源枚举记录后端和稳定身份；同名项可区分，刷新/消失不改选首项。输入与采集配置独立保存，CLI 覆盖一侧不修改另一侧。
- 帧统一 BGR8、1920×1080；像素与元数据在同一槽发布，PNG 按槽缓存。停止超时保留阻塞 Read 的原生所有者。
- 空 bootstrap 显式选择 CPU，避免每个短任务重复触发 Auto 推理设备初始化。设备写循环使用专用线程，避免长期阻塞线程池后漏掉短按。
- OCR/推理资源逐项拥有或借用。旧 `ImgLabel.Search`、GUI 截断和 CLI 向上取整保留；P1 未要求修改脚本阈值。

## 本地检查

| 检查 | 结果 |
|---|---|
| `.NET 10.0.400` Debug 全方案构建 | 0 错误；有 nullable/Avalonia 等警告 |
| `dotnet format --verify-no-changes --no-restore` / `git diff --check` | 通过 |
| Core | 1320 通过，3 跳过；包含真实 Maa、模块独立和 1000 次生命周期回归 |
| LSP / SDL | 67 / 18 通过 |
| GUI 服务 / Headless 界面 | 168 / 47 通过 |
| 两种编辑器 Ctrl+S 单独运行 | 2 通过 |
| 额外 MinGW x86 C VM / bytecode 差分 | 378 通过，1 跳过 |
| C VM x64 / x86 budget 门禁 | RAM 944 / 892 B；ROM 39124 / 50064 B，均在预算内 |
| Windows x64 GUI + CLI 发布 | 通过；沿用仓库的 framework-dependent 单文件发布方式 |
| 发布目录 `maa doctor` | native v5.14.3 / binding 5.10.0；实际库从发布目录加载；bootstrap、无控制器 Tasker、DirectHit 和 PNG 像素往返通过 |
| 发布程序无硬件 ECS / 中文路径 Flow | 原算术 corpus 和纯计算 Flow 正常完成；Flow 指定无效端口仍可运行纯计算 |
| 原文件摘要 | 仓库跟踪的 28 份 ECS/IL/Flow 文件与基线 blob 相同；未覆盖改写用户外部资源 |

普通回归共 **1620 项通过**，加上单独 Ctrl+S 的 2 项为 **1622 项通过**。x86 的 378 项是额外复跑，不重复算入该数字。显式人工截图/真实窗口用例和缺模型/平台的用例分别记录在 TRX 中，不作为 Maa 关键用例通过的替代。

全方案运行中的 1000 次输入启动/停止：P50 62.62 ms、P95 73.45 ms、P99 141.79 ms、最大 224.25 ms；每次结束 native 绑定归零，采样时活动帧租约为 0，预热后私有内存没有逐轮增长。这里的时长含输入投递、停止、原生排空及中性写出；它不是游戏反馈或模板/OCR 时延。

## 证据与范围

验收收集脚本在本地生成 `acceptance/<commit>/windows-x64/`：依赖与发布文件摘要、build/test 日志、TRX、原生加载路径、实际 fake IConnection 写出事件、帧/来源/epoch 快照、M01～M17 软件证据映射、性能和生命周期采样、原文件摘要、入口旁路清单及待验收项。未观察到的字段不推断补齐。CI Develop 上传 TRX 和 Maa 自检输出，并继续执行独立的 C VM 32/64 位门禁。

M01～M17 的 **P1 软件断言**通过：真实 Maa native + fake IConnection / 可控帧源，或对应来源目录、配置、ViewModel 契约。M02/M03 的识别部分随 P2；物理驱动热插拔及 GUI/CLI 对真实端点的 replay 仍待实机验收。

以下不属于本批已完成结论：P2 标签转换与 Maa 旧评分/OCR 迁移，P3 RunEcs，P4 Maa Pipeline 推进 Flow，P5 完整任务面板与跨平台发行验收。真实串口/采集卡、游戏反馈、30 分钟业务任务、macOS/Linux Maa 发布包仍 pending。Windows GUI 的可核实来源枚举使用 DirectShow；其他后端缺身份映射时明确失败。

回退应同时恢复原程序及配套 runtime/资源。P1 没有覆盖旧 ECS/IL/Flow 或原标签文件；当前包不是已完成旧识图迁移的发行版。
