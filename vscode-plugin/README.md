# EasyCon for VS Code

EasyCon 伊机控 VS Code 插件，为 `.ecs` 脚本文件提供语法高亮、代码智能和脚本执行等功能。

## 功能特性

### 语法高亮

插件为 `.ecs` 文件提供完整的语法高亮支持，包括：

- **注释** — `#` 开头的行注释，支持 TODO/FIXME/HACK/UNDONE 标记
- **字符串** — 单引号和双引号字符串
- **变量** — `$var` 变量、`@var` 捕获变量、`_const` 常量
- **数字** — 整数、小数、科学计数法
- **关键字** — IF、FOR、WHILE、FUNC 等控制流关键字
- **内置函数** — RAND、TIME、PRINT、ALERT、WAIT、AMIIBO、BEEP 等
- **手柄按键** — A、B、X、Y、L、R 等游戏手柄按键常量

### 注释切换

支持使用 `Ctrl+/` 快捷键快速切换行注释。

### 代码导航

以下功能由 Python 语言服务器（LSP）提供：

- **跳转到定义** — 鼠标悬停到变量、常量、函数名上并按住 CTRL 键，点击可跳转到定义位置
- **悬停提示** — 将鼠标悬停到关键字、内置函数、变量、按键上可查看详细说明
- **自动补全** — 输入 `$`、`_`、`@`、`(` 时自动提示关键字、内置函数、变量名和常量名
- **文档符号** — 在 VS Code 的大纲视图中查看脚本的变量、常量和函数列表
- **语义高亮** — 基于 AST 的精确语义着色，区分按键、方向、变量等不同语义

### 代码格式化

保存 `.ecs` 文件时会自动格式化代码。格式化功能由 Python 语言服务器提供，无需安装 `ezcon` 命令行工具即可使用。

### 执行脚本

支持在 VS Code 中直接运行 EasyCon 脚本，有以下两种方式：

- 按 `F5` 快捷键
- 点击编辑器标题栏的运行按钮 ▶

插件会自动读取脚本同目录下的 `config.toml` 配置文件，获取端口、视频采集设备等参数并传递给 `ezcon` 命令执行。
config.toml文件内容示例：
```toml
[core]
port=COM22 #单片机端口号
camera=0 #采集卡序号
videotype=ANY #采集卡打开类型
```

状态栏会显示当前安装的 EasyCon 版本号。

## 架构说明

EasyCon for VS Code 采用混合架构，部分功能已迁移至新的 Python 语言服务器（LSP）。

### 已迁移至 Python LSP 的功能

这些功能由 `ecs-language-server`（Python / pygls）提供，随插件自动启动，不需要安装 `ezcon` 工具：

| 功能 | 说明 |
|---|---|
| **代码诊断** | 实时显示脚本中的语法错误和语义错误（如未闭合的语句块、无效的按键名等） |
| **跳转到定义** | 变量、常量、函数的定义跳转 |
| **悬停提示** | 关键字、内置函数、变量、按键的详细说明 |
| **自动补全** | 关键字、内置函数、变量名、常量名的智能补全 |
| **代码格式化** | 脚本的自动格式化（缩进、对齐等） |
| **文档符号** | VS Code 大纲视图中的符号列表 |
| **语义高亮** | 基于 AST 的精确语义着色（区分按键、方向键、修饰键等） |

### 保留在旧 C# 系统的功能

这些功能仍由 `ezcon`（.NET 命令行工具）提供：

| 功能 | 说明 |
|---|---|
| **脚本执行** | 通过 `ezcon run` 在 Nintendo Switch 硬件上执行脚本（按 F5 或点击运行按钮） |
| **配置管理** | 读取 `config.toml` 获取串口、摄像头等参数 |
| **版本显示** | 状态栏显示的 EasyCon 版本号 |

### 依赖关系

- **代码格式化、跳转定义、悬停提示、自动补全等编辑功能**：由 Python LSP 提供，不再依赖 `ezcon`
- **脚本执行**：仍然依赖 `ezcon run` 命令，需要安装 EasyCon CLI 工具

## 打包脚本

### 使用 build-vsix 脚本打包插件

在 `ci` 目录下执行打包脚本，生成 `.vsix` 安装包后通过 VS Code 安装：

**Linux/macOS:**
```bash
./ci/build-vsix.sh
```

**Windows (PowerShell):**
```powershell
.\ci\build-vsix.ps1
```

脚本会在vscode-plugin目录下生成 `easycon-for-vscode-x.x.x.vsix` 插件安装包。

## 安装

通过 VS Code 的 `扩展 -> 从 VSIX 安装` 选项安装打包后的 `.vsix` 文件或直接拖拽安装包到 VS Code插件列表中自动安装。

## 前置条件

### 脚本执行

使用脚本执行功能需要安装 EasyCon 命令行工具 `ezcon`，可通过设置环境变量 `EASYCON_ROOT` 指定其路径，或将 `ezcon` 添加到系统 PATH 中。

### 语言服务器

代码格式化、跳转定义、悬停提示、自动补全等编辑功能由 Python 语言服务器提供。语言服务器随插件自动启动，支持以下三种运行方式（按优先级）：

1. **自定义路径**：在 VS Code 设置中配置 `easycon.languageServer.path` 指定语言服务器可执行文件路径
2. **内置可执行文件**：使用插件 `bin/` 目录下的 `ecs-lsp`（Windows 下为 `ecs-lsp.exe`）
3. **Python 运行**：通过 `python -m easycon_script_lsp` 启动（需要 Python 3.10+）
4. **打包**：cd ecs-language-server && python -m PyInstaller ecs-lsp.spec