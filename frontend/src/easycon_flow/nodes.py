"""目录 → NodeGraphQt 节点类（动态生成）+ 画布 ↔ NodeState 搬运。

节点**全部由后端目录驱动**（`GET /api/nodes`）：前端不硬编码任何节点类型、参数或端口。
后端加一个节点并登记到 FlowNodeCatalog，画布刷新后即出现，无需改前端。

参数控件：每个参数除属性面板（右侧 PropertiesBin）外，还会在**节点体内嵌一个同值控件**，
可直接在画布上编辑。控件与属性的双向同步由 NodeGraphQt 自带机制完成——
控件编辑经 value_changed → set_property 写回模型；任何 set_property（含属性面板编辑）
经 PropertyChangedCmd 自动回填 view.widgets[属性名]。因此内嵌控件的 get_value 必须
与属性值**严格等值往返**（见 IntNodeSpinBox / _PreviewEdit）。
"""

from __future__ import annotations

import re

from NodeGraphQt import BaseNode
from NodeGraphQt.constants import NodePropWidgetEnum
from NodeGraphQt.widgets.node_widgets import (NodeButton, NodeCheckBox,
                                             NodeComboBox, NodeLineEdit,
                                             NodeSpinBox)
from PySide6 import QtWidgets

from .flowdoc import (DATA_KIND, EXEC_KIND, Connection, NodeState, is_reference)

NODE_IDENTIFIER = "easycon"
NODE_ID_PROPERTY = "node_id"
LITERAL_PREFIX = "lit_"
SLOW_PREFIX = "slow_"
PARAM_PREFIX = "p_"
BUTTON_EDIT_PREFIX = "btn_edit_"
BUTTON_PICK_PREFIX = "btn_pick_"

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
    "text": NodePropWidgetEnum.QTEXT_EDIT.value,
    "image": NodePropWidgetEnum.QLINE_EDIT.value,
    "any": NodePropWidgetEnum.QLINE_EDIT.value,
}

_INT_RANGE = (-10_000_000, 10_000_000)
_FLOAT_RANGE = (-1.0e9, 1.0e9)


class IntNodeSpinBox(NodeSpinBox):
    """内嵌整数微调框。NodeGraphQt 自带 NodeSpinBox.get_value 返回字符串，
    这里保持 int，属性 ↔ 控件才能严格等值往返（否则 set_property 会把 int 覆盖成 str）。"""

    def get_value(self):
        return int(self.get_custom_widget().value())

    def set_value(self, value=0):
        if value != self.get_value():
            self.get_custom_widget().setValue(int(value))


class FloatNodeSpinBox(NodeSpinBox):
    """同 IntNodeSpinBox，浮点版。"""

    def get_value(self):
        return float(self.get_custom_widget().value())

    def set_value(self, value=0.0):
        if value != self.get_value():
            self.get_custom_widget().setValue(float(value))


class PreviewLineEdit(NodeLineEdit):
    """text（多行）参数的节点内只读预览：显示首行，值原样保留。

    get_value 必须原样返回完整多行文本——属性面板/对话框修改参数后，NodeGraphQt 的
    PropertyChangedCmd 会比较 widgets[name].get_value() 与新值来决定是否回填控件；
    若这里返回被行编辑器截断的文本，多行内容会被反向写回模型而丢失。
    控件只读、value_changed 不接线：预览永不回写，值以节点属性为唯一事实源。
    """

    def __init__(self, parent=None, name="", label="", text=""):
        super().__init__(parent, name, label, "")
        self._full_text = str(text or "")
        line = self.get_custom_widget()
        line.setReadOnly(True)
        line.setText(self._first_line())
        line.setPlaceholderText("（空 — 点「编辑…」填写）")

    def _first_line(self) -> str:
        return self._full_text.splitlines()[0] if self._full_text else ""

    def get_value(self):
        return self._full_text

    def set_value(self, text=""):
        if str(text) != self._full_text:
            self._full_text = str(text)
            self.get_custom_widget().setText(self._first_line())


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
            prop = param_property(param["name"])
            value_type = param.get("type", "string")
            stored = _create_property(self, prop, value_type, default_value(param),
                                      param.get("description", ""), param.get("options"))
            try:
                _embed_param_widget(self, prop, param["name"], value_type, stored,
                                    param.get("description", ""), param.get("options"))
            except Exception:  # noqa: BLE001 - 内嵌失败只损失节点内编辑，属性面板仍可用
                pass

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
        self.view.draw_node()

    return type(class_name_for(node_type), (BaseNode,), {
        "__identifier__": NODE_IDENTIFIER,
        "NODE_NAME": node_type,
        "__init__": __init__,
    })


def _create_property(node: BaseNode, name: str, value_type: str, value, tooltip: str,
                     options: list[str] | None) -> object:
    """建属性（只进右侧属性面板，不在节点体内嵌控件）；返回实际存入的（归一化）值。
    控件类型不兼容时退化为字符串控件（UI 不能因为目录变化而崩）。"""
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
        value = str(value)
        node.create_property(name, value)
    return value


def _embed_param_widget(node: BaseNode, prop: str, label: str, value_type: str, value,
                        tooltip: str, options: list[str] | None) -> None:
    """在节点体内嵌参数控件（节点上直接编辑参数）。

    控件编辑 → value_changed → set_property 写回模型；属性面板/加载图对参数的任何
    set_property 由 NodeGraphQt（PropertyChangedCmd）自动回填同名列控件——
    两边永远显示同一个值。控件类型按参数类型选择：
    bool 勾选框、int/number/float 微调框、enum 下拉、path 输入框+「…」选文件、
    text 只读首行预览+「编辑…」多行对话框、其余单行输入。
    """
    if value_type == "bool":
        widget = NodeCheckBox(node.view, prop, label, "", bool(value))
    elif value_type in ("int", "number"):
        widget = IntNodeSpinBox(node.view, prop, label, int(value or 0), *_INT_RANGE)
    elif value_type == "float":
        widget = FloatNodeSpinBox(node.view, prop, label, float(value or 0.0),
                                  *_FLOAT_RANGE, True)
    elif value_type == "enum":
        widget = NodeComboBox(node.view, prop, label, list(options or []))
        widget.set_value(str(value))
    elif value_type == "text":
        widget = PreviewLineEdit(node.view, prop, label, str(value or ""))
        _embed_button(node, BUTTON_EDIT_PREFIX + prop, label, "编辑…", tooltip,
                      lambda checked=False, n=node, p=prop, l=label: _edit_multiline(n, p, l))
    elif value_type == "path":
        widget = NodeLineEdit(node.view, prop, label, str(value or ""))
        _embed_button(node, BUTTON_PICK_PREFIX + prop, label, "…", f"{tooltip}（点击选择文件）",
                      lambda checked=False, n=node, p=prop: _pick_file(n, p))
    else:
        widget = NodeLineEdit(node.view, prop, label, str(value or ""))

    widget.setToolTip(tooltip or "")
    widget.value_changed.connect(lambda name, val, n=node: n.set_property(name, val))
    node.view.add_widget(widget)


def _embed_button(node: BaseNode, name: str, label: str, text: str, tooltip: str,
                  on_clicked) -> None:
    """内嵌一个动作按钮（不承载参数值）。注册同名隐藏属性：update_model() 会把
    view.widgets 的值写回模型，未登记的属性名会让它抛异常。"""
    node.create_property(name, "", widget_type=NodePropWidgetEnum.HIDDEN.value)
    widget = NodeButton(node.view, name, label, text)
    widget.setToolTip(tooltip or "")
    widget.get_custom_widget().clicked.connect(on_clicked)
    node.view.add_widget(widget)


def _edit_multiline(node: BaseNode, prop: str, label: str) -> None:
    """「编辑…」按钮：多行文本对话框（text 参数，如 script.run 的内联脚本）。"""
    dialog = _MultiLineDialog(f"编辑 {label}", str(node.get_property(prop) or ""),
                              QtWidgets.QApplication.activeWindow())
    if dialog.exec() == QtWidgets.QDialog.DialogCode.Accepted:
        node.set_property(prop, dialog.text())


def _pick_file(node: BaseNode, prop: str) -> None:
    """「…」按钮：文件选择对话框（path 参数），写回属性并同步内嵌输入框。"""
    current = str(node.get_property(prop) or "")
    path, _ = QtWidgets.QFileDialog.getOpenFileName(
        QtWidgets.QApplication.activeWindow(), "选择文件", current, "所有文件 (*)")
    if path:
        node.set_property(prop, path)


class _MultiLineDialog(QtWidgets.QDialog):
    """多行文本编辑对话框（确定返回 True）。"""

    def __init__(self, title: str, content: str, parent=None):
        super().__init__(parent)
        self.setWindowTitle(title)
        self.resize(560, 420)
        layout = QtWidgets.QVBoxLayout(self)
        self._edit = QtWidgets.QPlainTextEdit()
        self._edit.setPlainText(content)
        layout.addWidget(self._edit)
        buttons = QtWidgets.QDialogButtonBox(
            QtWidgets.QDialogButtonBox.StandardButton.Ok
            | QtWidgets.QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

    def text(self) -> str:
        return self._edit.toPlainText()


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
