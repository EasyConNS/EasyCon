# EasyCon Flow 画布（Python 前端）

EasyCon 编排图（`*.flow.json`）的可视化画布：拖节点、连 exec/数据边、调参数、跑图看节点计时。
技术栈 **PySide6 + NodeGraphQt**（不引入 ComfyUI / torch 全家桶），环境用 **uv** 管理
（`pyproject.toml` + `uv.lock`，`src/` 布局）。

> **节点功能全部由 EasyCon 后端提供**：本前端不含任何节点实现，节点类型、端口、参数、
> slow 字段、按键名、OCR 后端选项全部来自 `GET /api/nodes`。后端加一个节点并登记到
> `FlowNodeCatalog`，画布重新加载目录后即出现，前端无需改动。

---

## 1. 先启动后端（二选一）

```bash
cd "/path/to/EasyCon"

# A) 只跑服务（画布 / 外部工具的后端）
dotnet run --project src/EasyCon2.CLI -- serve --port 19391

# B) 启动 GUI：内嵌同一个服务（默认 19391，可用 EC_FLOW_PORT 覆盖，0 = 关闭）
```

验证：

```bash
curl -s http://127.0.0.1:19391/api/health
curl -s http://127.0.0.1:19391/api/nodes | head -c 400
```

## 2. 启动画布

先装 [uv](https://docs.astral.sh/uv/)（`brew install uv` / `curl -LsSf https://astral.sh/uv/install.sh | sh`
/ `winget install astral-sh.uv`），然后：

```bash
cd frontend
uv sync                    # 建 .venv 并按 uv.lock 装依赖（首次约 100+ MB，PySide6）
uv run easycon-flow        # 打开界面（等价于 ./run.sh）
```

显式指定后端：

```bash
uv run easycon-flow --backend http://127.0.0.1:19391   # 也可用环境变量 EC_FLOW_URL
./run.sh --backend http://127.0.0.1:19391              # 同一个东西的便捷包装
```

自检（不需要界面；`--backend` 可选）：

```bash
uv run easycon-flow --self-test                        # 离线：画布 ↔ flow.json 往返（43 项）
uv run easycon-flow --self-test --backend http://127.0.0.1:19391   # 追加真后端端到端（48 项）

# 无头 UI 冒烟（真的建窗、载图、跑图、试跑节点；需要后端在跑）
QT_QPA_PLATFORM=offscreen uv run python -m easycon_flow.uitest --backend http://127.0.0.1:19391
```

> `uv run` 会自动按 `uv.lock` 同步环境，所以改完依赖只需 `uv sync`（或 `uv add <包>` 后重新 `uv lock`）。
> `.python-version` 固定 3.14（PySide6 6.11 / NodeGraphQt 0.6.44 的实测组合）；uv 会按需自动获取该版本。

## 3. 界面怎么用

| 区域 | 说明 |
|---|---|
| 左：节点库 | 按层（感知 / 决策 / 控制流 / 动作）分组，双击添加节点；悬停看端口与参数说明 |
| 中：画布 | exec 边（控制流）与数据边（图像/文本/数值）分别连到同名端口；`Delete` 删除节点。**参数直接在节点上编辑**：bool 勾选框 / int 微调框 / enum 下拉 / 字符串输入框内嵌在节点体内，path 参数带「…」选文件按钮，多行文本（如 script.run 的内联脚本）带「编辑…」对话框；与右侧属性面板实时同步 |
| 右：属性 | 选中节点后编辑参数、数据入边字面量、以及 **slow 慢感知**（`everyFrames` / `onChange` / `intervalMs`） |
| 下：运行 / 设备 | 设备面板（视频源、单片机、OCR 后端）、运行状态表（执行/复用/计时）、日志 |

工具栏：**运行图**（F5，提交当前画布给后端）、**停止**（Shift+F5）、**执行选中节点**（F6，双击节点同效）、
图名 / 看门狗步数 / 时长上限、**重载节点目录**（Ctrl+R）。

- **运行图**：`POST /api/flow/run` → 每 300ms 轮询 `/api/flow/status` → 画布按节点着色
  （绿=已执行，红=出错节点），状态表显示每个节点的执行次数、slow 复用次数、最近/累计耗时。
- **执行选中节点**（F6 / 双击节点本体）：`POST /api/node/run` 传整图 + `nodeId`，所以该节点的数据入边引用的
  上游会被一并求值（例如直接试跑 `ocr.text` 会自动先抓一帧），结果弹窗显示输出，输出里有图像时直接预览。
  **actuation 层（`pad.*` / `script.run`）会被后端拒绝**——试跑是只读的，不会驱动设备。
- **设备面板**：直接操作后端的设备状态（连接/断开采集卡与单片机、切换 OCR 后端）。
  视频源与单片机都用「刷新」重新扫描可用设备/串口（单片机下拉也可手输串口名）；
  OCR 的 PP-OCR 模型目录可点「打开目录…」用系统对话框选择。
- **打开 / 保存**：`*.flow.json` 与后端 `flow` 命令 / `POST /api/flow/run` 完全同格式，文件可互换。
  节点位置写在 `pos` 字段里（后端执行时忽略，仅用于布局）。

## 4. 目录

```
frontend/
├── pyproject.toml            项目元数据 + 依赖 + console script（uv / PEP 621）
├── uv.lock                   锁定版本（提交入库，uv sync 复现）
├── .python-version           固定 Python 3.14
├── run.sh                    便捷包装：uv run easycon-flow
├── README.md
└── src/easycon_flow/
    ├── flowdoc.py            flow.json 文档模型 + 画布映射（纯 Python，无 Qt，可单测）
    ├── api.py                后端 HTTP 客户端
    ├── nodes.py              目录 → NodeGraphQt 节点类；画布 ↔ 状态搬运
    ├── window.py             主窗口（画布 / 节点库 / 属性 / 设备 / 运行面板）
    ├── selftest.py           离线自检 + 对真后端的端到端往返（--backend）
    ├── uitest.py             无头 UI 冒烟（建窗、载图、跑图、试跑节点；需要后端）
    └── main.py               入口（console script: easycon-flow）
```

## 5. 常见问题

| 现象 | 原因 / 处理 |
|---|---|
| `ModuleNotFoundError: No module named 'distutils'` | Python ≥3.12 移除了 distutils，而 NodeGraphQt 仍在 import。`setuptools` 已写进 `pyproject.toml` 依赖（提供兼容层），跑过 `uv sync` 即不会再出现 |
| `uv sync` 报缓存目录不可写 | 受限环境（沙箱、CI）常见：`UV_CACHE_DIR=/tmp/uv-cache uv sync`（缓存位置与项目无关） |
| 界面提示「无法连接后端」 | 后端没起：先按第 1 节启动 `serve` 或 GUI；修好后点「重载节点目录」即可，无需重启界面 |
| 节点库是空的 | 同上；也可能后端目录拉取失败，看界面「日志」页签 |
| 连不上采集卡 | 同一块采集卡只能被一处独占：GUI 监控页连着时，画布这边请先断开监控页。单片机可先用 `mock` |
| 跑图不走 `chk.true` / `chk.false` 分支 | 条件节点的出口端口必须分开连；无标签 exec 边只作为兜底 |
| 感知节点总是拿到旧的画面 | 数据入边在同一张图里只惰性求值一次；把画面来源节点也接进 exec 路径（画布上正常串起来即可），或用 `slow` 明确表达复用意图 |

规范与后端契约见 [`docs/Flow.md`](../docs/Flow.md)。
