"""EasyCon Flow 画布主窗口（PySide6 + NodeGraphQt）。

职责边界：画布只做「布局 + 参数编辑 + 触发」，节点语义、执行、设备状态全在后端
（HTTP Flow 服务）。前端不含任何节点实现，节点类型与参数由 /api/nodes 决定。
"""

from __future__ import annotations

import base64
import json
import os

from NodeGraphQt import NodeGraph, PropertiesBinWidget
from PySide6 import QtCore, QtGui, QtWidgets

from . import flowdoc
from .api import ApiError, FlowApi
from .nodes import (LAYER_COLORS, NodeCatalog, add_state, collect_connections,
                    collect_states, input_port, output_port)

LAYER_LABELS = {
    "sense": "感知",
    "decision": "决策",
    "actuation": "动作（驱动设备）",
    "flow": "控制流",
}
LAYER_ORDER = ["flow", "sense", "decision", "actuation"]

COLOR_EXECUTED = (46, 122, 74)
COLOR_ERROR = (170, 52, 52)
COLOR_RUNNING = (168, 140, 40)

POLL_INTERVAL_MS = 300


class FlowWindow(QtWidgets.QMainWindow):
    """编排画布 + 设备面板 + 运行面板。"""

    def __init__(self, api: FlowApi):
        super().__init__()
        self.api = api
        self.catalog: NodeCatalog | None = None
        self._path: str | None = None
        self._run_id: str | None = None
        self._dirty = False

        self.setWindowTitle("EasyCon Flow 画布")
        self.resize(1500, 950)

        self._build_graph_host()
        self._build_toolbar()
        self._build_palette()
        self._build_inspector()
        self._build_run_panel()

        self._poll = QtCore.QTimer(self)
        self._poll.setInterval(POLL_INTERVAL_MS)
        self._poll.timeout.connect(self._poll_status)

        self.reload_catalog()
        self.refresh_devices()

    # ------------------------------------------------------------ 画布宿主

    def _build_graph_host(self) -> None:
        self._graph_host = QtWidgets.QWidget()
        self._graph_layout = QtWidgets.QVBoxLayout(self._graph_host)
        self._graph_layout.setContentsMargins(0, 0, 0, 0)
        self.setCentralWidget(self._graph_host)
        self.graph = self._new_graph()

    def _new_graph(self) -> NodeGraph:
        graph = NodeGraph()
        graph.set_background_color(38, 41, 48)
        graph.set_grid_color(52, 56, 64)
        graph.node_double_clicked.connect(self._on_node_double_clicked)
        return graph

    def _install_graph(self, graph: NodeGraph) -> None:
        # NodeGraphQt 的 graph.widget 是属性（NodeGraphWidget 实例），不是方法
        old = self.graph
        self._graph_layout.removeWidget(old.widget)
        old.widget.setParent(None)
        old.widget.deleteLater()
        self.graph = graph
        self._graph_layout.addWidget(graph.widget)
        self._attach_properties_bin()

    # ------------------------------------------------------------ 工具栏

    def _build_toolbar(self) -> None:
        bar = self.addToolBar("主")
        bar.setMovable(False)

        def action(text: str, slot, shortcut: str | None = None, tip: str = "") -> QtGui.QAction:
            act = QtGui.QAction(text, self)
            act.triggered.connect(slot)
            if shortcut:
                act.setShortcut(QtGui.QKeySequence(shortcut))
            if tip:
                act.setToolTip(tip)
            bar.addAction(act)
            return act

        action("新建", self.new_doc, "Ctrl+N")
        action("打开…", self.open_doc, "Ctrl+O")
        action("保存", self.save_doc, "Ctrl+S")
        action("另存为…", lambda: self.save_doc(force_dialog=True))
        bar.addSeparator()

        self.act_run = action("▶ 运行图", self.run_flow, "F5", "把当前画布提交给后端执行")
        self.act_stop = action("■ 停止", self.stop_flow, "Shift+F5")
        self.act_stop.setEnabled(False)
        action("执行选中节点", self.trial_run_selected, "F6",
               "只跑这一个节点（含其数据入边引用的上游），actuation 层会被后端拒绝")
        bar.addSeparator()
        action("适配视图", lambda: self.graph.fit_to_selection() if self.graph.selected_nodes()
               else self.graph.auto_layout_nodes(), "Ctrl+F")
        action("重载节点目录", self.reload_catalog, "Ctrl+R", "重新拉取 /api/nodes")

        bar.addSeparator()
        bar.addWidget(QtWidgets.QLabel(" 图名 "))
        self.name_edit = QtWidgets.QLineEdit("untitled")
        self.name_edit.setMaximumWidth(180)
        self.name_edit.textChanged.connect(lambda *_: self._mark_dirty())
        bar.addWidget(self.name_edit)

        bar.addWidget(QtWidgets.QLabel(" 步数上限 "))
        self.max_steps = QtWidgets.QSpinBox()
        self.max_steps.setRange(1, 1_000_000)
        self.max_steps.setValue(flowdoc.DEFAULT_MAX_STEPS)
        bar.addWidget(self.max_steps)

        bar.addWidget(QtWidgets.QLabel(" 时长上限(s) "))
        self.timeout_sec = QtWidgets.QSpinBox()
        self.timeout_sec.setRange(1, 86400)
        self.timeout_sec.setValue(flowdoc.DEFAULT_TIMEOUT_SEC)
        bar.addWidget(self.timeout_sec)

    # ------------------------------------------------------------ 节点库

    def _build_palette(self) -> None:
        dock = QtWidgets.QDockWidget("节点库", self)
        dock.setAllowedAreas(QtCore.Qt.LeftDockWidgetArea | QtCore.Qt.RightDockWidgetArea)

        self.palette_tree = QtWidgets.QTreeWidget()
        self.palette_tree.setHeaderLabels(["节点", "说明"])
        self.palette_tree.setColumnWidth(0, 150)
        self.palette_tree.itemDoubleClicked.connect(self._on_palette_activated)

        hint = QtWidgets.QLabel("双击添加节点；拖动画布空白处平移")
        hint.setWordWrap(True)
        hint.setStyleSheet("color:#9aa0aa; padding:4px;")

        container = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(container)
        layout.setContentsMargins(4, 4, 4, 4)
        layout.addWidget(hint)
        layout.addWidget(self.palette_tree)
        dock.setWidget(container)
        self.addDockWidget(QtCore.Qt.LeftDockWidgetArea, dock)

    def _rebuild_palette(self) -> None:
        self.palette_tree.clear()
        if self.catalog is None:
            return
        grouped = self.catalog.layers()
        for layer in LAYER_ORDER:
            specs = grouped.get(layer)
            if not specs:
                continue
            root = QtWidgets.QTreeWidgetItem([LAYER_LABELS.get(layer, layer), f"{len(specs)} 个"])
            root.setFlags(QtCore.Qt.ItemIsEnabled)
            for spec in sorted(specs, key=lambda s: s["type"]):
                child = QtWidgets.QTreeWidgetItem([spec["type"], spec.get("summary", "")])
                child.setData(0, QtCore.Qt.UserRole, spec["type"])
                child.setToolTip(0, self._spec_tooltip(spec))
                root.addChild(child)
            self.palette_tree.addTopLevelItem(root)
        self.palette_tree.expandAll()

    @staticmethod
    def _spec_tooltip(spec: dict) -> str:
        lines = [spec.get("summary", ""), ""]
        for port in spec.get("ports") or []:
            lines.append(f"  [{port['kind']}] {port['name']} — {port.get('description', '')}")
        for param in spec.get("params") or []:
            default = f"（默认 {param['default']!r}）" if param.get("default") is not None else ""
            lines.append(f"  参数 {param['name']}: {param.get('description', '')}{default}")
        if spec.get("layer") == "actuation":
            lines.append("")
            lines.append("⚠ 该层会驱动设备，不支持单节点试跑")
        return "\n".join(lines)

    def _on_palette_activated(self, item: QtWidgets.QTreeWidgetItem, _column: int) -> None:
        node_type = item.data(0, QtCore.Qt.UserRole)
        if node_type:
            self.add_node(node_type)

    # ------------------------------------------------------------ 属性面板

    def _build_inspector(self) -> None:
        self.inspector_dock = QtWidgets.QDockWidget("属性（含 slow 慢感知）", self)
        self.inspector_dock.setAllowedAreas(QtCore.Qt.RightDockWidgetArea)
        self._inspector_holder = QtWidgets.QWidget()
        self._inspector_layout = QtWidgets.QVBoxLayout(self._inspector_holder)
        self._inspector_layout.setContentsMargins(0, 0, 0, 0)
        self.inspector_dock.setWidget(self._inspector_holder)
        self.addDockWidget(QtCore.Qt.RightDockWidgetArea, self.inspector_dock)

    def _attach_properties_bin(self) -> None:
        while self._inspector_layout.count():
            item = self._inspector_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        self.prop_bin = PropertiesBinWidget(node_graph=self.graph)
        self._inspector_layout.addWidget(self.prop_bin)
        self.graph.add_properties_bin(self.prop_bin)

    # ------------------------------------------------------------ 设备面板

    def _build_run_panel(self) -> None:
        dock = QtWidgets.QDockWidget("运行 / 设备", self)
        dock.setAllowedAreas(QtCore.Qt.BottomDockWidgetArea | QtCore.Qt.RightDockWidgetArea)

        tabs = QtWidgets.QTabWidget()
        tabs.addTab(self._build_device_tab(), "设备")
        tabs.addTab(self._build_status_tab(), "运行状态")
        tabs.addTab(self._build_log_tab(), "日志")
        dock.setWidget(tabs)
        self.addDockWidget(QtCore.Qt.BottomDockWidgetArea, dock)
        dock.setMinimumHeight(260)

    def _build_device_tab(self) -> QtWidgets.QWidget:
        page = QtWidgets.QWidget()
        layout = QtWidgets.QGridLayout(page)

        self.video_status = QtWidgets.QLabel("未知")
        self.video_combo = QtWidgets.QComboBox()
        self.video_combo.setMinimumWidth(260)
        btn_refresh = QtWidgets.QPushButton("刷新")
        btn_refresh.clicked.connect(self.refresh_devices)
        btn_video_on = QtWidgets.QPushButton("连接")
        btn_video_on.clicked.connect(self.connect_video)
        btn_video_off = QtWidgets.QPushButton("断开")
        btn_video_off.clicked.connect(self.disconnect_video)

        layout.addWidget(QtWidgets.QLabel("视频源"), 0, 0)
        layout.addWidget(self.video_status, 0, 1)
        layout.addWidget(self.video_combo, 0, 2)
        layout.addWidget(btn_refresh, 0, 3)
        layout.addWidget(btn_video_on, 0, 4)
        layout.addWidget(btn_video_off, 0, 5)

        self.mcu_status = QtWidgets.QLabel("未知")
        self.mcu_combo = QtWidgets.QComboBox()
        self.mcu_combo.setEditable(True)
        self.mcu_combo.setMinimumWidth(260)
        btn_mcu_mock = QtWidgets.QPushButton("连接 mock")
        btn_mcu_mock.clicked.connect(lambda: self.connect_mcu("mock"))
        btn_mcu_on = QtWidgets.QPushButton("连接")
        btn_mcu_on.clicked.connect(lambda: self.connect_mcu(self.mcu_combo.currentText().strip()))
        btn_mcu_off = QtWidgets.QPushButton("断开")
        btn_mcu_off.clicked.connect(self.disconnect_mcu)

        layout.addWidget(QtWidgets.QLabel("单片机"), 1, 0)
        layout.addWidget(self.mcu_status, 1, 1)
        layout.addWidget(self.mcu_combo, 1, 2)
        layout.addWidget(btn_mcu_mock, 1, 3)
        layout.addWidget(btn_mcu_on, 1, 4)
        layout.addWidget(btn_mcu_off, 1, 5)

        self.ocr_status = QtWidgets.QLabel("未知")
        self.ocr_backend = QtWidgets.QComboBox()
        self.ocr_model_dir = QtWidgets.QLineEdit()
        self.ocr_model_dir.setPlaceholderText("PP-OCR 模型目录（ppocr 后端必填）")
        btn_ocr_apply = QtWidgets.QPushButton("应用 OCR 设置")
        btn_ocr_apply.clicked.connect(self.apply_ocr)

        layout.addWidget(QtWidgets.QLabel("OCR 后端"), 2, 0)
        layout.addWidget(self.ocr_status, 2, 1)
        layout.addWidget(self.ocr_backend, 2, 2)
        layout.addWidget(self.ocr_model_dir, 2, 3, 1, 2)
        layout.addWidget(btn_ocr_apply, 2, 5)

        note = QtWidgets.QLabel(
            "设备状态由 EasyCon（后端）单一持有：画布上的连接/断开直接作用于后端服务，"
            "与 GUI 监控页各持一份，同一块采集卡不要在两处同时打开。")
        note.setWordWrap(True)
        note.setStyleSheet("color:#9aa0aa;")
        layout.addWidget(note, 3, 0, 1, 6)
        return page

    def _build_status_tab(self) -> QtWidgets.QWidget:
        page = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(page)
        self.run_status = QtWidgets.QLabel("未运行")
        self.run_status.setStyleSheet("font-weight:bold;")
        layout.addWidget(self.run_status)

        self.records = QtWidgets.QTableWidget(0, 6)
        self.records.setHorizontalHeaderLabels(["节点", "类型", "执行", "复用", "最近(ms)", "累计(ms)"])
        self.records.horizontalHeader().setStretchLastSection(True)
        self.records.setEditTriggers(QtWidgets.QAbstractItemView.NoEditTriggers)
        layout.addWidget(self.records)
        return page

    def _build_log_tab(self) -> QtWidgets.QWidget:
        page = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(page)
        self.log_view = QtWidgets.QPlainTextEdit()
        self.log_view.setReadOnly(True)
        self.log_view.setMaximumBlockCount(2000)
        layout.addWidget(self.log_view)
        return page

    # ------------------------------------------------------------ 后端交互

    def log(self, message: str) -> None:
        self.log_view.appendPlainText(message)

    def reload_catalog(self) -> None:
        document = self.canvas_to_doc() if self.catalog is not None else None
        try:
            payload = self.api.nodes()
        except ApiError as exc:
            self.log(f"[目录] 拉取失败：{exc}")
            QtWidgets.QMessageBox.warning(
                self, "无法连接后端",
                f"{exc}\n\n请先启动后端：\n"
                "  dotnet run --project src/EasyCon2.CLI -- serve --port 19391\n"
                "或启动 GUI（内嵌服务，默认 19391）。")
            return

        graph = self._new_graph()
        self.catalog = NodeCatalog(payload)
        self.catalog.register_into(graph)
        self._install_graph(graph)
        self._rebuild_palette()
        for problem in self.catalog.register_errors:
            self.log(f"[目录] 注册失败 {problem}")
        self.log(f"[目录] 已加载 {len(self.catalog.specs)} 个节点类型（v{self.catalog.version}）")

        if self.catalog.ocr_backends:
            self.ocr_backend.clear()
            self.ocr_backend.addItems(self.catalog.ocr_backends)

        if document and document.get("nodes"):
            self.doc_to_canvas(document)
            self.log("[目录] 已按新目录重建画布内容")

    def refresh_devices(self) -> None:
        try:
            video = self.api.video_info()
            mcu = self.api.mcu_info()
            options = self.api.options()
        except ApiError as exc:
            self.log(f"[设备] 查询失败：{exc}")
            return

        self.video_combo.clear()
        for source in video.get("sources") or []:
            self.video_combo.addItem(f"[{source.get('index')}] {source.get('name')}", source.get("index"))
        self._set_status(self.video_status, bool(video.get("connected")))

        current = self.mcu_combo.currentText()
        self.mcu_combo.clear()
        self.mcu_combo.addItems([str(p) for p in (mcu.get("ports") or [])])
        if current:
            self.mcu_combo.setEditText(current)
        self._set_status(self.mcu_status, bool(mcu.get("connected")))

        ocr = options.get("ocr") or {}
        backend = str(ocr.get("backend") or "none")
        index = self.ocr_backend.findText(backend)
        if index >= 0:
            self.ocr_backend.setCurrentIndex(index)
        if ocr.get("modelDir") and not self.ocr_model_dir.text():
            self.ocr_model_dir.setText(str(ocr["modelDir"]))
        detail = f"{backend}"
        if ocr.get("error"):
            detail += f"（装配失败：{ocr['error']}）"
        self.ocr_status.setText(detail)
        self.ocr_status.setStyleSheet("color:#e0b050;" if ocr.get("error") else "color:#7fc97f;")

    @staticmethod
    def _set_status(label: QtWidgets.QLabel, connected: bool) -> None:
        label.setText("已连接" if connected else "未连接")
        label.setStyleSheet("color:#7fc97f; font-weight:bold;" if connected else "color:#9aa0aa;")

    def connect_video(self) -> None:
        index = self.video_combo.currentData()
        if index is None:
            QtWidgets.QMessageBox.information(self, "提示", "没有可用的视频源，先点“刷新”。")
            return
        self._guard(lambda: self.api.connect_video(int(index)), "视频源已连接")

    def disconnect_video(self) -> None:
        self._guard(self.api.disconnect_video, "视频源已断开")

    def connect_mcu(self, port: str) -> None:
        if not port:
            QtWidgets.QMessageBox.information(self, "提示", "请选择或输入串口名（mock = 无硬件虚拟手柄）。")
            return
        self._guard(lambda: self.api.connect_mcu(port), f"单片机已连接: {port}")

    def disconnect_mcu(self) -> None:
        self._guard(self.api.disconnect_mcu, "单片机已断开")

    def apply_ocr(self) -> None:
        backend = self.ocr_backend.currentText().strip() or "none"
        model_dir = self.ocr_model_dir.text().strip() or None
        self._guard(lambda: self.api.set_ocr_backend(backend, model_dir), f"OCR 后端已设为 {backend}")

    def _guard(self, call, success_message: str) -> None:
        try:
            call()
        except ApiError as exc:
            QtWidgets.QMessageBox.warning(self, "操作失败", str(exc))
            self.log(f"[设备] {exc}")
        else:
            self.log(f"[设备] {success_message}")
        self.refresh_devices()

    # ------------------------------------------------------------ 文档

    def _mark_dirty(self, dirty: bool = True) -> None:
        self._dirty = dirty
        title = "EasyCon Flow 画布"
        if self._path:
            title += f" — {os.path.basename(self._path)}"
        self.setWindowTitle(("*" if dirty else "") + title)

    def canvas_to_doc(self) -> dict:
        if self.catalog is None:
            return flowdoc.build_doc("untitled", [], [])
        return flowdoc.build_doc(
            name=self.name_edit.text().strip() or "untitled",
            nodes=self._collect_states_fixed_ids(),
            connections=collect_connections(self.graph),
            max_steps=self.max_steps.value(),
            timeout_sec=self.timeout_sec.value(),
            catalog=self.catalog.specs,
        )

    def _collect_states_fixed_ids(self):
        """采集节点状态，顺手修复重复/缺失 id（画布复制节点会产生重复 id）。"""
        states = collect_states(self.graph)
        seen: set[str] = set()
        for state in states:
            if not state.id or state.id in seen:
                state.id = flowdoc.make_node_id(state.type, seen)
                node = self._node_by_old_id(state.id)
                if node is not None:
                    node.set_property("node_id", state.id)
                    node.set_name(state.id)
            seen.add(state.id)
        return states

    def _node_by_old_id(self, node_id: str):
        for node in self.graph.all_nodes():
            if str(node.name()) == node_id:
                return node
        return None

    def doc_to_canvas(self, document: dict) -> None:
        name, max_steps, timeout_sec, nodes, connections = flowdoc.parse_doc(
            document, self.catalog.specs if self.catalog else None)

        graph = self._new_graph()
        if self.catalog is not None:
            self.catalog.register_into(graph)
        self._install_graph(graph)

        self.name_edit.setText(name)
        self.max_steps.setValue(max_steps)
        self.timeout_sec.setValue(timeout_sec)

        by_id = {}
        for state in nodes:
            try:
                by_id[state.id] = add_state(graph, self.catalog, state)
            except Exception as exc:  # noqa: BLE001 - 未知类型等不该中断整图加载
                self.log(f"[加载] 跳过节点 {state.id}（{state.type}）: {exc}")

        for conn in connections:
            src = by_id.get(conn.src_id)
            dst = by_id.get(conn.dst_id)
            if src is None or dst is None:
                self.log(f"[加载] 跳过悬空连线 {conn.src_id}.{conn.src_port} → {conn.dst_id}")
                continue
            try:
                if conn.kind == flowdoc.EXEC_KIND:
                    if output_port(src, conn.src_port) is None:
                        src.add_output(conn.src_port)
                    output_port(src, conn.src_port).connect_to(input_port(dst, "in"))
                else:
                    output_port(src, conn.src_port).connect_to(input_port(dst, conn.dst_port))
            except Exception as exc:  # noqa: BLE001 - 端口缺失等不该中断加载
                self.log(f"[加载] 连线失败 {conn.src_id}.{conn.src_port} → {conn.dst_id}.{conn.dst_port}: {exc}")

        self._mark_dirty(False)
        if nodes and not any(state.pos for state in nodes):
            graph.auto_layout_nodes()
        self.log(f"[加载] {len(nodes)} 个节点 / {len(connections)} 条 exec+数据连线")

    def add_node(self, node_type: str, pos=None) -> None:
        if self.catalog is None or node_type not in self.catalog.types:
            self.log(f"[画布] 未知节点类型: {node_type}")
            return
        existing = {n.name() for n in self.graph.all_nodes()}
        new_id = flowdoc.make_node_id(node_type, existing)
        node = self.graph.create_node(self.catalog.types[node_type], name=new_id, pos=pos)
        node.set_property("node_id", new_id)
        self._mark_dirty()
        self.log(f"[画布] 已添加 {node_type} → {new_id}")

    def new_doc(self) -> None:
        self.doc_to_canvas(flowdoc.build_doc("untitled", [], []))
        self._path = None
        self._mark_dirty(False)

    def open_doc(self) -> None:
        path, _ = QtWidgets.QFileDialog.getOpenFileName(
            self, "打开编排图", "", "Flow 图 (*.flow.json *.json)")
        if not path:
            return
        try:
            with open(path, encoding="utf-8") as handle:
                document = json.load(handle)
        except (OSError, ValueError) as exc:
            QtWidgets.QMessageBox.critical(self, "打开失败", str(exc))
            return

        problems = flowdoc.validate_doc(document)
        if problems:
            QtWidgets.QMessageBox.warning(self, "图有问题（仍会载入）", "\n".join(problems))
        self.doc_to_canvas(document)
        self._path = path
        self._mark_dirty(False)
        self.log(f"[打开] {path}")

    def save_doc(self, force_dialog: bool = False) -> None:
        path = self._path
        if force_dialog or not path:
            path, _ = QtWidgets.QFileDialog.getSaveFileName(
                self, "保存编排图", path or "untitled.flow.json", "Flow 图 (*.flow.json)")
            if not path:
                return
            if not path.endswith(".json"):
                path += ".flow.json"

        document = self.canvas_to_doc()
        problems = flowdoc.validate_doc(document)
        if problems:
            answer = QtWidgets.QMessageBox.question(
                self, "图有问题", "\n".join(problems) + "\n\n仍要保存吗？")
            if answer != QtWidgets.QMessageBox.Yes:
                return

        try:
            with open(path, "w", encoding="utf-8") as handle:
                json.dump(document, handle, ensure_ascii=False, indent=2)
        except OSError as exc:
            QtWidgets.QMessageBox.critical(self, "保存失败", str(exc))
            return

        self._path = path
        self._mark_dirty(False)
        self.log(f"[保存] {path}")

    # ------------------------------------------------------------ 运行

    def run_flow(self) -> None:
        document = self.canvas_to_doc()
        problems = flowdoc.validate_doc(document)
        if problems:
            QtWidgets.QMessageBox.warning(self, "无法运行", "\n".join(problems))
            return
        try:
            self._run_id = self.api.run_flow(document)
        except ApiError as exc:
            QtWidgets.QMessageBox.critical(self, "运行失败", str(exc))
            return

        self._reset_highlight()
        self.run_status.setText(f"运行中… runId={self._run_id}")
        self.act_run.setEnabled(False)
        self.act_stop.setEnabled(True)
        self._poll.start()
        self.log(f"[运行] runId={self._run_id}（{len(document['nodes'])} 节点）")

    def stop_flow(self) -> None:
        if not self._run_id:
            return
        try:
            self.api.stop_flow(self._run_id)
            self.log(f"[运行] 已请求停止 {self._run_id}")
        except ApiError as exc:
            self.log(f"[运行] 停止失败：{exc}")

    def _poll_status(self) -> None:
        if not self._run_id:
            self._poll.stop()
            return
        try:
            payload = self.api.status(self._run_id)
        except ApiError as exc:
            self._poll.stop()
            self.run_status.setText(f"状态查询失败：{exc}")
            self._finish_run()
            return

        runs = payload.get("runs") or []
        if not runs:
            self._poll.stop()
            self.run_status.setText("运行记录已丢失")
            self._finish_run()
            return

        run = runs[0]
        status = run.get("status")
        self.run_status.setText(
            f"{status}  步数 {run.get('steps', 0)}  用时 {run.get('totalMs', 0):.0f}ms"
            + (f"  错误节点 {run.get('errorNode')}: {run.get('error')}" if run.get("error") else ""))

        records = run.get("records") or []
        self._fill_records(records)
        self._highlight(records, run.get("errorNode"))

        if status not in ("running", None):
            self._poll.stop()
            self._finish_run()
            events = run.get("events") or []
            for event in events[-12:]:
                self.log(f"[事件] {event}")

    def _finish_run(self) -> None:
        self.act_run.setEnabled(True)
        self.act_stop.setEnabled(False)

    def _fill_records(self, records: list[dict]) -> None:
        self.records.setRowCount(len(records))
        for row, record in enumerate(sorted(records, key=lambda r: -float(r.get("totalMs") or 0))):
            values = [
                str(record.get("id", "")),
                str(record.get("type", "")),
                str(record.get("count", 0)),
                str(record.get("reused", 0)),
                f"{float(record.get('lastMs') or 0):.0f}",
                f"{float(record.get('totalMs') or 0):.0f}",
            ]
            for column, value in enumerate(values):
                self.records.setItem(row, column, QtWidgets.QTableWidgetItem(value))

    def _reset_highlight(self) -> None:
        for node in self.graph.all_nodes():
            spec = getattr(node, "_spec", None)
            if spec is None:
                continue
            node.set_color(*LAYER_COLORS.get(spec.get("layer", "flow"), LAYER_COLORS["flow"]))

    def _highlight(self, records: list[dict], error_node: str | None) -> None:
        by_id = {n.get_property("node_id") or n.name(): n for n in self.graph.all_nodes()}
        self._reset_highlight()
        for record in records:
            node = by_id.get(str(record.get("id")))
            if node is None:
                continue
            node.set_color(*(COLOR_ERROR if str(record.get("id")) == error_node else COLOR_EXECUTED))
        if error_node and error_node in by_id:
            by_id[error_node].set_color(*COLOR_ERROR)

    # ------------------------------------------------------------ 单节点试跑

    def _on_node_double_clicked(self, node) -> None:
        self.trial_run_selected(node)

    def trial_run_selected(self, node=None) -> None:
        if node is None or not hasattr(node, "_spec"):
            selected = self.graph.selected_nodes()
            if not selected:
                QtWidgets.QMessageBox.information(self, "提示", "先在画布上选中一个节点。")
                return
            node = selected[0]

        spec = getattr(node, "_spec", None)
        if spec is None:
            return

        node_identifier = str(node.get_property("node_id") or node.name())
        document = self.canvas_to_doc()
        try:
            result = self.api.run_node_in_graph(document, node_identifier)
        except ApiError as exc:
            QtWidgets.QMessageBox.warning(self, "试跑被拒绝", str(exc))
            self.log(f"[试跑] {node_identifier} 被拒绝：{exc}")
            return

        self._show_trial_result(node_identifier, spec, result)

    def _show_trial_result(self, node_identifier: str, spec: dict, result: dict) -> None:
        outputs = result.get("outputs") or {}
        port = result.get("port")
        summary = [
            f"节点: {node_identifier}（{spec['type']}）",
            f"耗时: {float(result.get('ms') or 0):.1f} ms"
            + ("（slow 复用，未真正执行）" if result.get("reused") else ""),
        ]
        if port:
            summary.append(f"exec 出口: {port}")
        if result.get("error"):
            summary.append(f"错误: {result['error']}")
        if outputs:
            summary.append("")
            summary.append("数据输出:")
            for key, value in outputs.items():
                text = json.dumps(value, ensure_ascii=False) if not isinstance(value, str) else value
                if key == "image":
                    text = f"<图像 {max(len(value) // 1024, 1)} KB>"
                summary.append(f"  {key} = {text[:400]}")

        box = QtWidgets.QMessageBox(self)
        box.setWindowTitle("节点试跑结果")
        box.setText("\n".join(summary))
        image = outputs.get("image")
        if isinstance(image, str) and image:
            pixmap = self._decode_pixmap(image)
            if pixmap is not None:
                box.setIconPixmap(pixmap.scaledToWidth(480, QtCore.Qt.SmoothTransformation))
        box.exec()
        self.log(f"[试跑] {node_identifier} ok={result.get('ok')} port={port} ms={result.get('ms')}")

    @staticmethod
    def _decode_pixmap(base64_png: str):
        try:
            raw = base64.b64decode(base64_png, validate=False)
        except (ValueError, TypeError):
            return None
        pixmap = QtGui.QPixmap()
        if not pixmap.loadFromData(raw, "PNG"):
            return None
        return pixmap

    # ------------------------------------------------------------ 生命周期

    def closeEvent(self, event: QtGui.QCloseEvent) -> None:  # noqa: N802 - Qt 命名
        if self._dirty:
            answer = QtWidgets.QMessageBox.question(
                self, "未保存", "画布有未保存的修改，仍要退出吗？")
            if answer != QtWidgets.QMessageBox.Yes:
                event.ignore()
                return
        self._poll.stop()
        super().closeEvent(event)
