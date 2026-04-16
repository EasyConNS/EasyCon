# EasyCon2.Avalonia

> EasyCon2 跨平台用户界面实现

## 概述

这是EasyCon2项目的Avalonia UI实现，提供跨平台的图形用户界面。

## 快速导航

📖 **[完整文档导航](./DOCUMENTATION_INDEX.md)** - 查看所有文档  
🏗️ **[系统架构设计](./DESIGN_DOCUMENT.md)** - 完整的架构设计文档  
🎨 **[UI设计文档](./UI_DESIGN.md)** - 用户界面设计文档  
📦 **[模块详细设计](./MODULE_DESIGN.md)** - 各功能模块详细文档

## 项目结构

```
EasyCon2.Avalonia/
├── Assets/              # 资源文件
├── Converters/          # 数据转换器
├── Services/            # 业务服务
├── ViewModels/          # 视图模型
├── Views/               # 用户界面
├── App.axaml            # 应用程序入口
└── Program.cs           # 主程序
```

## 运行项目

```bash
# 还原依赖
dotnet restore

# 运行项目
dotnet run

# 发布单文件版本
dotnet publish -c Release -r win-x64 --self-contained
```

## 主要窗口

### MainWindow
主窗口，提供设备连接、脚本执行、日志显示等功能。

### KeyMappingWindow
按键映射配置窗口，用于设置键盘到手柄的映射关系。

## 技术特性

- **MVVM架构**: 基于CommunityToolkit.Mvvm
- **数据绑定**: 强大的双向数据绑定
- **跨平台**: 支持Windows、Linux、macOS
- **响应式设计**: 适配不同屏幕尺寸
- **主题支持**: 支持亮色/暗色主题切换

## 开发指南

### 添加新功能

1. 在 `ViewModels/` 中创建ViewModel
2. 在 `Views/` 中创建对应的View
3. 在 `Services/` 中添加业务逻辑
4. 在 `App.axaml.cs` 中注册服务

### 代码规范

- 使用C# 10特性
- 遵循MVVM模式
- 提供XML文档注释
- 编写单元测试

## 相关模块

- [EasyCon.Core](../EasyCon.Core/) - 核心功能模块
- [EasyCon.Device](../EasyCon.Device/) - 设备通信模块
- [EasyCon.Capture](../EasyCon.Capture/) - 图像处理模块
- [EasyCon.Script](../EasyCon.Script/) - 脚本解析模块
- [EasyCon.VPad](../EasyCon.VPad/) - 虚拟手柄模块

---

**最后更新**: 2026年4月16日