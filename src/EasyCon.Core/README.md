# EasyCon.Core 模块

## 模块概述

Core模块是EasyCon2的业务枢纽，协调Script、Device、Capture等子模块，为上层UI提供统一API。职责包括脚本执行编排、项目管理、配置管理和推送通知。

## 核心功能

### 脚本执行引擎
- **Scripter** — 脚本编排器，负责脚本的解析、编译、执行和停止
- **IRunner** — 脚本运行器接口，统一不同语言的执行方式
- 内置三种运行器：
  - **EasyRunner** — 运行自研ECS脚本，通过 `Compilation` 管线执行
  - **PyRunner** — 运行Python脚本（基于Python.Runtime）
  - **LuaRunner** — 运行Lua脚本（基于NLua）
- **GamePadAdapter** — 将 `NintendoSwitch` 设备适配为 `ICGamePad` 接口，桥接Device模块与Script模块

### 项目管理
- **ProjectManager** — 基于ZIP的项目文件管理，打包脚本（main.ecs + lib/*.ecs）和图像标签（img/*.IL）

### 配置管理
- **ConfigManager** — 静态配置管理器，统一JSON读写
- **ConfigState** — 应用全局设置（采集类型、界面选项、烧录后自动运行等）
- **KeyMappingConfig** — 键盘到Switch按键的映射配置
- **AlertConfig** — 推送通知配置（支持PushPlus、Bark、自定义Webhook）

### 推送通知
- **AlertDispatcher** — 根据AlertConfig发送HTTP推送，支持模板变量替换

### 远程助手
- **AssistClient** — WebSocket客户端，连接远程助手服务，支持日志推送和远程指令

### 工具方法
- **ECCore** — 静态入口类，提供采集源枚举、设备列表、图像标签加载等便捷方法
- **ImgLabelExt** — 图像标签扩展方法，从帧中提取标签指定区域的子图
- **KeyExt** — 按键类型转换扩展（GamePadKey → ECKey）

## 配置文件

所有配置以JSON格式存储在 `%AppData%/easycon/` 目录，由 `AppPaths` 统一管理路径：
- `config.json` — 应用配置
- `keymapping.json` — 按键映射
- `alert.json` — 推送通知

## 依赖项

- EasyCon.Device — 设备通信
- EasyCon.Capture — 图像处理
- EasyCon.Script — 脚本解析

---

**版本**: 2.0
