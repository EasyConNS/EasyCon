# 伊机控 EasyCon

> 任天堂Switch自动化控制程序 - 实现全自动孵蛋、放生等宝可梦功能

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue.svg)](https://github.com/dotnet/core)

## 项目简介

伊机控（EasyCon）是一款功能强大的任天堂Switch自动化控制程序，通过自定义脚本语言、图像识别和虚拟手柄技术，实现对Switch主机的完全自动化控制。

### 项目截图

![image-20220108012539217](https://s2.loli.net/2022/01/08/fZGywpk3mIh5Yde.png)
![image-20220108012549502](https://s2.loli.net/2022/01/08/SlCY95UFXJiIbuL.png)

## 技术特性

### 🎮 智能控制系统

- **虚拟手柄**: 完美模拟真实手柄操作
- **精确时序**: 毫秒级操作精度，避免输入错误
- **多种运行模式**: 支持联机、烧录、固件三种模式

#### 运行模式说明

**联机模式**（推荐新手）
- 通过电脑控制单片机，脚本在电脑端运行
- 实时显示运行过程，可以看到每一步操作
- 支持虚拟手柄，可以直接在电脑上操作Switch
- 适合调试和学习，操作直观友好

**烧录模式**（推荐进阶用户）
- 连线写入程序到单片机，脚本在单片机上运行
- 运行时不需要保持电脑连接，可独立工作
- 支持极限效率脚本，性能最优
- 适合长时间自动化任务

**固件模式**（适合特定场景）
- 生成固件文件后手动刷入单片机
- 适合没有USB-TTL线的情况
- 效果和烧录模式类似，部署灵活

### 🖼️ 图像识别能力
- **高精度匹配**: 基于OpenCV的模板匹配技术
- **OCR文字识别**: 自动识别游戏文本信息
- **智能决策**: 根据画面内容自动调整操作策略
- **实时处理**: 快速响应游戏状态变化

### 📜 强大脚本系统
- **简单易学**: 自研脚本语言，无需编程基础
- **功能完整**: 支持复杂逻辑和条件判断
- **即编即用**: 实时调试，快速验证脚本效果
- **模块化设计**: 易于扩展和维护

### 🌐 跨平台支持
- **多系统**: Windows、Linux、macOS全平台支持
- **现代UI**: 基于Avalonia的跨平台图形界面
- **命令行**: 强大的CLI工具，支持批处理

## 应用场景

### 宝可梦系列
- **🥚 全自动孵蛋** - 智能蛋序管理，自动筛选优秀个体
- **🔄 批量放生** - 高效处理大量宝可梦
- **🎯 极巨战寻物** - 自动寻找特定极巨化宝可梦
- **⏱️ 过帧操作** - 精确控制游戏帧数
- **💎 SL紫光** - 自动化闪光宝可梦获取
- **🚗 无限开车** - 循环获取奖励
- **🔨 合化石处理** - 自动化合成流程
- **⛏️ 自动挖矿** - 循环收集矿石
- **🏃 战斗塔** - 自动挑战高层战斗塔

### 怪物猎人系列
- 自动刷护石
- 自动刷装备词条

### 其他Switch游戏
- 通用的按键自动化，适合重复性操作
- 自定义脚本支持，适配各种游戏需求

## 📚 核心文档

- **[快速开始](./docs/GETTING_STARTED.md)** - 新手友好的安装教程
- **[系统架构](./docs/Framework.md)** - 系统架构和设计理念
- **[脚本语法](./docs/Script.md)** - ECS脚本语言完整语法说明
- **[文档中心](./docs/README.md)** - 完整的技术文档导航

## 🔧 技术架构

- **.NET 10.0**: 现代化的跨平台开发框架
- **Avalonia UI**: 优雅的跨平台用户界面
- **OpenCV**: 强大的图像处理和计算机视觉
- **自研脚本引擎**: 完整的编译器-虚拟机实现
- **模块化设计**: 松耦合架构，易于扩展维护

## 🤝 参与贡献

我们欢迎各种形式的贡献！无论是bug报告、功能建议还是代码提交。

- 📋 [贡献指南](./CONTRIBUTING.md)
- 🐛 [问题反馈](https://github.com/EasyConNS/EasyCon/issues)
- 💬 [讨论交流](https://github.com/EasyConNS/EasyCon)

## 📝 开源许可

本项目采用 GNU General Public License v3.0 许可证

- 查看 [LICENSE](LICENSE) 文件了解详情

## 👥 项目团队

### 核心维护

- **当前维护者**: [ca1e](https://github.com/ca1e) - 项目维护和开发

### 原创作者

- **[铃落](https://github.com/nukieberry)** - 项目创始人
- **[elmagnifico](https://github.com/elmagnificogi)** - 核心开发

### 社区支持

- **[GitHub Issues](https://github.com/EasyConNS/EasyCon/issues)** - 问题反馈和技术支持
- **[项目主页](https://github.com/EasyConNS/EasyCon)** - 源码仓库和文档

## 🔧 硬件支持

### 单片机固件

- **项目仓库**: [EasyConNS/EasyMCU](https://github.com/EasyConNS/EasyMCU)
- **固件功能**: Switch手柄模拟和通信协议实现
- **支持硬件**: ESP32、Arduino等兼容单片机

### 技术文档

- **[指令集说明](https://docs.qq.com/sheet/DZm1ydlZadkpncUNo?c=A88A0AZ0)** - 虚拟机指令集完整文档
- **[开发文档](./docs)** - 详细的架构设计和API文档

---

<div align="center">

**让游戏自动化更简单** 🎮✨

**最后更新**: 2026年4月16日

</div>
