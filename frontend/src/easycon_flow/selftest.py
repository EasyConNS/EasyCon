"""离线自检：不依赖后端，验证画布 ↔ flow.json 的双向映射。

    QT_QPA_PLATFORM=offscreen python -m easycon_flow.selftest
    # 或对着真后端跑（会用 GET /api/nodes 的真实目录做同样的往返检查）
    QT_QPA_PLATFORM=offscreen python -m easycon_flow.selftest --backend http://127.0.0.1:19391

覆盖：目录 → 动态节点类、参数/字面量/slow 采集、exec 与数据连线序列化、
flow.json 解析回填、以及「参数名与 NodeGraphQt 预置属性同名」的冲突处理。
"""

from __future__ import annotations

import argparse
import json
import sys

from PySide6 import QtWidgets

from . import flowdoc
from .api import ApiError, FlowApi
from .nodes import (NodeCatalog, add_state, collect_connections, collect_states,
                    input_port, output_port)

FAKE_CATALOG = {
    "version": 1,
    "slow": [
        {"name": "everyFrames", "type": "int", "description": "帧号推进不足 N 帧即复用"},
        {"name": "onChange", "type": "bool", "description": "输入未变即复用"},
        {"name": "intervalMs", "type": "int", "description": "间隔未到即复用"},
    ],
    "keys": ["A", "B", "TOP"],
    "ocrBackends": ["none", "tesseract", "ppocr"],
    "nodes": [
        {"type": "start", "layer": "flow", "summary": "起点",
         "ports": [{"name": "out", "kind": "exec-out", "type": "exec", "description": ""}],
         "params": []},
        {"type": "capture.frame", "layer": "sense", "summary": "抓帧",
         "ports": [
             {"name": "in", "kind": "exec-in", "type": "exec", "description": ""},
             {"name": "out", "kind": "exec-out", "type": "exec", "description": ""},
             {"name": "image", "kind": "data-out", "type": "image", "description": "画面"},
             {"name": "x", "kind": "data-in", "type": "number", "description": "ROI 左"},
         ],
         "params": [
             {"name": "waitForNew", "type": "bool", "description": "等新帧", "default": False},
             {"name": "timeoutMs", "type": "int", "description": "等待上限", "default": 2000},
             {"name": "x", "type": "int", "description": "ROI 左", "default": 0},
         ]},
        {"type": "ocr.text", "layer": "sense", "summary": "OCR",
         "ports": [
             {"name": "in", "kind": "exec-in", "type": "exec", "description": ""},
             {"name": "out", "kind": "exec-out", "type": "exec", "description": ""},
             {"name": "image", "kind": "data-in", "type": "image", "description": "画面"},
             {"name": "text", "kind": "data-out", "type": "text", "description": "文本"},
             {"name": "conf", "kind": "data-out", "type": "number", "description": "置信度"},
         ],
         "params": [{"name": "lang", "type": "string", "description": "语言", "default": "eng"}]},
        {"type": "compare", "layer": "decision", "summary": "比较",
         "ports": [
             {"name": "in", "kind": "exec-in", "type": "exec", "description": ""},
             {"name": "true", "kind": "exec-out", "type": "exec", "description": "成立"},
             {"name": "false", "kind": "exec-out", "type": "exec", "description": "不成立"},
             {"name": "a", "kind": "data-in", "type": "any", "description": "左"},
             {"name": "b", "kind": "data-in", "type": "any", "description": "右"},
         ],
         "params": [{"name": "op", "type": "enum", "description": "运算符", "default": "==",
                     "options": ["==", "!=", ">", "<", ">=", "<="]}]},
        # 参数名 name 与 NodeGraphQt 预置属性冲突 → 必须走 p_ 前缀
        {"type": "state.step", "layer": "decision", "summary": "计数器",
         "ports": [
             {"name": "in", "kind": "exec-in", "type": "exec", "description": ""},
             {"name": "out", "kind": "exec-out", "type": "exec", "description": ""},
             {"name": "done", "kind": "exec-out", "type": "exec", "description": ""},
             {"name": "value", "kind": "data-out", "type": "number", "description": "当前值"},
         ],
         "params": [
             {"name": "name", "type": "string", "description": "计数器名", "default": "step"},
             {"name": "op", "type": "enum", "description": "操作", "default": "inc",
              "options": ["inc", "set", "reset"]},
             {"name": "value", "type": "int", "description": "增量", "default": 1},
         ]},
        {"type": "pad.key", "layer": "actuation", "summary": "按键",
         "ports": [
             {"name": "in", "kind": "exec-in", "type": "exec", "description": ""},
             {"name": "out", "kind": "exec-out", "type": "exec", "description": ""},
         ],
         "params": [{"name": "key", "type": "enum", "description": "键名", "default": "A",
                     "options": ["A", "B", "TOP"]}]},
        {"type": "script.run", "layer": "actuation", "summary": "执行脚本",
         "ports": [
             {"name": "in", "kind": "exec-in", "type": "exec", "description": ""},
             {"name": "out", "kind": "exec-out", "type": "exec", "description": ""},
             {"name": "arg1", "kind": "data-in", "type": "text", "description": "ARG(1)"},
             {"name": "ok", "kind": "data-out", "type": "number", "description": "完成"},
         ],
         "params": [
             {"name": "script", "type": "text", "description": "内联脚本（多行）"},
             {"name": "file", "type": "path", "description": "脚本路径"},
         ]},
    ],
}

SAMPLE_DOC = {
    "version": 1,
    "name": "selftest",
    "maxSteps": 500,
    "timeoutSec": 60,
    "nodes": [
        {"id": "start", "type": "start", "pos": {"x": 0.0, "y": 0.0}},
        {"id": "cap", "type": "capture.frame",
         "pos": {"x": 10.0, "y": 40.0},
         "params": {"waitForNew": True, "timeoutMs": 3000, "x": -1},
         "slow": {"everyFrames": 2}},
        {"id": "ocr", "type": "ocr.text",
         "pos": {"x": 120.0, "y": 40.0},
         "params": {"lang": "eng"},
         "inputs": {"image": {"node": "cap", "port": "image"}, "x": 12}},
        {"id": "chk", "type": "compare",
         "pos": {"x": 240.0, "y": 40.0},
         "params": {"op": ">="},
         "inputs": {"a": {"node": "ocr", "port": "conf"}, "b": 50}},
        {"id": "loop", "type": "state.step",
         "pos": {"x": 360.0, "y": 40.0},
         "params": {"name": "retry", "op": "inc", "value": 1}},
        {"id": "press", "type": "pad.key",
         "pos": {"x": 480.0, "y": 40.0},
         "params": {"key": "A"}},
        {"id": "run", "type": "script.run",
         "pos": {"x": 600.0, "y": 40.0},
         "params": {"script": "$n = ARG(1)\nPRINT \"n=\" & $n", "file": "scripts/hello.ecs"}},
    ],
    "exec": [
        ["start", "cap"],
        ["cap", "ocr"],
        ["ocr", "chk"],
        ["chk.true", "loop"],
        ["chk.false", "press"],
        ["loop.out", "press"],
        ["press", "run"],
        ["run", "cap"],
    ],
}


class Checker:
    def __init__(self) -> None:
        self.passed = 0
        self.failures: list[str] = []

    def check(self, condition: bool, message: str) -> None:
        if condition:
            self.passed += 1
        else:
            self.failures.append(message)

    def equal(self, actual, expected, message: str) -> None:
        self.check(actual == expected, f"{message}\n    期望: {expected!r}\n    实际: {actual!r}")


def run_self_test(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="EasyCon Flow 画布离线自检")
    parser.add_argument("--backend", default=None, help="用真后端的 /api/nodes 目录跑自检")
    args = parser.parse_args(list(argv if argv is not None else sys.argv[1:]))

    payload = FAKE_CATALOG
    if args.backend:
        try:
            payload = FlowApi(args.backend).nodes()
            print(f"[自检] 使用真后端目录：{args.backend}")
        except ApiError as exc:
            print(f"[自检] 后端不可用（{exc}），退回内置目录")

    _ = QtWidgets.QApplication.instance() or QtWidgets.QApplication([])

    checker = Checker()
    _check_flowdoc_pure(checker)
    _check_canvas_roundtrip(checker, payload)
    if args.backend:
        _check_backend_roundtrip(checker, payload, args.backend)

    print(f"[自检] 通过 {checker.passed} 项")
    if checker.failures:
        print(f"[自检] 失败 {len(checker.failures)} 项：")
        for failure in checker.failures:
            print(f"  - {failure}")
        return 1
    print("[自检] OK")
    return 0


def _check_flowdoc_pure(checker: Checker) -> None:
    checker.equal(flowdoc.exec_endpoint("a", "out"), "a", "单出口不写端口后缀")
    checker.equal(flowdoc.exec_endpoint("a", None), "a", "无端口不写后缀")
    checker.equal(flowdoc.exec_endpoint("chk", "true"), "chk.true", "条件出口带端口")
    checker.equal(flowdoc.split_endpoint("chk.true"), ("chk", "true"), "端点拆分")
    checker.equal(flowdoc.split_endpoint("cap"), ("cap", None), "无端口拆分")
    checker.equal(flowdoc.normalize_exec_edge({"from": "chk.false", "to": "x"}),
                  ("chk", "false", "x"), "对象形态 exec 边")

    existing = {"a"}
    checker.equal(flowdoc.make_node_id("capture.frame", existing), "capture_frame", "点号换成下划线")
    checker.equal(flowdoc.make_node_id("a", existing), "a2", "重名递增")

    problems = flowdoc.validate_doc({"nodes": [], "exec": []})
    checker.check(any("没有任何节点" in p for p in problems), "空图应报错")

    dup = {"nodes": [{"id": "a", "type": "wait"}, {"id": "a", "type": "wait"}], "exec": []}
    checker.check(any("重复" in p for p in flowdoc.validate_doc(dup)), "重复 id 应报错")

    dangling = {"nodes": [{"id": "a", "type": "start"}], "exec": [["a", "nope"]]}
    checker.check(any("不存在" in p for p in flowdoc.validate_doc(dangling)), "悬空边应报错")

    ok = {"nodes": [{"id": "s", "type": "start"}, {"id": "e", "type": "end"}], "exec": [["s", "e"]]}
    checker.equal(flowdoc.validate_doc(ok), [], "合法图应无问题")


def _check_canvas_roundtrip(checker: Checker, payload: dict) -> None:
    from NodeGraphQt import NodeGraph

    graph = NodeGraph()
    catalog = NodeCatalog(payload)
    catalog.register_into(graph)
    checker.equal(catalog.register_errors, [], "所有目录节点都应能注册成画布节点类")

    if catalog.register_errors:
        return

    # 1) 载入示例图 → 画布
    name, max_steps, timeout_sec, nodes, connections = flowdoc.parse_doc(SAMPLE_DOC, catalog.specs)
    checker.equal(name, "selftest", "图名解析")
    checker.equal(max_steps, 500, "看门狗步数解析")
    checker.equal(timeout_sec, 60, "看门狗时长解析")
    checker.equal(len(nodes), len(SAMPLE_DOC["nodes"]), "节点数解析")
    checker.equal(len(connections), 10, "exec 8 条 + 数据 2 条")

    by_id = {}
    for state in nodes:
        by_id[state.id] = add_state(graph, catalog, state)

    for conn in connections:
        src = by_id[conn.src_id]
        dst = by_id[conn.dst_id]
        if conn.kind == flowdoc.EXEC_KIND:
            output_port(src, conn.src_port).connect_to(input_port(dst, "in"))
        else:
            output_port(src, conn.src_port).connect_to(input_port(dst, conn.dst_port))

    # 2) 画布 → 采集状态与连线
    states = collect_states(graph)
    checker.equal(len(states), len(SAMPLE_DOC["nodes"]), "节点采集数一致")

    by_flow_id = {s.id: s for s in states}
    checker.equal(by_flow_id["cap"].params.get("waitForNew"), True, "bool 参数往返")
    checker.equal(by_flow_id["cap"].params.get("timeoutMs"), 3000, "int 参数往返")
    cap_state = by_flow_id["cap"]
    # capture.frame.x 在内置目录里是「参数 + 数据入边」，在真后端目录里只是数据入边；
    # 两种形态下值都必须落到画布控件上并往返回来
    checker.equal(cap_state.params.get("x", cap_state.literals.get("x")), -1,
                  "参数 x 与同名数据入边字面量统一")
    checker.equal(by_flow_id["cap"].slow, {"everyFrames": 2}, "slow 往返")
    checker.equal(by_flow_id["ocr"].params.get("lang"), "eng", "字符串参数往返")
    checker.equal(by_flow_id["chk"].params.get("op"), ">=", "enum 参数往返")
    checker.equal(by_flow_id["loop"].params.get("name"), "retry",
                  "参数名与 NodeGraphQt 预置属性同名时仍能往返")
    checker.equal(by_flow_id["loop"].params.get("op"), "inc", "enum 缺省与显式值共存")

    # 内嵌参数控件：节点上直接编辑，值与属性双向一致
    from .nodes import BUTTON_EDIT_PREFIX, BUTTON_PICK_PREFIX
    script_text = "$n = ARG(1)\nPRINT \"n=\" & $n"
    run_node = by_id["run"]
    checker.check("script" in run_node.view.widgets, "text 参数应内嵌预览控件")
    checker.check(BUTTON_EDIT_PREFIX + "script" in run_node.view.widgets,
                  "text 参数应内嵌「编辑…」按钮")
    checker.check(BUTTON_PICK_PREFIX + "file" in run_node.view.widgets,
                  "path 参数应内嵌「…」选文件按钮")
    checker.equal(run_node.view.widgets["script"].get_value(), script_text,
                  "多行预览控件应保留完整文本（不得被行编辑器截断）")
    cap_node = by_id["cap"]
    checker.check(isinstance(cap_node.view.widgets["waitForNew"].get_value(), bool),
                  "bool 内嵌控件取值应为 bool")
    checker.equal(cap_node.view.widgets["timeoutMs"].get_value(), 3000, "int 内嵌控件与属性同步")
    checker.check(isinstance(cap_node.view.widgets["timeoutMs"].get_value(), int),
                  "int 内嵌控件取值应保持 int（不得退化成字符串）")
    cap_node.set_property("timeoutMs", 1500)
    checker.equal(cap_node.view.widgets["timeoutMs"].get_value(), 1500,
                  "set_property 应回填内嵌控件（属性面板编辑路径）")

    canvas_connections = collect_connections(graph)
    exec_edges = sorted([c for c in canvas_connections if c.kind == flowdoc.EXEC_KIND],
                        key=lambda c: (c.src_id, c.src_port, c.dst_id))
    data_edges = sorted([c for c in canvas_connections if c.kind == flowdoc.DATA_KIND],
                        key=lambda c: (c.src_id, c.src_port, c.dst_id))
    checker.equal(len(exec_edges), 8, "exec 连线数")
    checker.equal(len(data_edges), 2, "数据连线数")
    checker.check(any(c.src_id == "chk" and c.src_port == "true" and c.dst_id == "loop" for c in exec_edges),
                  "条件出口 chk.true 连线被识别为 exec")

    # 3) 画布 → 文档 → 与原始文档等价
    document = flowdoc.build_doc(name, states, canvas_connections, max_steps, timeout_sec, catalog.specs)
    by_doc_id = {n["id"]: n for n in document["nodes"]}
    checker.equal(by_doc_id["ocr"]["inputs"]["image"], {"node": "cap", "port": "image"}, "数据入边写回引用")
    checker.equal(by_doc_id["chk"]["inputs"]["b"], 50, "数据入边写回字面量")
    checker.equal(_normalize_exec(document["exec"]), _normalize_exec(SAMPLE_DOC["exec"]),
                  "exec 边集合一致（无条件出口写裸 id 与显式 .out 等价）")
    checker.equal(len(document["nodes"]), len(SAMPLE_DOC["nodes"]), "节点数一致")

    # 4) 文档 → 画布 → 文档：二次往返必须稳定
    graph2 = NodeGraph()
    catalog.register_into(graph2)
    _, _, _, nodes2, conn2 = flowdoc.parse_doc(document, catalog.specs)
    for state in nodes2:
        add_state(graph2, catalog, state)
    for conn in conn2:
        src = graph2.get_node_by_name(conn.src_id)
        dst = graph2.get_node_by_name(conn.dst_id)
        if conn.kind == flowdoc.EXEC_KIND:
            output_port(src, conn.src_port).connect_to(input_port(dst, "in"))
        else:
            output_port(src, conn.src_port).connect_to(input_port(dst, conn.dst_port))

    document2 = flowdoc.build_doc(name, collect_states(graph2), collect_connections(graph2),
                                  max_steps, timeout_sec, catalog.specs)
    checker.equal(_normalized(document2), _normalized(document), "二次往返稳定（文档等价）")


SMOKE_DOC = {
    "name": "selftest-smoke",
    "maxSteps": 50,
    "timeoutSec": 30,
    "nodes": [
        {"id": "start", "type": "start"},
        {"id": "chk", "type": "text.contains",
         "params": {"mode": "contains", "pattern": "TAUROS"},
         "inputs": {"text": "Lv25 TAUROS No.128"}},
        {"id": "step", "type": "state.step",
         "params": {"name": "n", "op": "inc", "value": 1, "max": 2}},
        {"id": "wait", "type": "wait", "params": {"ms": 1}},
        {"id": "end", "type": "end"},
    ],
    "exec": [["start", "chk"], ["chk.true", "step"], ["chk.false", "end"],
             ["step.out", "wait"], ["wait", "step"], ["step.done", "end"]],
}


def _check_backend_roundtrip(checker: Checker, payload: dict, backend: str) -> None:
    """对着真后端：把前端序列化出来的图提交执行，并试跑一个节点（端到端契约）。"""
    import time

    from NodeGraphQt import NodeGraph

    from .api import ApiError, FlowApi

    api = FlowApi(backend)
    try:
        api.health()
    except ApiError as exc:
        checker.check(False, f"后端不可用：{exc}")
        return

    # 画布 → 文档（与界面「运行图」完全同一条代码路径）
    graph = NodeGraph()
    catalog = NodeCatalog(payload)
    catalog.register_into(graph)
    _, max_steps, timeout_sec, nodes, connections = flowdoc.parse_doc(SMOKE_DOC, catalog.specs)
    by_id = {state.id: add_state(graph, catalog, state) for state in nodes}
    for conn in connections:
        src, dst = by_id[conn.src_id], by_id[conn.dst_id]
        if conn.kind == flowdoc.EXEC_KIND:
            output_port(src, conn.src_port).connect_to(input_port(dst, "in"))
        else:
            output_port(src, conn.src_port).connect_to(input_port(dst, conn.dst_port))

    document = flowdoc.build_doc("selftest-smoke", collect_states(graph), collect_connections(graph),
                                 max_steps, timeout_sec, catalog.specs)
    checker.equal(flowdoc.validate_doc(document), [], "前端校验应通过")

    try:
        run_id = api.run_flow(document)
    except ApiError as exc:
        checker.check(False, f"提交运行失败：{exc}")
        return
    checker.check(bool(run_id), "后端返回 runId")

    run = None
    for _ in range(60):
        time.sleep(0.2)
        runs = (api.status(run_id) or {}).get("runs") or []
        if not runs:
            break
        run = runs[0]
        if run.get("status") != "running":
            break

    checker.check(run is not None and run.get("status") == "completed",
                  f"前端提交的图应在后端跑完：{run and run.get('status')} {run and run.get('error')}")
    if run:
        steps = {r["id"]: r["count"] for r in run.get("records") or []}
        checker.equal(steps.get("step"), 2, "state.step 边界（max=2 → 第 2 次走 done）")

    # 单节点试跑：数据入边引用的上游要被一并求值
    try:
        trial = api.run_node_in_graph(document, "step")
        checker.equal(trial.get("port"), "out", "试跑 state.step 的出口端口")
    except ApiError as exc:
        checker.check(False, f"单节点试跑失败：{exc}")


def _normalize_exec(edges: list) -> list:
    """把 ["a.out","b"] 归一成 ["a","b"]：后端对无条件出口只看裸 id（FromPort == null 兜底）。"""
    normalized = []
    for edge in edges:
        src, dst = (edge[0], edge[1]) if isinstance(edge, (list, tuple)) else (edge["from"], edge["to"])
        if isinstance(src, str) and src.endswith(".out"):
            src = src[:-4]
        normalized.append([src, dst])
    return sorted(normalized)


def _normalized(document: dict) -> dict:
    """去掉节点顺序差异后比较（画布遍历顺序不保证稳定）。"""
    clone = json.loads(json.dumps(document))
    clone["nodes"] = sorted(clone["nodes"], key=lambda n: n["id"])
    clone["exec"] = sorted(clone["exec"])
    return clone


if __name__ == "__main__":
    raise SystemExit(run_self_test())
