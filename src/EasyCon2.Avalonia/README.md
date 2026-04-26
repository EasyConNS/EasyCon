# EasyCon2.Avalonia 模块

## 模块概述

EasyCon2.Avalonia是基于Avalonia 12的跨平台GUI实现，采用MVVM架构（CommunityToolkit.Mvvm），支持Windows、Linux和macOS。

## 技术架构

- **MVVM模式**: Views（`.axaml`）+ ViewModels + Services
- **依赖注入**: 服务通过接口注册（IDeviceService、ICaptureService、IScriptService等）
- **编译绑定**: `AvaloniaUseCompiledBindingsByDefault=true`，所有XAML绑定需指定 `x:DataType`

## 主要功能

- 设备连接与管理
- 脚本编辑与执行
- 视频画面监视
- 按键映射配置（KeyMappingWindow）
- 日志输出显示

## 相关模块

- [EasyCon.Core](../EasyCon.Core/) — 核心业务逻辑
- [EasyCon.Device](../EasyCon.Device/) — 设备通信
- [EasyCon.Capture](../EasyCon.Capture/) — 图像采集与识别
- [EasyCon.Script](../EasyCon.Script/) — 脚本解析与执行
- [EasyCon.VPad](../EasyCon.VPad/) — 虚拟手柄控件

---

**版本**: 2.0
