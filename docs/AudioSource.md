# 游戏音频源

EasyCon 的 Avalonia 主窗口现在可以独立连接 Windows WASAPI 录音输入。音频源与视频源、单片机和虚拟手柄分别管理：选择设备后点击“连接音频源”，收到第一块数据才显示“已连接”；设备移除、采集停止或 15 秒内没有首包会显示连接失败。

## 当前范围

- 枚举当前可用的 Windows 录音输入端点，不自动选择默认麦克风。
- 使用 NAudio `WasapiCapture` 读取共享模式 PCM 数据。
- 发布采样格式、连续 PCM 数据块、峰值/RMS 电平和连接状态。
- 在主窗口的经典布局和卡片布局中显示设备、格式、连接状态及峰值 dBFS。
- 应用退出或主窗口关闭时停止采集并释放 WASAPI 资源。

本次只加入音频源和电平展示，不包含音频判闪、频谱分析、样本匹配或任何自动化决策。后续消费者可以订阅 `MainWindowViewModel.Audio.CaptureSource.DataAvailable`，自行处理 `AudioChunk`；音频采集不会改变现有 ECS 脚本的等待、按键或图像判定行为。

## 数据模型

`EasyCon.Capture.Audio.IAudioCaptureSource` 提供以下接口：

- `GetAvailableDevices()` 返回 `AudioDeviceInfo`（稳定端点 ID、名称和 `wasapi` 后端标识）。
- `TryConnect(deviceId)` 开始连接；状态先变为 `Connecting`，首个非空数据块到达后变为 `Connected`。
- `DisconnectAsync()` 停止当前采集并回到 `Disconnected`。
- `DisconnectIfConnectingAsync()` 只在仍等待首包时停止采集，用于避免首包与超时同时到达时误断开已连接的源。
- `StateChanged` 通知 `AudioCaptureState`；失败时包含错误文本。
- `DataAvailable` 通知 `AudioChunk`。`Pcm` 是设备实际格式的交错小端字节，不重采样、不自动增益、不降噪。
- `LevelChanged` 通知 `AudioLevel`。界面按峰值计算 dBFS，显示范围下限为 -60 dBFS；静音表示仍有数据到达，并不表示断流。

当前支持 PCM 8/16/24/32 bit 和 IEEE float 32 bit。音频判闪需要统一的 float32 数据、窗口边界和缺包语义，待后续方案确定后再在独立变更中加入。

## 平台限制

WASAPI 仅在 Windows 上启用；其它平台的设备列表为空，连接会进入失败状态。设备必须作为 Windows 录音端点可见；只通过特殊 DirectShow 采集图暴露的音频不会出现在列表中。Windows 麦克风隐私权限或设备被其它程序独占也可能导致连接失败。

## 验证

在 Windows 上连接采集卡音频输入，确认面板显示设备名、实际采样率/声道/位深和实时 dBFS。拔出设备或停止输入后，确认状态变为失败而不是静音。自动化测试覆盖 PCM 电平计算、空/非法格式、设备选择和连接状态事件；真实采集卡和不同驱动格式仍需硬件验收。
