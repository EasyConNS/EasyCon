## models

通用自动化虚拟机平台插件模块，用于实现ffi

**模块系统已实现** ✅

### 模块架构

不同模块提供的函数都应通过平台系统调用指令执行，调用编号各个模块应唯一不可冲突

对兼容的字节码文件调用未实现模块功能须有处理机制

### 已实现模块

#### 1. device模块 (required)
- **功能**: 实现单片机手柄操作和设备通信
- **实现**: `EasyCon.Device` 项目
- **接口**: `IDeviceService`, `NintendoSwitch`
- **特性**:
  - 串口/蓝牙/网络连接
  - 自动设备发现
  - 协议抽象层
  - 状态监控和重连

#### 2. capture模块 (required)
- **功能**: 实现图像识别和视频采集
- **实现**: `EasyCon.Capture` 项目
- **接口**: `ICaptureService`, `OpenCVCapture`
- **特性**:
  - 模板匹配算法
  - OCR文字识别
  - 多视频源支持
  - 性能优化缓存

#### 3. script模块 (required)
- **功能**: 脚本语言解析和执行
- **实现**: `EasyCon.Script` 项目
- **接口**: `IOutputAdapter`, `ICGamePad`, `Compilation`, `Evaluator`
- **特性**:
  - SourceText→Lexer→Parser→Binder→Evaluator 编译管线
  - 绑定器进行类型检查和作用域分析
  - 单片机字节码生成（Assembler）
  - 控制流图和寄存器分配优化

#### 4. vpad模块 (optional)
- **功能**: 虚拟手柄UI和按键映射
- **实现**: `EasyCon.VPad` 项目
- **接口**: `IControllerAdapter`, `KeyMappingManager`
- **特性**:
  - 可视化手柄UI
  - 键盘映射配置
  - 物理手柄支持
  - 配置文件管理

### 规划中模块

#### sysbot模块 (optional)
- **功能**: 连接sys-botbase/noxos等后端实现内存操作
- **状态**: 规划中
- **应用**: 游戏内存修改、调试辅助

#### 扩展模块系统
- **FFI接口**: 外部函数调用接口
- **插件系统**: 第三方模块支持
- **脚本API**: 脚本级别的模块访问

### 模块调用规范

#### 系统调用指令
```assembly
srv, <module_id>, <function_id>, [args]
```

#### 调用编号分配
- **0x00-0x0F**: 核心系统功能
- **0x10-0x1F**: device模块
- **0x20-0x2F**: capture模块
- **0x30-0x3F**: script模块
- **0x40-0x4F**: vpad模块
- **0x50-0xFF**: 保留给扩展模块

> 不同平台(PC, MCU)应根据自身平台特点选择实现部分模块