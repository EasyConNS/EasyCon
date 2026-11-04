#!/usr/bin/env bash
# 启动 EasyCon Flow 画布（环境由 uv 管理：pyproject.toml + uv.lock）。
#
#   ./run.sh                       # 打开界面（uv 首次会自动建 .venv 并装依赖）
#   ./run.sh --backend http://127.0.0.1:19391
#   ./run.sh --self-test           # 离线自检（画布 ↔ flow.json 往返），不开界面
#   ./run.sh --self-test --backend http://127.0.0.1:19391   # 追加对真后端的端到端检查
#
# 等价于：uv sync && uv run easycon-flow [参数…]
set -euo pipefail

cd "$(dirname "$0")"

if ! command -v uv >/dev/null 2>&1; then
    echo "[run.sh] 未找到 uv —— 本项目用 uv 管理 Python 环境与依赖。" >&2
    echo "  安装：curl -LsSf https://astral.sh/uv/install.sh | sh   （macOS/Linux）" >&2
    echo "        brew install uv ／ winget install astral-sh.uv" >&2
    echo "  也可不用本脚本，直接运行：uv run easycon-flow [参数…]" >&2
    exit 1
fi

# uv 会按 uv.lock 同步 .venv 后再执行 console script（首次运行会打印安装进度）
exec uv run easycon-flow "$@"
