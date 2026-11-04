"""EasyCon Flow 服务 HTTP 客户端（后端 = CLI `serve` 或 GUI 内嵌服务）。

只依赖 requests；后端地址默认 http://127.0.0.1:19391（可用环境变量 EC_FLOW_URL 覆盖）。
"""

from __future__ import annotations

import os
from typing import Any

import requests

DEFAULT_BASE_URL = os.environ.get("EC_FLOW_URL", "http://127.0.0.1:19391")
DEFAULT_TIMEOUT = 20.0


class ApiError(RuntimeError):
    """后端返回的错误（带服务端给出的可读原因）。"""

    def __init__(self, message: str, status: int | None = None):
        super().__init__(message)
        self.status = status


class FlowApi:
    """Flow 服务的薄封装：设备管理、图运行、单节点试跑、选项。"""

    def __init__(self, base_url: str = DEFAULT_BASE_URL, timeout: float = DEFAULT_TIMEOUT):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout

    # ------------------------------------------------------------ 基础

    def _url(self, path: str) -> str:
        return f"{self.base_url}{path}"

    def _request(self, method: str, path: str, **kwargs) -> Any:
        kwargs.setdefault("timeout", self.timeout)
        try:
            response = requests.request(method, self._url(path), **kwargs)
        except requests.RequestException as exc:
            raise ApiError(f"无法连接 Flow 服务 {self.base_url}：{exc}") from exc

        if response.status_code >= 400:
            raise ApiError(self._error_text(response), response.status_code)
        if not response.content:
            return None
        try:
            return response.json()
        except ValueError:
            return response.text

    @staticmethod
    def _error_text(response: requests.Response) -> str:
        try:
            payload = response.json()
        except ValueError:
            return f"HTTP {response.status_code}: {response.text[:200]}"
        if isinstance(payload, dict):
            return str(payload.get("error") or payload.get("errors") or payload)
        return f"HTTP {response.status_code}: {payload}"

    def get(self, path: str) -> Any:
        return self._request("GET", path)

    def post(self, path: str, payload: dict | None = None) -> Any:
        return self._request("POST", path, json=payload or {})

    # ------------------------------------------------------------ 服务与目录

    def health(self) -> dict:
        return self.get("/api/health")

    def nodes(self) -> dict:
        """节点目录（nodes / slow / keys / ocrBackends）。"""
        return self.get("/api/nodes")

    def options(self) -> dict:
        return self.get("/api/options")

    def set_ocr_backend(self, backend: str, model_dir: str | None = None) -> dict:
        payload: dict = {"ocrBackend": backend}
        if model_dir:
            payload["ocrModelDir"] = model_dir
        return self.post("/api/options", payload)

    # ------------------------------------------------------------ 设备

    def video_info(self) -> dict:
        return self.get("/api/device/video")

    def connect_video(self, index: int, api: int = 0) -> dict:
        return self.post("/api/device/video/connect", {"index": int(index), "api": int(api)})

    def disconnect_video(self) -> dict:
        return self.post("/api/device/video/disconnect")

    def mcu_info(self) -> dict:
        return self.get("/api/device/mcu")

    def connect_mcu(self, port: str) -> dict:
        return self.post("/api/device/mcu/connect", {"port": port})

    def disconnect_mcu(self) -> dict:
        return self.post("/api/device/mcu/disconnect")

    # ------------------------------------------------------------ 图运行

    def run_flow(self, graph: dict) -> str:
        """提交图 JSON，返回 runId。"""
        payload = self.post("/api/flow/run", {"json": _dumps(graph)})
        run_id = (payload or {}).get("runId")
        if not run_id:
            raise ApiError(f"后端未返回 runId: {payload!r}")
        return str(run_id)

    def run_flow_file(self, path: str) -> str:
        payload = self.post("/api/flow/run", {"path": path})
        run_id = (payload or {}).get("runId")
        if not run_id:
            raise ApiError(f"后端未返回 runId: {payload!r}")
        return str(run_id)

    def stop_flow(self, run_id: str) -> dict:
        return self.post("/api/flow/stop", {"runId": run_id})

    def status(self, run_id: str | None = None) -> dict:
        path = "/api/flow/status" if not run_id else f"/api/flow/status?runId={run_id}"
        return self.get(path)

    # ------------------------------------------------------------ 单节点试跑

    def run_node(self, node_type: str, params: dict | None = None,
                 inputs: dict | None = None) -> dict:
        payload: dict = {"type": node_type}
        if params:
            payload["params"] = params
        if inputs:
            payload["inputs"] = inputs
        return self.post("/api/node/run", payload)

    def run_node_in_graph(self, graph: dict, node_id: str) -> dict:
        """按图试跑某个节点：其数据入边引用的上游会被一并求值。"""
        return self.post("/api/node/run", {"graph": _dumps(graph), "nodeId": node_id})


def _dumps(payload: dict) -> str:
    import json
    return json.dumps(payload, ensure_ascii=False)
