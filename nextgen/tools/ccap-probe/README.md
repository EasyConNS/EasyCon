# ccap-rs 独立验收

本工具直接依赖 crates.io 官方 `ccap-rs = 1.6.0`，使用默认 `build-source` 特性编译发布包内的原生实现；没有依赖补丁、源码修改或 vendor。它是独立 Cargo workspace，拥有自己的锁文件。生产核心、CLI、工作流宿主与 Web 现统一使用 ccap，已移除 nokhwa；本工具保留原始依赖行为探针，并通过路径依赖 `easycon-sense` 回归生产 `VideoSession::open_default`，不再保留测试专用的视频适配器。

所有 Provider 操作在同一个线程顺序执行。每轮新建 Provider，打开指定设备、配置、开始、取帧，最后显式停止并销毁 Provider；失败也执行停止和销毁。最后一帧在 Provider 销毁后再次读取，验证数据所有权。每次取帧有原生超时，设备打开／停止／销毁不具备强制中断期限。

## 运行

在本目录执行，需要 Rust、平台 C++17 工具链和供 bindgen 使用的 libclang。macOS 构建复用 `nextgen/Info.plist` 声明相机用途。

```sh
cargo build --locked
cargo test --locked
cargo clippy --locked --all-targets -- -D warnings
cargo run --locked -- list
cargo run --locked -- capture --device "OBS Virtual Camera" --format bgra32 --frames 30 --cycles 3
cargo run --locked -- capture --device "OBS Virtual Camera" --format rgb24 --frames 30 --cycles 3
# OBS 作为采集卡替身：显式运行真实硬件回归；默认 cargo test 不打开设备。
cargo test --locked obs_capture_card_contract -- --ignored --nocapture --test-threads=1
# 另验收真实 OpenCV 5 模板匹配；指定包含项目桥接及原生库的目录。
EASYCON_OPENCV_DIR=/absolute/path/to/opencv-runtime \
  cargo test --locked obs_opencv_capture_card_match -- --ignored --nocapture --test-threads=1
```

`--device` 使用枚举返回的完整名称。打开后校验 `device_info().name` 完全一致，避免原生后端找不到名称时回退到内置相机。硬件测试默认使用 OBS Virtual Camera，可通过 `CCAP_TEST_DEVICE` 显式选择其他名称。`Provider::get_devices()` 会短暂打开每个设备查询格式，并非严格的无打开枚举；返回序号也不是可持久化设备标识。

按需增加 `--width 640 --height 480 --fps 30`。指定尺寸时，实际帧尺寸不符立即失败。默认保留设备的实际尺寸；日志分别记录属性报告值与实际帧信息。

默认不保存图像。使用 `--output captures/frame.ppm` 可保存最后一帧，目录需预先存在，文件已存在时拒绝覆盖。仅支持紧密 RGB24 或 BGRA32 的标准 RGB 归一化，处理行对齐和上下翻转；这是验收程序的数据读取，不修改依赖。`captures/` 和构建产物不加入版本控制。

## 2026-10-24 初始独立探针验收结果

环境：macOS / Apple Silicon（aarch64），官方 `ccap-rs 1.6.0`。用户确认实体采集卡尚未接入，本轮只测试现有内置相机和 OBS Virtual Camera。

| 检查 | 结果 | 证据 |
|---|---|---|
| 枚举设备及格式 | 通过 | 发现 MacBook Air 的相机、OBS Virtual Camera；均包含 BGRA32 |
| OBS BGRA32 | 通过 | 3 轮重建 Provider，每轮 30 帧；实际 1920×1080、stride 7680 |
| 内置相机 BGRA32，保持默认尺寸 | 通过 | 3 轮 × 30 帧；实际 1920×1080 |
| 内置相机 RGB24 | 通过 | 3 轮 × 30 帧；实际 1920×1080、stride 5760 |
| 帧序号、时间戳 | 通过 | 所有成功采集轮次严格递增；允许丢弃中间帧 |
| 停止、销毁与重新打开 | 通过 | 成功的 9 轮均显式停止、销毁并验证停止状态；保存的帧在销毁后可读 |
| OBS RGB24 重连 | 未通过，重复复现 | 第一轮 30 帧通过，第二轮第 1 帧返回 BGRA32、index=0；程序返回非零 |
| 内置相机设置 640×480 | 未通过 | 属性报告 640×480，实际首帧仍为 1920×1080；程序返回非零 |
| 图像检查 | 通过 | 保存 OBS BGRA 帧，转换为 RGB 后显示正常的 OBS 虚拟相机未启动占位图；并非游戏画面 |
| 探针自身检查 | 通过 | 2 项离线单元测试、rustfmt、严格 Clippy、构建 |

BGRA 帧的 `size_in_bytes` 为 8,294,464，比 `1920 * 1080 * 4` 多 64 字节；探针按 plane 的 stride 和 height 读取有效行，不将尾部存储当作像素。

本地原始日志位于 `captures/obs-bgra.log`、`camera-bgra-final.log`、`camera-rgb.log`、`obs-rgb-diagnostic.log`、`camera-resolution.log`。预览是 `captures/obs-bgra.png`；内置相机画面没有落盘。调试构建包含逐帧数据校验，这些耗时不作为采集吞吐基准。

## OBS 作为采集卡替身的契约回归

测试位于 `src/regression.rs`，直接使用生产 `VideoSession::open_default("id:OBS Virtual Camera", clock)`。核心 `src/video/ccap.rs` 在专用采集线程构建、使用、停止并销毁官方 Provider；按实帧的 stride、方向、通道顺序转换为紧密 RGB8，保留首帧，不修改依赖或加入重试。生产遵循 ccap 的平台默认 packed 输出（本机 BGRA32），避免打开后更改输出格式造成启动队列混入旧格式；同时支持 RGB24／BGR24／RGBA32 的 RGB8 标准化。

生产以首帧确定尺寸和编码，`frames_per_second=0` 表示 ccap 未提供整数帧率（也包含分数帧率）。初始试验适配器曾估计交付帧率，该估计已删除；未知帧率不再伪装为正整数设备 FPS。Web 显示“帧率未知”。`video-formats` 仅探测实帧确认的默认格式；显式格式参数只校验默认流约束，不承诺切换模式。

2026-10-24，本机官方 `ccap-rs 1.6.0` 的最终回归：

| 契约 | 结果 |
|---|---|
| 精确选择 OBS、BGRA32 → RGB8 | 通过；实际 1920×1080，RGB 长度 6,220,800 字节 |
| 每次重连读取 30 张新帧 | 通过；3 个独立 Session，共 90 张，序号、源及接收时间戳前进 |
| 多读者、单读者取消 | 通过；强制挂起读取后取消，其他订阅继续得到新帧 |
| 原子原语 | 通过；Capture、RegionStatistics 消费真实 OBS 帧并保留来源 |
| IR 与组合子 | 通过；JSON 往返、静态校验、Timeout → Pipe(Capture → image_from_frame)，4 个节点成功并产生 trace |
| 真正触发 IR 超时 | 通过；等待不可达的新帧游标，20 ms Timeout 取消 Capture、完成运行作用域清理；物理源仍可读取 |
| 停止与所有权 | 通过；两轮 stop 幂等，一轮仅 Drop；生产工作线程停止并 join，旧读者返回 Stopped；同线程生命周期由会话单元测试覆盖 |
| 重连隔离、保留帧 | 通过；旧 Session 游标返回 SourceChanged；保留帧在后续采集及销毁后内容不变 |
| 真实 OpenCV 5.0.0 | 通过；从 OBS 帧中央裁取 192×192 模板，MatchTemplate 原语在全帧搜索，命中 (864,444)，置信度 0.99999984 |
| 原始 RGB24 重连 | 仍未通过；再次在第 2 轮首帧收到 BGRA32，严格校验返回非零 |
| OBS 指定 640×480 | 未通过严格尺寸校验；属性报告 640×480，实帧 1920×1080，返回非零，不据此认定 OBS 支持该模式 |
| 离线与核心回归 | 通过；探针 3 项离线测试、2 项显式硬件测试、主工作区全 feature 的 220 项测试（含 Web、设备与集成测试）；rustfmt、严格 Clippy 通过 |

生产回归停止并 join 耗时 27–37 ms，读者取消低于 1 ms；这些仅为本次样本。OpenCV 使用已有本机验收包 `.deps/opencv-dnn-runtime`，通过 `NativeMatcher` 原生工作线程执行并显式 shutdown。图像为 OBS 未启动时的占位图；模板从同一实帧裁取，用来校验像素、坐标、来源和真实后端调用，不代表游戏目标识别率。

硬件测试在外部 45 秒进程期限下执行，生产回归原始结果保存在本机 `captures/obs-production-ccap.log`；初始测试适配器结果保留为 `captures/obs-contract.log`；原始失败检查分别保存为 `captures/obs-rgb-regression.log`、`captures/obs-resolution-regression.log`，不能用 BGRA 适配结果替代它们。默认测试不会打开相机；核心库的两项原生 OpenCV 专项测试未在本轮重复运行。

## 上游行为与接入判断

OBS 重连的格式错误已通过同一路径重复复现，未丢弃异常帧或增加重试掩盖错误。官方发布源码的相关路径是：

- Rust `src/provider.rs::open_device_with_extra_info(Some(name), false)` 创建带名称的 Provider，再调用停止；底层 `native/src/ccap_core.cpp::Provider(deviceName)` 内部执行 `open(deviceName)`，默认自动启动。
- `native/src/ccap_imp_apple.mm::stop` 停止 AVCaptureSession，但不清空 `m_availableFrames`；`native/src/ccap_imp.cpp::grab` 优先返回队列中的旧帧。这解释了配置 RGB24 后首帧仍可能为之前的 BGRA32。
- `native/src/ccap_imp_apple.mm::openCamera` 在打开时把 `m_frameProp.width/height` 传入 ObjC 相机会话；之后 `ProviderImp::set(Width/Height)` 只修改 C++ 属性，`ProviderApple::start` 沿用 ObjC 先前的 `_resolution`，未把新属性重新传入。Rust 按名称打开又会重建 Provider，打开前配置也无法保留到该路径。属性报告值不能证明实际分辨率已切换。
- 更正此前推断：`m_propertyChanged` 在发布源码中只有定义和赋值，没有读取；“重复赋值清掉标记导致分辨率失效”没有依据，不再作为原因。OBS 是否支持指定物理模式也应另行验证，不能将 OBS 仅输出 1080p 当作支持任意模式。

结论：生产已统一为 ccap，未保留其他视频后端或切换开关。OBS 的真实帧通过采集、原语、IR 和 OpenCV 匹配回归；Web 页面也已实测连接、预览、断开、重新连接，断开后帧 API 返回 204，连接后返回有效 JPEG。上游原始 RGB24 重连和尺寸属性问题仍保留失败记录；生产使用默认模式和实帧验证，不宣称这些依赖问题已修复。

尚未验收：实体采集卡、Windows/Linux 实机、热拔插、权限拒绝、读帧超时故障注入、长时间采集及性能。原生依赖保持未修改，以上缺陷不宣称已经修复。
