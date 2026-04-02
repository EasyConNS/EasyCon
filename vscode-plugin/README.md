# EasyCon for VS Code

EasyCon 伊机控 VS Code 插件，为 `.ecs` 脚本文件提供语法高亮、注释切换和脚本执行等功能。

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

### 执行脚本

支持在 VS Code 中直接运行 EasyCon 脚本，有以下三种方式：

- 按 `F5` 快捷键
- 点击编辑器标题栏的运行按钮 ▶
- 右键编辑器选择「Run EasyCon Script」

插件会自动读取脚本同目录下的 `config.toml` 配置文件，获取端口、视频采集设备等参数并传递给 `ezcon` 命令执行。

状态栏会显示当前安装的 EasyCon 版本号。

## 安装

### 方法一：复制到扩展目录

将整个 `vscode-plugin` 目录复制到 VS Code 扩展目录下，并重命名为 `easycon.vscode-ecs-<版本号>`：

- Windows: `%USERPROFILE%\.vscode\extensions\`
- macOS/Linux: `~/.vscode/extensions/`

然后重启 VS Code。

### 方法二：使用 build-vsix 脚本打包安装

在 `vscode-plugin` 目录下执行打包脚本，生成 `.vsix` 安装包后通过 VS Code 安装：

```powershell
.\build-vsix.ps1
```

打包完成后，通过 VS Code 的 `扩展 -> 从 VSIX 安装` 选项安装生成的 `.vsix` 文件，或使用命令行：

```powershell
code --install-extension vscode-ecs-0.0.1.vsix
```

## 前置条件

使用脚本执行功能需要安装 EasyCon 命令行工具 `ezcon`，可通过设置环境变量 `EASYCON_ROOT` 指定其路径，或将 `ezcon` 添加到系统 PATH 中。
