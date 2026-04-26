# EasyCon2 项目架构设计文档

## 1. 项目概述

EasyCon2 是一个面向游戏主机（主要是任天堂Switch）的自动化脚本执行平台。该系统允许用户通过自定义脚本语言控制游戏主机，实现自动化游戏操作、图像识别、虚拟手柄映射等功能。

### 1.1 项目目标
- 提供跨平台的游戏控制器自动化解决方案
- 支持图像识别和基于视觉的自动化操作
- 实现虚拟手柄与真实手柄的无缝映射
- 提供友好的用户界面和脚本开发体验

### 1.2 技术栈
- **UI框架**: Avalonia (跨平台), WinForms (Windows)
- **编程语言**: C# (.NET)
- **图像处理**: OpenCV
- **通信协议**: 串口通信、蓝牙、TCP/IP
- **脚本引擎**: 自定义脚本语言解析器

## 2. 整体架构

### 2.1 分层架构

```
┌─────────────────────────────────────────────────────────┐
│                   用户界面层 (UI Layer)                    │
│  ┌──────────────────┐         ┌──────────────────┐      │
│  │  Avalonia UI     │         │  WinForms UI     │      │
│  │  (跨平台)         │         │   (Windows)      │      │
│  └──────────────────┘         └──────────────────┘      │
└─────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────┐
│                 业务逻辑层 (Business Logic)               │
│  ┌──────────────────┐         ┌──────────────────┐      │
│  │  ViewModels      │         │   Services       │      │
│  │  (MVVM模式)       │         │   (业务服务)      │      │
│  └──────────────────┘         └──────────────────┘      │
└─────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────┐
│                   核心层 (Core Layer)                     │
│  ┌──────────────────────────────────────────────────┐   │
│  │              EasyCon.Core                        │   │
│  │  (脚本运行器、项目管理、配置管理)                    │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────┐
│                   模块层 (Module Layer)                   │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐  │
│  │  Device  │ │ Capture  │ │  Script  │ │  VPad    │  │
│  │ (设备通信)│ │(图像处理)│ │(脚本解析)│ │(虚拟手柄)│  │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘  │
└─────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────┐
│                   硬件层 (Hardware Layer)                 │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐                │
│  │ 单片机设备 │ │ 采集卡   │ │ 手柄设备  │                │
│  └──────────┘ └──────────┘ └──────────┘                │
└─────────────────────────────────────────────────────────┘
```

### 2.2 项目模块划分

#### 2.2.6 UI模块
- **EasyCon2.Avalonia**: 跨平台UI实现
- **EasyCon2**: Windows专用WinForms实现
- **EasyCon2.UI.Common**: 通用UI组件和资源

#### 2.2.1 核心模块 (EasyCon.Core)
- **职责**: 提供脚本执行、项目管理、配置管理等核心功能
- **主要组件**:
  - `ECCore`: 核心功能入口
  - `Scripter`: 脚本管理器
  - `ProjectManager`: 项目管理器
  - `Config`: 配置管理
  - `Runner`: 脚本运行器（支持多种脚本类型）

#### 2.2.2 设备通信模块 (EasyCon.Device)
- **职责**: 处理与下位机设备的通信
- **主要功能**:
  - 设备发现与连接
  - 串口/蓝牙/WIFI通信
  - 协议实现（Modbus/Serial/TCP）

#### 2.2.3 图像采集模块 (EasyCon.Capture)
- **职责**: 提供图像采集和处理功能
- **主要功能**:
  - 采集卡/虚拟摄像头驱动
  - 视频帧数据获取
  - 图像识别（模板匹配、OCR）

#### 2.2.4 脚本解析模块 (EasyCon.Script)
- **职责**: 实现自定义脚本语言的解析和执行
- **主要功能**:
  - 词法分析（Syntax/Lexer）
  - 语法解析（Syntax/Parser）
  - 绑定与类型检查（Binding/Binder）
  - 树解释执行（Evaluator）
  - 单片机字节码生成（Assembly/Assembler）

#### 2.2.5 虚拟手柄模块 (EasyCon.VPad)
- **职责**: 实现虚拟手柄UI和按键映射
- **主要功能**:
  - 手柄UI界面
  - 按键映射配置
  - 跨平台手柄支持

#### 2.2.6 UI模块
- **EasyCon2.Avalonia**: 跨平台UI实现
- **EasyCon2**: Windows专用WinForms实现
- **EasyCon2.UI.Common**: 通用UI组件

#### 2.2.7 命令行工具 (EasyCon2.CLI)
- **职责**: 提供命令行操作接口
- **主要功能**:
  - 脚本批量执行
  - 自动化测试
  - 系统管理

## 3. 数据流设计

### 3.1 脚本执行流程

```
用户操作 → UI层 → ViewModel → Service → Core层 → Script模块 → Device模块
                                                            ↓
用户反馈 ← UI层 ← ViewModel ← Service ← Core层 ← Device模块 ← 硬件设备
```

### 3.2 设备通信流程

```
Service层 → Device模块 → 协议层 → 物理连接 → 硬件设备
    ↓
状态更新 → 事件通知 → UI更新
```

### 3.3 图像处理流程

```
Service层 → Capture模块 → OpenCV → 图像采集
    ↓
图像数据 → 图像识别 → 结果返回 → 脚本决策
```

## 4. 接口设计

### 4.1 设备服务接口

```csharp
public interface IDeviceService
{
    bool IsConnected { get }
    event Action? ConnectionLost;
    string[] GetAvailablePorts();
    bool TryConnect(string port);
    string? AutoConnect();
    void Disconnect();
    NintendoSwitch GetDevice();
}
```

### 4.2 图像采集接口

```csharp
public interface ICaptureService
{
    bool IsConnected { get }
    event Action? ConnectionLost;
    string[] GetAvailableSources();
    bool TryConnect(string sourceName);
    void Disconnect();
    OpenCVCapture? GetCapture();
}
```

### 4.3 控制器服务接口

```csharp
public interface IControllerService
{
    bool IsConnected { get }
    string[] GetAvailableSources();
    bool TryConnect(string sourceName);
    void Disconnect();
}
```

### 4.4 脚本服务接口

```csharp
public interface IScriptService
{
    bool IsRunning { get }
    event Action<bool> IsRunningChanged;
    void Run(string scriptPath);
    void Stop();
}
```

### 4.5 日志服务接口

```csharp
public interface ILogService
{
    event Action<string?> LogAppended;
    void AddLog(string message);
    void Clear();
}
```

## 5. MVVM架构实现

### 5.1 ViewModel层设计

#### MainWindowViewModel
- **职责**: 主窗口的ViewModel，协调各个服务模块
- **主要功能**:
  - 管理设备连接状态
  - 处理脚本执行
  - 日志显示管理
  - UI状态同步

#### ViewModelBase
- **职责**: 提供ViewModel基础功能
- **主要功能**:
  - 属性变更通知
  - 通用验证逻辑
  - 生命周期管理

#### ThemeManager
- **职责**: 管理应用主题
- **主要功能**:
  - 主题切换
  - 颜色方案管理

### 5.2 View层设计

#### MainWindow
- **布局**: 
  - 顶部：脚本路径和打开按钮
  - 中部：日志输出区域
  - 底部：模块连接器区域

#### KeyMappingWindow
- **功能**: 按键映射配置窗口
- **特性**: 模态对话框，支持实时按键映射编辑

## 6. 事件与消息传递

### 6.1 事件驱动架构

```csharp
// 设备连接状态变更事件
_deviceService.ConnectionLost += () => {
    // 更新UI状态
};

// 日志追加事件
_logService.LogAppended += text => {
    // 更新日志显示
};

// 脚本运行状态变更事件
_scriptService.IsRunningChanged += running => {
    // 更新运行状态UI
};
```

### 6.2 线程调度

- UI线程操作通过 `Dispatcher.UIThread.Post()` 调度
- 后台任务使用 `Task.Run()` 执行
- 定时器使用 `System.Timers.Timer` 实现

## 7. 依赖注入与配置

### 7.1 服务注册

```csharp
// 在App.axaml.cs中注册服务
// Services are registered and resolved in the application setup
```

### 7.2 配置管理

- 应用配置存储在 `EasyCon.Core/Config.cs`
- 支持多种配置源（文件、注册表、环境变量）

## 8. 扩展性设计

### 8.1 插件化架构
- 模块间通过接口通信，支持模块替换
- 脚本引擎支持多种脚本语言
- 设备驱动支持多种通信方式

### 8.2 跨平台支持
- UI层使用Avalonia实现跨平台
- 核心逻辑与平台无关
- 设备驱动抽象层支持不同平台

## 9. 性能优化

### 9.1 日志系统优化
- 使用StringBuilder批量合并日志
- 限制最大日志长度（100,000字符）
- 定期清理过期日志

### 9.2 图像处理优化
- 使用OpenCV的高效图像处理算法
- 支持图像缓存和重用
- 异步图像采集处理

### 9.3 内存管理
- 及时释放设备资源
- 使用弱引用避免内存泄漏
- 定期垃圾回收优化

## 10. 错误处理与容错

### 10.1 异常处理策略
- 服务层捕获并转换异常
- 用户友好的错误消息显示
- 详细的日志记录

### 10.2 连接容错
- 自动重连机制
- 连接状态监控
- 优雅降级处理

## 11. 安全性考虑

### 11.1 输入验证
- 文件路径验证
- 脚本内容安全检查
- 设备连接参数验证

### 11.2 权限管理
- 设备访问权限控制
- 文件系统操作权限
- 网络通信权限

## 12. 测试策略

### 12.1 单元测试
- 每个模块的独立测试
- 模拟接口测试
- 边界条件测试

### 12.2 集成测试
- 模块间协作测试
- 端到端功能测试
- 性能测试

## 13. 部署与发布

### 13.1 构建配置
- Debug配置：包含诊断工具
- Release配置：优化性能，移除调试信息

### 13.2 发布策略
- 多平台二进制发布
- 安装包制作
- 自动更新机制

## 14. 未来发展方向

### 14.1 功能扩展
- 支持更多游戏主机平台
- 增强图像识别能力
- 云端脚本共享平台

### 14.2 技术升级
- 迁移到.NET最新版本
- 采用更现代的UI框架特性
- 优化性能和内存使用

---

**文档版本**: 1.0  
**最后更新**: 2026年4月16日  
**维护者**: EasyCon2开发团队