# Flow — 编排图（节点式流程）规范

> **定位**：EasyCon PC 侧节点编排系统的**现状规范**——`*.flow.json` 图格式、节点目录与语义、
> 执行模型（看门狗 / 计时 / 停止 / 慢感知）、HTTP 与 MCP 接口、Python 画布前端。
> 权威源：`src/EasyCon.Core/Flow/`（`FlowGraph` / `FlowNodeCatalog` / `FlowNodeRuntime` /
> `FlowExecutor` / `FlowServiceState` / `FlowServiceHttp` / `FlowServiceTools`）与
> `frontend/src/easycon_flow/`。冲突时以代码为准并修本文。
> 相关文档：能力装配见 [Framework.md](Framework.md)，脚本语言见 [Script.md](Script.md)，
> 字节码契约见 [VM2.md](VM2.md) / [VmSemanticContract.md](VmSemanticContract.md)。

## 0. 边界（先看这条，避免误用）

1. **图只在 PC 执行**。烧录单片机的产物永远只有纯 ECS 脚本（`.ecx`）；图不参与编译、不提供烧录入口。
2. **图是编排，不是语言**。图里的决策能力只有 `compare` / `text.contains` / `state.step`
   （外加感知节点）——复杂逻辑一律走 `script.run` 逃逸阀（写 ECS，拿 `ARG` 传参、`PRINT` 回传）。
3. **节点功能全部由后端提供**。前端（Python 画布）不含任何节点实现；节点类型、端口、参数 schema
   来自 `GET /api/nodes`，其源头是 `FlowNodeCatalog`。后端加节点不需要改前端。
4. **危险操作 fail-closed**：单节点试跑拒绝 actuation 层；MCP 默认不导出需要确认的工具。

---

## 1. flow.json 格式

普通 JSON（后缀 **`.flow.json`**），无 schema 版本演进负担（`version` 目前恒为 1）。
`pos` 仅供画布布局，执行时忽略。

```json
{
  "version": 1,
  "name": "battle-loop",
  "maxSteps": 3000,
  "timeoutSec": 1800,
  "nodes": [
    {
      "id": "cap",
      "type": "capture.frame",
      "pos": { "x": 10, "y": 40 },
      "params": { "waitForNew": true, "timeoutMs": 2000 },
      "slow": { "everyFrames": 2 },
      "inputs": {
        "image": { "node": "cap2", "port": "image" },
        "b": 3
      }
    }
  ],
  "exec": [ ["start", "cap"], ["chk.true", "hit"], ["chk.false", "miss"] ]
}
```

| 字段 | 语义 |
|---|---|
| `version` | 格式版本（当前 1） |
| `name` | 图名（报告/界面显示） |
| `maxSteps` | **看门狗**：节点执行步数上限（硬上限 100000，防图作者漏配） |
| `timeoutSec` | **看门狗**：整图总时长上限（秒） |
| `nodes[].id` | 节点唯一 id；**不要含点号**（exec 边用 `id.port` 表示条件出口） |
| `nodes[].type` | 节点类型（见 §2 目录） |
| `nodes[].params` | 节点参数（各类型自有 schema） |
| `nodes[].pos` | 画布坐标 `{x, y}`，执行忽略 |
| `nodes[].slow` | 慢感知策略（见 §4.2），null = 每次都执行 |
| `nodes[].inputs` | 数据入边：端口名 → 值。值为 `{"node":"id","port":"port"}` **引用**，或字面量（string/number/bool） |
| `exec` | exec 边（控制流）。两种形态：`["from","to"]` 与 `["from.outPort","to"]`，也接受 `{"from":..,"to":..}` |

### 1.1 exec 边的出口端口

- 单出口节点（`wait` / `capture.frame` / `ocr.text` / `script.run` / `pad.*` / `start` / `end`）
  写**裸节点 id**：`["cap","ocr"]`。
- 条件节点写**出口端口名**：`["chk.true","hit"]` / `["chk.false","miss"]`、
  `["step.out","body"]` / `["step.done","after"]`、`["chg.true","next"]`。
- 出口选择规则（`FlowExecutor.SelectEdge`）：节点返回出口端口时**优先精确匹配**该端口，
  其次才回落到无标签边。`["a.out","b"]` 与 `["a","b"]` 在语义上等价，但**无条件单出口必须写裸 id**
  （后端对无标签边做兜底匹配）——画布前端已按此规则写入。
- 无出边的节点执行完即视为整图正常结束（`Completed = true`）。

### 1.2 结构校验

`FlowGraph.Parse` 拒绝：重复节点 id、空图、exec 边引用不存在的节点。
起点解析：显式 `start` 节点优先；否则取**唯一**无 exec 入边的节点；全环图取第一个节点；
其余情况报错（提示无 exec 入边的节点列表）。

### 1.3 数据入边语义（重要）

- 引用形式 `{"node":"id","port":"port"}` 在**首次被读取时惰性求值**（递归执行上游节点）。
- 当次运行内**只求值一次**（memo）；exec 边上的节点每次执行都会刷新自身输出，
  因此「数据来自 exec 路径上游」恒为最新帧。
- 因此：**把感知来源节点也接进 exec 路径**（正常串起来）是推荐画法。若某个感知节点只作为数据来源
  （不在 exec 路径上），它整轮只跑一次——这与「每轮抓新帧」的期望不符，需要时请用 `slow` 明确表达复用意图。
- 数据边成环会被**显式拒绝**（报「数据入边存在环」），不会递归到栈溢出。
- 读取一个**不存在**的节点引用会报错。

---

## 2. 节点目录（`GET /api/nodes`，单一事实源）

端口列 `kind:type` 含义：`ein`=exec 入、`eout`=exec 出（名字即出口端口）、
`din`=数据入（可连线或给字面量）、`dout`=数据出。

<!-- 节点表由后端目录生成：改 FlowNodeCatalog 后请同步重跑
     curl -s http://127.0.0.1:19391/api/nodes 重新生成此表 -->

| 类型 | 层 | 说明 | 端口 | 参数（默认值） |
|---|---|---|---|---|
| `start` | 控制流 | 起点（图执行的入口） | `out`(eout:exec) | — |
| `end` | 控制流 | 终点（正常结束） | `in`(ein:exec) | — |
| `wait` | 控制流 | 延时 | `in`(ein:exec)、`out`(eout:exec) | `ms`=100(int) |
| `capture.frame` | 感知 | 抓取采集卡当前帧 | `in`(ein)、`out`(eout)、`image`(dout:image)、`frameIndex`(dout:number)、`x`/`y`/`w`/`h`(din:number) | `waitForNew`=false(bool)、`timeoutMs`=2000(int) |
| `image.file` | 感知 | 读取单张图片文件 | `in`(ein)、`out`(eout)、`image`(dout:image) | `path`(path) |
| `ocr.text` | 感知 | OCR 识别文字（整图或 ROI） | `in`(ein)、`out`(eout)、`image`(din:image)、`x`/`y`/`w`/`h`(din:number)、`text`(dout:text)、`conf`(dout:number) | `lang`="eng"、`x`/`y`/`w`/`h`=0(int) |
| `compare` | 决策 | 比较两个值，true/false 双出口 | `in`、`true`(eout)、`false`(eout)、`a`/`b`(din:any) | `op`="=="(`==`/`!=`/`>`/`<`/`>=`/`<=`) |
| `text.contains` | 决策 | 文本判定，双出口 | `in`、`true`、`false`、`text`(din:text)、`pattern`(din:text)、`hit`(dout:number) | `mode`="contains"(`contains`/`startsWith`/`endsWith`/`equals`/`regex`)、`pattern`=""、`ignoreCase`=true |
| `state.step` | 决策 | 运行内命名计数器（循环/重试） | `in`、`out`(eout)、`done`(eout)、`value`(din:number)、`value`(dout:number)、`name`(dout:text) | `name`="step"、`op`="inc"(`inc`/`set`/`reset`)、`value`=1、`max`=0 |
| `vision.changed` | 感知 | 与上次观察到的画面比较，双出口 | `in`、`true`、`false`、`image`(din:image)、`changed`(dout:number) | — |
| `script.run` | 动作 | 执行 `.ecs` 脚本（ARG 传参，PRINT 回传） | `in`、`out`、`arg1`/`arg2`/`arg3`(din:text)、`ok`(dout:number)、`logs`(dout:text) | `file`(path) |
| `pad.key` | 动作 | 按键 | `in`、`out` | `key`="A"、`durationMs`=100、`times`=1、`intervalMs`=100 |
| `pad.sequence` | 动作 | 按键序列宏（`"A,100; ↓,200"`） | `in`、`out` | `seq`(string) |

`/api/nodes` 还返回：`slow`（慢感知字段说明）、`keys`（手柄键名，供 `pad.key` 下拉）、
`ocrBackends`（`none`/`tesseract`/`ppocr`）。

### 2.1 各节点语义要点

- **`capture.frame`**：`x/y/w/h` 是**数据入边**（不是参数），缺省 `-1` 表示整帧；
  `waitForNew = true` 时等待采集源发布新帧（需 `FrameIndex`；`timeoutMs` 到期按当前帧继续，
  等待可被停止令牌打断）；输出 `frameIndex` 供慢感知与诊断使用。
- **`ocr.text`**：`image` 缺失直接报错（不会静默取当前帧）。ROI 的 `x/y/w/h` 既可连线也可写参数
  （入边优先）。OCR 引擎是**宿主装配**的：服务侧默认 `none`（未装配 → 节点报错），
  用 `POST /api/options` 切换。`conf` 是后端最近一次识别的置信度（0-100）。
- **`compare`**：数值优先（int/double/可解析字符串），否则字符串序比较；出口 `true`/`false`。
- **`text.contains`**：`regex` 模式带 200ms 匹配超时（坏正则不挂死整图，按未命中处理并记日志）。
- **`state.step`**：运行内命名计数器，`inc` 累加 `value`（`value` 是**数据入边优先**，参数为兜底）；
  `set` 直接设定；`reset` 归零。`max > 0` 且当前值 ≥ `max` 时走 `done` 出口，否则 `out`。
  状态随本次运行存亡（不上 MCU、不落盘）。
- **`vision.changed`**：与本节点在本次运行内**上一次观察到的画面**比较；首次执行只建立基线
  （`changed = 0`），因此「等待画面变化」不会在第一帧误触发。指纹取 `image` 入边（内容即输入），
  缺省取当前帧的编码字节。**不要给它配 `slow`**（复用会跳过基线更新）。
- **`script.run`**：脚本路径相对路径按 `__APP__` 解析；`arg1..arg3` 传成 `ARG(1..3)`；
  `PRINT` 输出以 `\n` 连接进 `logs`；`ok = 1` 表示执行完成。脚本内的能力（采集/OCR/推理/手柄）
  以**借用**方式来自本次运行（`ScriptHostAssembler` 的 `BorrowResources`），
  内层释放不会拆掉外层还在用的服务。
- **`pad.key` / `pad.sequence`**：需要单片机（真机或 `mock`）；`pad.sequence` 的键名支持
  `↑↓←→` 归一化为 `TOP/DOWN/LEFT/RIGHT`；两者都可用停止令牌打断。

---

## 3. 执行模型

```
FlowExecutor.Run(token)
  ├─ FlowNodeRuntime: 节点语义 + 数据入边（惰性 + memo + slow 复用）
  └─ 从 start 沿 exec 边推进，直到无出边（Completed）或出错/看门狗/停止
```

**每步**：执行节点 → 记 `ExecCount` / `ReusedCount` / `LastMs` / `TotalMs` → 写事件
（`nodeEnd <id> <type> <ms>ms`，slow 复用则为 `nodeReuse ...`）→ 选下一条 exec 边。

| 机制 | 行为 |
|---|---|
| 看门狗（步数） | `steps >= min(maxSteps, 100000)` → `WatchdogTriggered`，事件 `watchdog maxSteps` |
| 看门狗（时长） | `elapsed >= timeoutSec` → `WatchdogTriggered`，事件 `watchdog timeout` |
| 停止 | 外部 `CancellationToken`；`wait` / 等待新帧 / `pad.*` 间隔都用 `token.WaitHandle.WaitOne`，可被立即打断 |
| 出错 | 记录 `Error` + `ErrorNodeId`，事件 `error <id> <msg>`，整图停止（不做隐式重试） |
| 报告 | `Steps` / `TotalMs` / `Records[nodeId]`（含 `LastOutputs`，供 `/api/flow/status` 与试跑回传） |
| 事件流 | `ConcurrentQueue<string>`，服务轮询取快照，前端据此高亮与显示计时 |

### 3.1 慢感知（`slow`）

写在**节点上**（不是 `params` 里），把「多贵」与「多新」解耦：

```json
{ "id": "ocr", "type": "ocr.text", "slow": { "everyFrames": 3, "onChange": true, "intervalMs": 500 } }
```

| 字段 | 命中条件（满足即**复用上次输出**、不重复执行） |
|---|---|
| `everyFrames` | 采集源帧号推进 **< N** 帧（源不提供帧号 → 判定不了 → 保守重算） |
| `onChange` | 感知输入指纹与上次**相同**（指纹取 `image` 入边，缺省取当前帧编码字节） |
| `intervalMs` | 距上次执行 **< N 毫秒** |

多个条件同时给出取**最保守**语义：任一条判定「该重算」就重算；全部判「可复用」且存在上次缓存才复用。
空对象 `{}` = 永不复用（等于不写 `slow`）。复用时会记 `ReusedCount++` 并发 `nodeReuse` 事件。
`onChange` 的指纹只在配置了该字段时才计算（否则每步都会白抓一帧）。

### 3.2 单节点试跑（`POST /api/node/run`）

语义 = 「执行画布上这一个节点」，与整图执行**共用同一个 `FlowNodeRuntime`**，所以试跑结果可信。

- 独立模式：`{type, params, inputs}`（`inputs` 只接受字面量）。
- 图中模式：`{graph, nodeId}`——数据入边引用的上游会被一并惰性求值
  （画布上双击 `ocr.text` 会自动先抓到一帧）。
- 返回 `{ok, ms, reused, port, outputs, error}`；`port` 是 exec 出口端口
  （`compare` 这类条件节点只有出口、没有数据输出）。
- **只读边界**：actuation 层（`pad.*` / `script.run`）直接拒绝；并且 `ExecuteGuard`
  会拦下**经数据入边间接到达**的 actuation 节点（只校验目标节点的层是不够的）。
- 超时 15 秒；引擎每次试跑单独构建并释放（服务侧 OCR 引擎是共享的，按借用语义传入）。

---

## 4. HTTP API（loopback，无鉴权）

默认端口 **19391**；GUI 内嵌服务用 `EC_FLOW_PORT` 覆盖（0 = 关闭）；CLI 用 `serve --port`。
绑定 `127.0.0.1`，不要暴露到外网。

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/health` | `{ok, service}` |
| GET | `/api/nodes` | 节点目录（§2，画布渲染的单一事实源） |
| GET | `/api/options` | `{appDir, ocr:{backend, modelDir, engineCreated, error, options}}` |
| POST | `/api/options` | `{ocrBackend, ocrModelDir}` 切换 OCR 后端（`none`/`tesseract`/`ppocr`；ppocr 必须给目录） |
| GET | `/api/device/video` | `{connected, sources:[{index,name}]}` |
| POST | `/api/device/video/connect` | `{index, api}`（`api` 为 OpenCV VideoCaptureAPIs 数值，默认 0） |
| POST | `/api/device/video/disconnect` | — |
| GET | `/api/device/mcu` | `{connected, ports:[...]}` |
| POST | `/api/device/mcu/connect` | `{port}`（`"mock"` = 无硬件虚拟手柄） |
| POST | `/api/device/mcu/disconnect` | — |
| POST | `/api/flow/run` | `{json}`（图 JSON 字符串）或 `{path}`（图文件路径）→ `{runId}` |
| POST | `/api/flow/stop` | `{runId}` → 请求停止（协作式取消） |
| GET | `/api/flow/status?runId=` | `{runs:[{runId,name,status,steps,totalMs,error,errorNode,events[],records[]}]}`（`runId` 缺省列全部） |
| POST | `/api/node/run` | §3.2 单节点试跑 |

`status` 取值：`running` / `completed` / `stopped` / `watchdog` / `error`。
出错时 HTTP 状态码 400 且体里带可读 `error`。

### 4.1 MCP / agent 工具（同源）

`FlowServiceTools.RegisterAll(registry, state)` 注册到同一 `FlowServiceState`，能力与画布对等：

| 工具 | 需确认 | 说明 |
|---|---|---|
| `flow_device_query` | 否 | `kind=video｜mcu｜nodes` 查询设备与节点目录 |
| `flow_device_connect` | **是** | 连接/断开视频源与单片机 |
| `flow_run` | **是** | 运行图（`graph` 或 `path`） |
| `flow_control` | 否 | `action=status｜stop` |
| `flow_node_run` | 否 | 单节点试跑（actuation 层被拒 → 天然只读） |

GUI 启动时把 Flow 服务与这些工具一并装配进 GUI 的共享注册中心；MCP 端点仍按 fail-closed 过滤
需要确认的工具（`flow_device_connect` / `flow_run` 默认不导出）。

---

## 5. 宿主与装配

- **能力来源**：`FlowHostContext` 只带端口（`ICaptureSource` / `IVisionService` / `IOcrService` /
  `IInference` / `IPadInput`）与 `AppDir`；null 一律表示「该能力不可用」，相关节点报错而非静默降级。
- **唯一装配点**：服务侧每次运行/试跑经 `ScriptHostAssembler.Assemble` 组装一次
  （`FlowServiceState.BuildRunContext`），采集源与手柄以**端口**传入，OCR/推理为**共享借用**
  （`BorrowResources = true`），由 `FlowServiceState` 自己持有并在 `Dispose` 释放。
  `script.run` 节点同样走装配器（不再手写 `CapabilitySet`）。
- **采集源**：`FrameStoreCaptureSource`（最新帧 + 单调 `FrameIndex`）——`waitForNew` 与
  `slow.everyFrames` 依赖帧号。旧的委托适配 `DelegateCaptureSource` 不提供帧号，并把
  「采集卡检查异常」等哨兵字符串归一为 `null`（避免被当成 Base64 图像）。
- **设备持有**：`FlowServiceState` 自己持有采集卡/单片机连接状态。
  GUI 监控页另持一份，**同一块采集卡不要两处同时打开**；画布流程统一走 `/api/device/*`。

### 5.1 命令行

```bash
# 跑图（无 UI）：--port mock 虚拟手柄，--video -1 不接采集源
dotnet run --project src/EasyCon2.CLI -- flow examples/battle.flow.json --port mock --video -1
dotnet run --project src/EasyCon2.CLI -- flow x.flow.json --max-steps 500 --timeout-sec 60

# 起服务（画布/外部工具的后端）
dotnet run --project src/EasyCon2.CLI -- serve --port 19391
```

`flow` 子命令走装配器，因此 **OCR 默认可用（tesseract）**；`serve`/GUI 的服务侧 OCR 默认 `none`，
需要 `POST /api/options` 显式开启（避免无人值守服务悄悄加载模型）。

---

## 6. Python 画布前端（`frontend/`）

技术栈 PySide6 + NodeGraphQt（不引入 ComfyUI/torch）；环境用 **uv** 管理
（`pyproject.toml` + `uv.lock`，`src/` 布局，console script `easycon-flow`）。
详见 [`frontend/README.md`](../frontend/README.md)。

| 文件 | 职责 |
|---|---|
| `frontend/pyproject.toml` / `uv.lock` / `.python-version` | 依赖与锁定（PEP 621 + uv）；Python 固定 3.14 |
| `frontend/src/easycon_flow/flowdoc.py` | flow.json 文档模型 + 画布映射（纯 Python，无 Qt，可单测） |
| `frontend/src/easycon_flow/api.py` | 后端 HTTP 客户端 |
| `frontend/src/easycon_flow/nodes.py` | 目录 → NodeGraphQt 节点类；画布 ↔ 状态搬运（含预置属性名冲突规避 `p_` 前缀） |
| `frontend/src/easycon_flow/window.py` | 主窗口：画布 / 节点库 / 属性（含 slow） / 设备面板 / 运行面板 |
| `frontend/src/easycon_flow/selftest.py` | 离线自检 + 对真后端的端到端往返（`--backend`） |
| `frontend/src/easycon_flow/uitest.py` | 无头 UI 冒烟（建窗、载图、跑图、试跑节点；需要后端） |

启动与自检：

```bash
cd frontend
uv sync                                  # 建 .venv 并按 uv.lock 装依赖
uv run easycon-flow                      # 打开界面（等价 ./run.sh）
uv run easycon-flow --self-test          # 离线自检（35 项）
uv run easycon-flow --self-test --backend http://127.0.0.1:19391   # +真后端端到端（40 项）
QT_QPA_PLATFORM=offscreen uv run python -m easycon_flow.uitest --backend http://127.0.0.1:19391
```

依赖坑：NodeGraphQt 0.6.x 仍 `import distutils`，Python ≥3.12 已移除该模块 →
`pyproject.toml` 里的 `setuptools` 是**必需项**（提供兼容层）。

---

## 7. 已知限制与挂账

| 项 | 现状 / 原因 |
|---|---|
| `ocr.screen`（det+rec 全屏 → lines 数组） | **挂账**：Core 的 `IOcrService` 由单行识别器支撑（`EngineCacheOcrService.GetOrInit` → `IOcrRecognizer`），全屏多行需要先暴露 `IOcrEngine` 端到端引擎（Capture 层已有，含 PP-OCRv5 det+rec）。当前 `ocr.text` 走宿主 OCR 通路，`lang`/ROI 由后端决定 |
| `vision.label`（标签匹配） | **挂账**：需要宿主注入手柄式视觉原料（GUI 的 `LabelMatch` 委托）到 `FlowServiceState`；目前服务侧 `Vision = null`，`vision.*` 只有 `vision.changed`（不依赖 `IVisionService`）可用 |
| `switch`（多路分支） | **挂账**：静态端口目录表达不了动态出口；当前用 `compare`/`text.contains` 链或 `state.step` + `script.run` 替代 |
| `ai.agent`（函数调用回环节点） | **挂账**：需要 provider 抽象（ollama/vllm/siliconflow/onnx/script）+ 按图配置的回调工具集，且默认只读、`pad` 类需显式授权 |
| `subgraph`（子图节点） | **挂账**：图内嵌图，需要节点目录支持动态展开 |
| 数据入边新鲜度 | 纯数据边（上游不在 exec 路径上）整轮只求值一次（§1.3）；需要逐轮新鲜时把来源节点接进 exec 路径 |
| `slow` 与 GUI 联动（暂停/继续） | 未实现：目前只有停止（协作式取消），没有暂停/单步 |

> 新增节点必须同时登记 `FlowNodeCatalog`（前端据此渲染）与 `FlowNodeRuntime`（执行语义）；
> `FlowNodeCatalogTests.Catalog_EveryNodeTypeIsImplementedByRuntime` 会遍历目录逐个执行，
> 抓出「目录里有、运行时没有」的漂移。
