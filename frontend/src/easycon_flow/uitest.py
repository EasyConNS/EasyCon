"""无头 UI 冒烟测试：真的把窗口建起来、载图、跑图、试跑节点。

    QT_QPA_PLATFORM=offscreen python -m easycon_flow.uitest [--backend URL]

不需要显示器（offscreen 平台插件），但**需要后端在跑**——它走的是与界面完全相同的
HTTP 路径（/api/nodes、/api/flow/run、/api/flow/status、/api/node/run）。
适合在改动画布代码后快速确认「窗口能开、图能载、图能跑、节点能试跑」。
"""

from __future__ import annotations

import argparse
import sys
import time

from PySide6 import QtWidgets

from .api import ApiError, DEFAULT_BASE_URL, FlowApi
from .nodes import collect_connections
from .window import FlowWindow

SMOKE_GRAPH = {
    "name": "ui-smoke",
    "maxSteps": 50,
    "timeoutSec": 30,
    "nodes": [
        {"id": "start", "type": "start", "pos": {"x": 0, "y": 0}},
        {"id": "chk", "type": "text.contains", "pos": {"x": 120, "y": 0},
         "params": {"mode": "contains", "pattern": "TAUROS"},
         "inputs": {"text": "Lv25 TAUROS No.128"}},
        {"id": "step", "type": "state.step", "pos": {"x": 260, "y": 0},
         "params": {"name": "n", "op": "inc", "value": 1, "max": 2}},
        {"id": "wait", "type": "wait", "pos": {"x": 400, "y": 0}, "params": {"ms": 1}},
        {"id": "end", "type": "end", "pos": {"x": 540, "y": 0}},
    ],
    "exec": [["start", "chk"], ["chk.true", "step"], ["chk.false", "end"],
             ["step.out", "wait"], ["wait", "step"], ["step.done", "end"]],
}


def run_ui_test(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="EasyCon Flow 画布无头 UI 冒烟测试")
    parser.add_argument("--backend", default=DEFAULT_BASE_URL)
    args = parser.parse_args(list(argv if argv is not None else sys.argv[1:]))

    app = QtWidgets.QApplication.instance() or QtWidgets.QApplication([])
    api = FlowApi(args.backend)

    try:
        api.health()
    except ApiError as exc:
        print(f"[uitest] 后端不可用：{exc}")
        return 2

    failures: list[str] = []
    passed = 0

    def check(condition: bool, message: str) -> None:
        nonlocal passed
        if condition:
            passed += 1
        else:
            failures.append(message)

    window = FlowWindow(api)
    window.show()
    app.processEvents()

    check(window.catalog is not None and len(window.catalog.specs) > 5, "节点目录应加载")
    check(window.palette_tree.topLevelItemCount() >= 3, "节点库应按层分组")

    for node_type in ("start", "text.contains", "state.step", "wait", "end"):
        window.add_node(node_type)
    app.processEvents()
    check(len(window.graph.all_nodes()) == 5, "添加节点应生效")
    check(len(window.canvas_to_doc()["nodes"]) == 5, "画布应能序列化为 flow.json")

    window.doc_to_canvas(SMOKE_GRAPH)
    app.processEvents()
    check(len(window.graph.all_nodes()) == len(SMOKE_GRAPH["nodes"]), "载图应还原节点")
    exec_edges = [c for c in collect_connections(window.graph) if c.kind == "exec"]
    check(len(exec_edges) == len(SMOKE_GRAPH["exec"]), f"载图应还原 exec 边（{len(exec_edges)}）")

    # 内嵌参数控件（真后端目录）：控件存在、int 保持 int、属性 → 控件同步
    wait_node = window.graph.get_node_by_name("wait")
    check("ms" in wait_node.view.widgets, "wait 节点应内嵌 ms 参数控件")
    check(isinstance(wait_node.view.widgets["ms"].get_value(), int),
          "ms 内嵌控件取值应保持 int")
    wait_node.set_property("ms", 5)
    check(wait_node.view.widgets["ms"].get_value() == 5, "set_property 应回填内嵌控件")

    window.refresh_devices()
    app.processEvents()
    check(window.video_status.text() != "未知", "设备面板应刷新出状态")

    # 运行（真实后端）+ 轮询高亮
    window.run_flow()
    deadline = time.time() + 20
    while window._run_id and window._poll.isActive() and time.time() < deadline:
        app.processEvents()
        time.sleep(0.1)
    app.processEvents()
    check("completed" in window.run_status.text(), f"图应跑完：{window.run_status.text()}")
    check(window.records.rowCount() >= len(SMOKE_GRAPH["nodes"]), "运行状态表应填满节点记录")

    # 单节点试跑（拦掉模态弹窗，避免阻塞无头环境）
    trialed: dict = {}
    window._show_trial_result = lambda node_id, spec, result: trialed.update(node_id=node_id, **result)
    window.trial_run_selected(window.graph.get_node_by_name("step"))
    app.processEvents()
    check(trialed.get("ok") is True, f"单节点试跑应成功：{trialed.get('error')}")
    check(trialed.get("port") == "out", f"state.step 首次试跑应走 out：{trialed.get('port')}")

    print(f"[uitest] 通过 {passed} 项（后端 {args.backend}）")
    if failures:
        print(f"[uitest] 失败 {len(failures)} 项：")
        for failure in failures:
            print(f"  - {failure}")
        return 1
    print("[uitest] OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(run_ui_test())
