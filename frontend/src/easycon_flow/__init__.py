"""EasyCon Flow 画布（Python 前端包）。

分层：
  flowdoc.py  flow.json 文档模型与画布映射（纯 Python，无 Qt，可单测）
  api.py      后端 HTTP 客户端
  nodes.py    目录 → NodeGraphQt 节点类 + 画布 ↔ 状态搬运
  window.py   主窗口（画布 / 节点库 / 属性 / 设备 / 运行面板）
  main.py     入口
"""

__all__ = ["api", "flowdoc", "nodes", "window"]
