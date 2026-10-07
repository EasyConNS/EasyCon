"""入口：启动 EasyCon Flow 画布。

用法：
    python -m easycon_flow.main [--backend http://127.0.0.1:19391]

后端地址也可用环境变量 EC_FLOW_URL 指定；界面内可随时“重载节点目录”重连。
"""

from __future__ import annotations

import argparse
import os
import sys

from PySide6 import QtWidgets

from .api import ApiError, DEFAULT_BASE_URL, FlowApi
from .window import FlowWindow


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="EasyCon Flow 编排画布（后端必须已启动）")
    parser.add_argument("--backend", default=None,
                        help=f"后端 Flow 服务地址（默认 {DEFAULT_BASE_URL}，可用 EC_FLOW_URL 覆盖）")
    parser.add_argument("--self-test", action="store_true", help="只做离线自检，不打开界面")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    argv = list(argv if argv is not None else sys.argv[1:])
    args = parse_args(argv)

    if args.self_test:
        # 自检用独立参数表；显式给了 --backend 才跑对真后端的端到端检查
        from .selftest import run_self_test
        return run_self_test(["--backend", args.backend] if args.backend else [])

    app = QtWidgets.QApplication.instance() or QtWidgets.QApplication(sys.argv)
    app.setApplicationName("EasyCon Flow")

    api = FlowApi(args.backend or DEFAULT_BASE_URL)
    window = FlowWindow(api)

    try:
        api.health()
    except ApiError as exc:
        window.log(f"[后端] 连接失败：{exc}")
        QtWidgets.QMessageBox.warning(
            window, "后端未就绪",
            f"{exc}\n\n启动后端之一：\n"
            "  1) dotnet run --project src/EasyCon2.CLI -- serve --port 19391\n"
            f"  2) 启动 EasyCon GUI（内嵌服务，默认 19391；端口 {os.environ.get('EC_FLOW_PORT', '19391')}）\n\n"
            "界面已打开，修复后点“重载节点目录”即可。")

    window.show()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
