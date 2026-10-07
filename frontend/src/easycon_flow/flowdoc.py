"""flow.json 文档模型与画布映射（纯 Python，无 Qt 依赖，可独立测试）。

后端契约（`docs/Flow.md` 与 `GET /api/nodes` 是唯一事实源）：

    {
      "version": 1,
      "name": "battle",
      "maxSteps": 3000,          # 看门狗：节点执行步数
      "timeoutSec": 1800,        # 看门狗：总时长
      "nodes": [ { "id": "cap", "type": "capture.frame",
                   "pos": {"x": 10, "y": 20},        # 仅前端布局，执行忽略
                   "params": {...}, "slow": {...},
                   "inputs": { "image": {"node": "cap", "port": "image"},
                               "b": 3 } } ],          # 引用 或 字面量
      "exec": [ ["cap", "ocr"], ["chk.true", "hit"] ]  # "node" 或 "node.outPort"
    }

本模块只做两件事：把 flow.json 解析成画布状态、把画布状态写回 flow.json。
Qt 侧（nodes.py / window.py）负责 NodeGraphQt ↔ 画布状态的搬运。
"""

from __future__ import annotations

import copy
import re
from dataclasses import dataclass, field

DEFAULT_MAX_STEPS = 3000
DEFAULT_TIMEOUT_SEC = 1800
FORMAT_VERSION = 1

EXEC_KIND = "exec"
DATA_KIND = "data"


class FlowDocError(ValueError):
    """flow.json 结构错误（前端侧预校验，后端还会再校验一次）。"""


@dataclass
class NodeState:
    """画布上的一个节点（与 flow.json 的 node 对象一一对应）。"""

    id: str
    type: str
    pos: tuple[float, float] | None = None
    params: dict = field(default_factory=dict)
    literals: dict = field(default_factory=dict)
    slow: dict = field(default_factory=dict)

    def clone(self) -> NodeState:
        return NodeState(self.id, self.type, self.pos, copy.deepcopy(self.params),
                         copy.deepcopy(self.literals), copy.deepcopy(self.slow))


@dataclass
class Connection:
    """画布上的一条连线：exec 控制流或数据流。"""

    src_id: str
    src_port: str
    dst_id: str
    dst_port: str
    kind: str = EXEC_KIND


# ---------------------------------------------------------------- 端点表示

def exec_endpoint(node_id: str, port: str | None) -> str:
    """exec 边的 from 表示：单出口写 "id"，条件出口写 "id.true"。"""
    if port in (None, "", "out"):
        return node_id
    return f"{node_id}.{port}"


def split_endpoint(text: str) -> tuple[str, str | None]:
    """把 "id.true" 拆成 ("id", "true")；"id" → ("id", None)。

    节点 id 里可能含点号（用户自定义），因此从**第一个**点号切分：
    节点的 id 约定不含点号（前端生成 id 时也会保证），这里按后端 FromNode 的同一规则处理。
    """
    if "." in text:
        node, port = text.split(".", 1)
        return node, (port or None)
    return text, None


def normalize_exec_edge(edge) -> tuple[str, str | None, str]:
    """兼容后端的两种 exec 边形态，返回 (from_node, from_port, to_node)。"""
    if isinstance(edge, (list, tuple)):
        if not edge:
            raise FlowDocError("exec 边不能为空数组")
        src = str(edge[0])
        dst = str(edge[1]) if len(edge) > 1 else ""
    elif isinstance(edge, dict):
        src = str(edge.get("from", ""))
        dst = str(edge.get("to", ""))
    else:
        raise FlowDocError(f"无法识别的 exec 边: {edge!r}")
    node, port = split_endpoint(src)
    return node, port, dst


def make_exec_edge(node_id: str, port: str | None, dst_id: str) -> list[str]:
    endpoint = exec_endpoint(node_id, port)
    return [endpoint, dst_id]


# ---------------------------------------------------------------- 解析

def parse_doc(doc: dict, catalog: dict[str, dict] | None = None) -> tuple[str, int, int, list[NodeState], list[Connection]]:
    """flow.json → (name, maxSteps, timeoutSec, nodes, connections)。

    catalog（type → 目录项）用于把与参数同名的数据入边字面量并回参数，避免画布上出现
    「参数 x」与「数据输入字面量 x」两个重复控件。
    """
    if not isinstance(doc, dict):
        raise FlowDocError("图文档必须是 JSON 对象")

    name = str(doc.get("name") or "untitled")
    max_steps = _as_int(doc.get("maxSteps"), DEFAULT_MAX_STEPS)
    timeout_sec = _as_int(doc.get("timeoutSec"), DEFAULT_TIMEOUT_SEC)

    raw_nodes = doc.get("nodes") or []
    if not isinstance(raw_nodes, list):
        raise FlowDocError("nodes 必须是数组")

    nodes: list[NodeState] = []
    seen: set[str] = set()
    data_connections: list[Connection] = []
    for raw in raw_nodes:
        if not isinstance(raw, dict):
            raise FlowDocError("节点必须是对象")
        node_id = str(raw.get("id") or "")
        node_type = str(raw.get("type") or "")
        if not node_id:
            raise FlowDocError("节点缺少 id")
        if not node_type:
            raise FlowDocError(f"节点 {node_id} 缺少 type")
        if node_id in seen:
            raise FlowDocError(f"节点 id 重复: {node_id}")
        seen.add(node_id)

        pos_raw = raw.get("pos") or {}
        pos = None
        if isinstance(pos_raw, dict) and ("x" in pos_raw or "y" in pos_raw):
            pos = (float(pos_raw.get("x") or 0.0), float(pos_raw.get("y") or 0.0))

        params = copy.deepcopy(raw.get("params") or {})
        slow = copy.deepcopy(raw.get("slow") or {})

        literals: dict = {}
        inputs = raw.get("inputs") or {}
        if isinstance(inputs, dict):
            for port, value in inputs.items():
                if is_reference(value):
                    data_connections.append(Connection(
                        src_id=str(value.get("node") or ""),
                        src_port=str(value.get("port") or ""),
                        dst_id=node_id,
                        dst_port=str(port),
                        kind=DATA_KIND,
                    ))
                else:
                    literals[str(port)] = value

        # 与参数同名的数据入边字面量并回参数（后端也是「入边优先于参数」）
        spec = (catalog or {}).get(node_type)
        if spec:
            param_names = {p["name"] for p in (spec.get("params") or [])}
            for port in list(literals):
                if port in param_names:
                    params[port] = literals.pop(port)

        nodes.append(NodeState(id=node_id, type=node_type, pos=pos, params=params,
                               literals=literals, slow=slow))

    exec_edges = doc.get("exec") or []
    if not isinstance(exec_edges, list):
        raise FlowDocError("exec 必须是数组")
    for edge in exec_edges:
        src_node, src_port, dst_node = normalize_exec_edge(edge)
        if not dst_node:
            raise FlowDocError(f"exec 边缺少目标节点: {edge!r}")
        data_connections.append(Connection(src_id=src_node, src_port=src_port or "out",
                                           dst_id=dst_node, dst_port="in", kind=EXEC_KIND))

    return name, max_steps, timeout_sec, nodes, data_connections


def is_reference(value) -> bool:
    """判断数据入边值是否为 {"node","port"} 引用。"""
    return isinstance(value, dict) and "node" in value


# ---------------------------------------------------------------- 序列化

def build_doc(name: str, nodes: list[NodeState], connections: list[Connection],
              max_steps: int = DEFAULT_MAX_STEPS, timeout_sec: int = DEFAULT_TIMEOUT_SEC,
              catalog: dict[str, dict] | None = None) -> dict:
    """画布状态 → flow.json。同一端口既有字面量又有连线时以连线为准。"""
    doc: dict = {
        "version": FORMAT_VERSION,
        "name": name or "untitled",
        "maxSteps": int(max_steps),
        "timeoutSec": int(timeout_sec),
        "nodes": [],
        "exec": [],
    }

    node_objects: dict[str, dict] = {}
    for state in nodes:
        node: dict = {"id": state.id, "type": state.type}
        if state.pos is not None:
            node["pos"] = {"x": round(float(state.pos[0]), 1), "y": round(float(state.pos[1]), 1)}

        params = copy.deepcopy(state.params)
        spec = (catalog or {}).get(state.type)
        param_names = {p["name"] for p in (spec.get("params") or [])} if spec else set()

        inputs: dict = {}
        for port, value in (state.literals or {}).items():
            if value is None or value == "":
                continue
            if port in param_names and port not in params:
                params[port] = value
            elif port not in param_names:
                inputs[port] = value

        for conn in connections:
            if conn.dst_id == state.id and conn.kind == DATA_KIND:
                inputs[conn.dst_port] = {"node": conn.src_id, "port": conn.src_port}

        if params:
            node["params"] = params
        if inputs:
            node["inputs"] = inputs
        if state.slow:
            node["slow"] = copy.deepcopy(state.slow)

        doc["nodes"].append(node)
        node_objects[state.id] = node

    for conn in connections:
        if conn.kind != EXEC_KIND:
            continue
        doc["exec"].append(make_exec_edge(conn.src_id, conn.src_port, conn.dst_id))

    return doc


# ---------------------------------------------------------------- 预校验

def validate_doc(doc: dict) -> list[str]:
    """前端侧结构校验（与后端 FlowGraph.Validate 对齐）：返回可读问题列表，空 = 通过。"""
    problems: list[str] = []
    nodes = doc.get("nodes") or []
    ids = [str(n.get("id") or "") for n in nodes]

    duplicates = {i for i in ids if ids.count(i) > 1}
    for dup in sorted(duplicates):
        problems.append(f"节点 id 重复: {dup}")
    if not nodes:
        problems.append("图中没有任何节点")

    known = set(ids)
    has_start = any(n.get("type") == "start" for n in nodes)
    for edge in doc.get("exec") or []:
        try:
            src_node, _src_port, dst_node = normalize_exec_edge(edge)
        except FlowDocError as exc:
            problems.append(str(exc))
            continue
        if src_node != "start" and src_node not in known:
            problems.append(f"exec 边引用了不存在的节点: {src_node}")
        if dst_node not in known:
            problems.append(f"exec 边引用了不存在的节点: {dst_node}")

    if not has_start:
        # 与后端 FindStart 一致：无 start 时靠「唯一无 exec 入边节点」兜底
        targets = set()
        for edge in doc.get("exec") or []:
            try:
                _src, _port, dst = normalize_exec_edge(edge)
            except FlowDocError:
                continue
            targets.add(dst)
        roots = [i for i in ids if i not in targets]
        if len(roots) != 1:
            problems.append("未定义 start 节点，且无 exec 入边的节点不唯一（后端会拒绝执行）")

    return problems


# ---------------------------------------------------------------- 画布辅助

_ID_SAFE = re.compile(r"[^0-9A-Za-z_]+")


def make_node_id(node_type: str, existing: set[str]) -> str:
    """按类型生成唯一节点 id（点号换成下划线，点号会破坏 exec 端点解析）。"""
    base = _ID_SAFE.sub("_", node_type).strip("_") or "node"
    if base not in existing:
        return base
    index = 2
    while f"{base}{index}" in existing:
        index += 1
    return f"{base}{index}"


def unique_name(name: str, existing: set[str]) -> str:
    if name not in existing:
        return name
    index = 2
    while f"{name}_{index}" in existing:
        index += 1
    return f"{name}_{index}"


def _as_int(value, fallback: int) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return fallback
