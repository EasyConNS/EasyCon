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

- **虚拟手柄** - 完美模拟真实手柄操作，毫秒级精确时序
- **图像识别** - 基于OpenCV的模板匹配和OCR文字识别，支持智能决策
- **自研脚本语言** - 简单易学，支持复杂逻辑和实时调试
- **跨平台** - 支持 Windows、Linux、macOS，提供GUI和CLI两种界面

### 运行模式说明

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

## 应用场景

**宝可梦系列**: 全自动孵蛋、批量放生、极巨战寻物、过帧、SL紫光、自动挖矿、战斗塔等

**怪物猎人系列**: 自动刷护石、自动刷装备词条

**其他游戏**: 通用按键自动化，通过自定义脚本适配各种重复性操作

## 📚 核心文档

- **[快速开始](./docs/GETTING_STARTED.md)** - 新手友好的安装教程
- **[系统架构](./docs/Framework.md)** - 技术栈与系统架构设计
- **[脚本语法](./docs/Script.md)** - ECS脚本语言完整语法说明
- **[文档中心](./docs/README.md)** - 完整的技术文档导航

## 🤝 参与贡献

我们欢迎各种形式的贡献！无论是bug报告、功能建议还是代码提交。

- 📋 [贡献指南](./CONTRIBUTING.md)
- 🐛 [问题反馈](https://github.com/EasyConNS/EasyCon/issues)

## 📝 开源许可

本项目采用 GNU General Public License v3.0 许可证

- 查看 [LICENSE](LICENSE) 文件了解详情

## 👥 项目团队

- **维护者**: [ca1e](https://github.com/ca1e)(当前维护)、[elmagnifico](https://github.com/elmagnificogi)
- **原始作者**: [铃落](https://github.com/nukieberry)

## 🔧 硬件支持

- **单片机固件**: [EasyMCU](https://github.com/EasyConNS/EasyMCU) - 支持 ESP32、Arduino 等兼容单片机
- **[指令集说明](https://docs.qq.com/sheet/DZm1ydlZadkpncUNo?c=A88A0AZ0)** - 虚拟机指令集完整文档

---

<div align="center">

**让游戏自动化更简单** 🎮✨

**最后更新**: 2026年4月16日

</div>
