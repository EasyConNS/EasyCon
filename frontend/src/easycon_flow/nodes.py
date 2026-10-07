"""目录 → NodeGraphQt 节点类（动态生成）+ 画布 ↔ NodeState 搬运。

节点**全部由后端目录驱动**（`GET /api/nodes`）：前端不硬编码任何节点类型、参数或端口。
后端加一个节点并登记到 FlowNodeCatalog，画布刷新后即出现，无需改前端。
"""

from __future__ import annotations

import re

from NodeGraphQt import BaseNode
from NodeGraphQt.constants import NodePropWidgetEnum

from .flowdoc import (DATA_KIND, EXEC_KIND, Connection, NodeState, is_reference)

NODE_IDENTIFIER = "easycon"
NODE_ID_PROPERTY = "node_id"
LITERAL_PREFIX = "lit_"
SLOW_PREFIX = "slow_"
PARAM_PREFIX = "p_"

# NodeGraphQt 预置属性名（model.py: "reserved for default property"）。
# 目录里的参数若与之同名（如 state.step 的 name），必须加 p_ 前缀，读取时再剥掉。
RESERVED_PROPERTY_NAMES = {
    "type_", "icon", "name", "color", "border_color", "text_color", "disabled",
    "selected", "visible", "width", "height", "pos", "layout_direction",
    "port_deletion_allowed", "subgraph_session", "id",
    "_TEMP_accept_connection_types", "_TEMP_reject_connection_types",
}


def param_property(param_name: str) -> str:
    """参数名 → 节点属性名（避开 NodeGraphQt 预置属性）。"""
    return PARAM_PREFIX + param_name if param_name in RESERVED_PROPERTY_NAMES else param_name


def param_name(property_name: str) -> str:
    """节点属性名 → 参数名。"""
    return property_name[len(PARAM_PREFIX):] if property_name.startswith(PARAM_PREFIX) else property_name

# 层配色（节点底色）
LAYER_COLORS = {
    "flow": (86, 92, 104),
    "sense": (52, 88, 132),
    "decision": (140, 96, 40),
    "actuation": (124, 56, 56),
}

_WIDGET_BY_TYPE = {
    "bool": NodePropWidgetEnum.QCHECK_BOX.value,
    "int": NodePropWidgetEnum.QSPIN_BOX.value,
    "number": NodePropWidgetEnum.QSPIN_BOX.value,
    "float": NodePropWidgetEnum.FLOAT.value,
    "enum": NodePropWidgetEnum.QCOMBO_BOX.value,
    "path": NodePropWidgetEnum.FILE_OPEN.value,
    "string": NodePropWidgetEnum.QLINE_EDIT.value,
    "text": NodePropWidgetEnum.QLINE_EDIT.value,
    "image": NodePropWidgetEnum.QLINE_EDIT.value,
    "any": NodePropWidgetEnum.QLINE_EDIT.value,
}

_INT_RANGE = (-10_000_000, 10_000_000)


def class_name_for(node_type: str) -> str:
    """节点类型 → Python 类名（点号等非法字符换成下划线）。"""
    return "easycon_" + (re.sub(r"[^0-9A-Za-z_]+", "_", node_type).strip("_") or "node")


def type_key(node_type: str) -> str:
    """NodeGraphQt 的节点类型键（<identifier>.<ClassName>）。"""
    return f"{NODE_IDENTIFIER}.{class_name_for(node_type)}"


def default_value(param: dict):
    if "default" in param and param["default"] is not None:
        return param["default"]
    ptype = param.get("type")
    if ptype == "bool":
        return False
    if ptype in ("int", "number"):
        return 0
    if ptype in ("enum",) and param.get("options"):
        return param["options"][0]
    return ""


def make_node_class(spec: dict, slow_fields: list[dict]) -> type:
    """按目录项生成一个 NodeGraphQt 节点类。

    命名约定（序列化时按前缀还原）：
      - 节点参数      → 属性名 = 参数名
      - slow 策略字段 → 属性名 = slow_<字段>
      - 未连线的数据入边字面量 → 属性名 = lit_<端口>
    """
    node_type = spec["type"]
    ports = spec.get("ports") or []
    params = spec.get("params") or []

    def __init__(self):
        BaseNode.__init__(self)
        self._spec = spec
        self._slow_field_names = [f["name"] for f in slow_fields]

        for port in ports:
            kind = port.get("kind")
            name = port.get("name") or "out"
            if kind == "exec-in":
                # exec 入口必须允许多条入边：循环回跳 + 分支汇合都会指向同一个 in
                self.add_input("in", multi_input=True)
            elif kind == "exec-out":
                self.add_output(name)
            elif kind == "data-in":
                self.add_input(name, multi_input=False, display_name=True)
            elif kind == "data-out":
                self.add_output(name, multi_output=True, display_name=True)

        self.create_property(NODE_ID_PROPERTY, "", widget_type=NodePropWidgetEnum.HIDDEN.value)

        for param in params:
            _create_property(self, param_property(param["name"]), param.get("type", "string"),
                             default_value(param), param.get("description", ""),
                             param.get("options"))

        # 未连线的数据入边字面量（与参数同名的端口不重复建控件——参数本身就是那个值）
        param_names = {p["name"] for p in params}
        for port in ports:
            if port.get("kind") != "data-in" or port["name"] in param_names:
                continue
            _create_property(self, LITERAL_PREFIX + port["name"], port.get("type", "any"),
                             "", port.get("description", ""), None)

        for field in slow_fields:
            _create_property(self, SLOW_PREFIX + field["name"], field.get("type", "string"),
                             default_value(field), f"slow: {field.get('description', '')}",
                             field.get("options"))

        self.set_color(*LAYER_COLORS.get(spec.get("layer", "flow"), LAYER_COLORS["flow"]))

    return type(class_name_for(node_type), (BaseNode,), {
        "__identifier__": NODE_IDENTIFIER,
        "NODE_NAME": node_type,
        "__init__": __init__,
    })


def _create_property(node: BaseNode, name: str, value_type: str, value, tooltip: str,
                     options: list[str] | None) -> None:
    """建属性；控件类型不兼容时退化为字符串控件（UI 不能因为目录变化而崩）。"""
    widget = _WIDGET_BY_TYPE.get(value_type, NodePropWidgetEnum.QLINE_EDIT.value)
    kwargs: dict = {"widget_type": widget, "widget_tooltip": tooltip or None}
    if value_type == "enum" and options:
        kwargs["items"] = list(options)
        if value not in options:
            value = options[0]
    elif value_type in ("int", "number"):
        kwargs["range"] = list(_INT_RANGE)
        value = int(value or 0)
    elif value_type == "bool":
        value = bool(value)
    else:
        value = "" if value is None else str(value)

    try:
        node.create_property(name, value, **kwargs)
    except Exception:  # noqa: BLE001 - 目录里出现未知控件类型时保底
        node.create_property(name, str(value))


class NodeCatalog:
    """后端目录的本地视图：按类型查规格，并把动态节点类注册到画布实例。

    NodeGraphQt 的节点注册挂在 graph 实例上，因此换画布（新建/打开/重载目录）时
    必须对目标 graph 重新 `register_into`。
    """

    def __init__(self, payload: dict):
        self.specs: dict[str, dict] = {}
        self.types: dict[str, str] = {}          # flow 类型 → NodeGraphQt 类型键
        self.slow_fields: list[dict] = list(payload.get("slow") or [])
        self.keys: list[str] = list(payload.get("keys") or [])
        self.ocr_backends: list[str] = list(payload.get("ocrBackends") or [])
        self.version = payload.get("version", 1)
        self.register_errors: list[str] = []

        for spec in payload.get("nodes") or []:
            self.specs[spec["type"]] = spec
            self.types[spec["type"]] = type_key(spec["type"])

    def register_into(self, graph) -> None:
        """把目录节点类注册到指定画布（重复注册会被 NodeGraphQt 拒绝，故逐个容错）。"""
        self.register_errors = []
        for node_type, spec in self.specs.items():
            try:
                graph.register_node(make_node_class(spec, self.slow_fields))
            except Exception as exc:  # noqa: BLE001 - 单个节点注册失败不该拖垮整个画布
                self.register_errors.append(f"{node_type}: {exc}")

    def spec(self, node_type: str) -> dict:
        return self.specs.get(node_type) or {"type": node_type, "layer": "flow", "ports": [], "params": []}

    def layers(self) -> dict[str, list[dict]]:
        grouped: dict[str, list[dict]] = {}
        for spec in self.specs.values():
            grouped.setdefault(spec.get("layer", "flow"), []).append(spec)
        return grouped


# ---------------------------------------------------------------- 端口查找

def output_port(node: BaseNode, name: str):
    """按名取输出端口（NodeGraphQt 的 output_ports() 是列表，不是字典）。"""
    return next((p for p in node.output_ports() if p.name() == name), None)


def input_port(node: BaseNode, name: str):
    """按名取输入端口。"""
    return next((p for p in node.input_ports() if p.name() == name), None)


def port_name(port) -> str:
    return port.name() if port is not None else ""


# ---------------------------------------------------------------- 画布 → 状态

def node_id(node: BaseNode) -> str:
    value = node.get_property(NODE_ID_PROPERTY)
    return str(value) if value else node.name()


def node_type_of(node: BaseNode) -> str:
    spec = getattr(node, "_spec", None)
    if spec:
        return spec["type"]
    type_ = node.type_ or ""
    return type_.split(".", 1)[1] if "." in type_ else type_


def collect_states(graph) -> list[NodeState]:
    """画布 → NodeState 列表（参数 / 字面量 / slow）。

    按目录规格逐项读取，而不是遍历节点属性表——后者会把 NodeGraphQt 的预置属性
    （name/color/pos/...）误当成节点参数，且无法区分同名冲突项。
    """
    states: list[NodeState] = []
    for node in graph.all_nodes():
        spec = getattr(node, "_spec", None)
        if spec is None:
            continue   # 非本目录节点（如 BackdropNode）不参与 flow.json

        params: dict = {}
        for param in spec.get("params") or []:
            prop = param_property(param["name"])
            if node.has_property(prop):
                value = node.get_property(prop)
                if _is_meaningful(value):
                    params[param["name"]] = value

        param_names = {p["name"] for p in (spec.get("params") or [])}
        literals: dict = {}
        for port in spec.get("ports") or []:
            if port.get("kind") != "data-in" or port["name"] in param_names:
                continue       # 与参数同名的入边由参数控件承载
            prop = LITERAL_PREFIX + port["name"]
            if node.has_property(prop):
                value = node.get_property(prop)
                if _is_meaningful(value):
                    literals[port["name"]] = value

        slow: dict = {}
        for field in getattr(node, "_slow_field_names", []):
            prop = SLOW_PREFIX + field
            if node.has_property(prop):
                value = node.get_property(prop)
                # slow 的 0/False 语义就是「该条件不启用」→ 不写进 flow.json
                if value:
                    slow[field] = value

        pos = node.pos()
        states.append(NodeState(
            id=node_id(node),
            type=spec["type"],
            pos=(float(pos[0]), float(pos[1])) if pos else None,
            params=params,
            literals=literals,
            slow=slow,
        ))
    return states


def collect_connections(graph) -> list[Connection]:
    """画布 → 连线列表（按源端口的种类区分 exec / data）。"""
    result: list[Connection] = []
    for node in graph.all_nodes():
        spec = getattr(node, "_spec", None)
        if spec is None:
            continue
        exec_outs = {p["name"] for p in (spec.get("ports") or []) if p.get("kind") == "exec-out"}
        for port in node.output_ports():
            port_name = port.name()
            for other in port.connected_ports():
                target = other.node()
                if getattr(target, "_spec", None) is None:
                    continue
                result.append(Connection(
                    src_id=node_id(node),
                    src_port=port_name,
                    dst_id=node_id(target),
                    dst_port=other.name() if port_name not in exec_outs else "in",
                    kind=EXEC_KIND if port_name in exec_outs else DATA_KIND,
                ))
    return result


def _is_meaningful(value) -> bool:
    """空字符串 / None 视为「未设置」，不写进 flow.json（保持文件干净）。"""
    if value is None:
        return False
    if isinstance(value, str):
        return value.strip() != ""
    return True


# ---------------------------------------------------------------- 状态 → 画布

def add_state(graph, catalog: NodeCatalog, state: NodeState) -> BaseNode:
    """按 NodeState 在画布上建节点并回填属性。"""
    type_ = catalog.types.get(state.type)
    if type_ is None:
        raise KeyError(f"目录里没有节点类型: {state.type}")

    node = graph.create_node(type_, name=state.id,
                             pos=tuple(state.pos) if state.pos else None)
    node.set_property(NODE_ID_PROPERTY, state.id)

    spec = catalog.spec(state.type)
    param_names = {p["name"] for p in (spec.get("params") or [])}
    for name, value in (state.params or {}).items():
        prop = param_property(name)
        if node.has_property(prop):
            node.set_property(prop, value)
        elif node.has_property(LITERAL_PREFIX + name):
            # 旧图/手写图可能把「只是数据入边」的端口写在 params 里（如 capture.frame.x）：
            # 后端只读 inputs，这里把它落到该端口的字面量控件上，值不至于丢失
            node.set_property(LITERAL_PREFIX + name, value)

    for port, value in (state.literals or {}).items():
        prop = LITERAL_PREFIX + port
        if node.has_property(prop):
            node.set_property(prop, value)
        elif node.has_property(param_property(port)):      # 与参数同名的入边字面量
            node.set_property(param_property(port), value)

    for field, value in (state.slow or {}).items():
        prop = SLOW_PREFIX + field
        if node.has_property(prop):
            node.set_property(prop, value)

    return node


def literal_inputs(node: BaseNode) -> dict:
    """节点的字面量数据入边（供 validate/试跑使用）。"""
    result: dict = {}
    for prop in node.properties().values():
        name = prop.name
        if name.startswith(LITERAL_PREFIX):
            value = node.get_property(name)
            if _is_meaningful(value):
                result[name[len(LITERAL_PREFIX):]] = value
    return result


def reference_ports(node: BaseNode) -> set[str]:
    """已被连线占用的数据入边端口名。"""
    used: set[str] = set()
    exec_ins = {p.get("name") for p in (getattr(node, "_spec", {}) or {}).get("ports", [])
                if p.get("kind") == "exec-in"}
    for port in node.input_ports():
        if port.name() in exec_ins:
            continue
        if port.connected_ports():
            used.add(port.name())
    return used


__all__ = [
    "DATA_KIND", "EXEC_KIND", "LAYER_COLORS", "NODE_ID_PROPERTY", "NodeCatalog",
    "add_state", "collect_connections", "collect_states", "is_reference",
    "literal_inputs", "node_id", "node_type_of", "reference_ports", "type_key",
]
