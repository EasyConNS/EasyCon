# 视频预览链路与 WebRTC 调研

日期：2026-10-24。当前落地的是本地 HTTP MJPEG 监视流；WebRTC 为后续候选，尚未集成。

## 当前实现

```text
ccap 默认流，实际帧 1920×1080（本次 Air 相机）
    ↓ 原生采集线程，RGB8 规范化
VideoSession：不可变帧，watch 只保留最新一张
    ├─ Capture / IR / 图像识别：完整原始帧
    └─ Web 预览：按需、目标 24 fps、最长 640×360
          ↓ 借用原始像素，不复制整张 1080p 图像
       缩放 → JPEG → watch 最新预览
          ↓ 一次编码、多观众；慢读者跳过旧帧
       GET /api/video/stream，单连接 multipart/x-mixed-replace
          ↓ 浏览器原生图像流解码
       卡片 ↔ 放大监视窗口，同一个 img 和同一条流
```

预览只做旁路显示，不改变原语的图像格式、ROI、识别精度或采集元数据。没有观众时跳过缩放和编码，但保持物理采集、原始帧发布与设备错误检测。网页隐藏时移除视频流，重新可见时恢复；设备连接仍由宿主管理。

编码使用现有 image 库，保留缩小时的面积平均，避免给小窗引入明显混叠。处理节拍使用 interval + Skip，处理落后时跳过节拍，不在编码后再附加固定延迟。网络发送受背压约束时 watch 只保留最新预览，不创建逐观众历史帧队列；HTTP／浏览器自身缓冲仍存在，不能据此承诺端到端延迟上限。

流会在设备断开、读帧错误或宿主取消时结束；关闭宿主会唤醒流并允许 HTTP 优雅退出。相同 Host／Origin／Fetch Metadata 限制仍然适用，服务只绑定回环地址。没有音频播放、录制或外部媒体服务器。

## Air 相机实测

macOS arm64，ccap 生产后端，实际源帧 1920×1080。每组网络抽样约 12 秒。CPU 是宿主进程消耗的 CPU 时间／墙钟时间，100% 表示一个核心，不是整机占比。

| 链路 | 宿主 CPU | 预览交付帧率 | 帧间隔 P95 | 进程 RSS |
|---|---:|---:|---:|---:|
| 原实现 Debug，图片轮询 | 118.4% | 2.65 fps | 未记录 | 236.1 MiB |
| 同一原实现 Release，图片轮询 | 35.1% | 8.15 fps | 128.9 ms | 214.0 MiB |
| 新实现 Release，MJPEG | 35.6% | 23.99 fps | 43.6 ms | 207.5 MiB |

新版 12 秒收到 288 个有效 JPEG 分片，X-Frame-Id 无重复，平均 JPEG 约 20,976 字节。新旧 Release 的 CPU 基本相同，新版交付帧率约为原来的 2.9 倍；相对最初运行的 Debug 宿主，总负载下降约 70%，其中 Release 优化贡献很大，不能全归因于流协议。

Debug 采集原语基线：不运行预览，180 帧约 7.37 秒（包括打开和关闭），进程平均 CPU 约 48.4%。采样分别定位到 RGB 规范化、缩放和 JPEG 编码；仅更换 HTTP 传输不能消除这些 CPU 工作。

本机内置浏览器已验收真实 Air 画面、640×360 解码和放大窗口。浏览器自动化检查了 Esc 返回、放大时不增加流请求、隐藏页面暂停、设备断开关闭窗口，以及桌面／窄屏布局。13 项 Web 宿主测试覆盖多观众、慢读者、关闭、无人观看时停止编码和错误传播。

上述 fps 是网络收到预览帧的节拍，不是硬件公布的 FPS，也不是显示器呈现测量；设备未报告 FPS 时界面仍显示“帧率未知”。没有测量传感器到屏幕的完整延迟、温度、功耗或长时间热稳态。Windows、Linux 和实体采集卡尚未实机验收。

## 方案比较

| 方案 | 编码与显示 | 本项目的取舍 |
|---|---|---|
| HTTP MJPEG | 每帧 JPEG，浏览器通过 multipart 图像流显示 | 已实现；复用现有依赖，旁路接入 ccap，便于定位问题。仍有 CPU 缩放／JPEG 开销，高清或高帧率时需要继续评估 |
| 硬件 H.264 + WebRTC | 原生编码，RTP／RTCP、ICE、DTLS／SRTP、信令，浏览器媒体轨道 | 推荐作为下一阶段试验；需要验证实际硬件编码器、原生运行库分发和浏览器协商，不能只新增一个 WebRTC crate 就认为编码已解决 |
| 硬件 H.264 + WebCodecs | 原生编码，自定义传输与时间戳，浏览器 VideoDecoder + Canvas | 可用于受控浏览器环境；仍需处理配置、关键帧、背压、时序、资源释放和支持检测，不是无需编码器的捷径 |

WHATWG 定义了 multipart 的流式浏览器处理；WebCodecs 的官方文档说明它提供编解码接口，不提供完整的传输、封装或播放系统。WebRTC 则需要媒体轨道协商及相应编解码格式。这里的取舍是项目建议，性能结论必须由实际完整链路测量确认。

## 后续 WebRTC 最小验证路线

优先验证 GStreamer + Rust 绑定：将 ccap 现有帧送入 appsrc，不引入第二个相机采集后端。单独的预览编码工作线程拥有媒体管线；原始帧 watch 与 IR 保持现有关系。最初只做本地单观众、无音频，不开放公网服务。

```text
ccap → 原始帧订阅 → appsrc
    → 格式转换／缩放 → 平台硬件 H.264 编码
    → WebRTC 发送／本地信令
    → RTCPeerConnection → video.srcObject
```

GStreamer webrtcsink 包含信令、拥塞控制和 WebRTC 管线，支持原始或已编码视频输入；需随产品提供 GStreamer 运行时和所用插件。本机未安装 GStreamer，这轮没有安装，也没有声称已跑通 WebRTC。官方文档说明默认的多观众管线不共享编码，后续若需要多窗口，必须验证共享已编码帧或使用更底层 webrtcbin，不能直接照搬当前“一次编码”结论。

macOS 可先验证 VideoToolbox H.264 硬件编码，GStreamer 提供 vtenc_h264_hw、realtime 与禁止帧重排等相关属性。Windows／Linux 需要分别按机器能力验证硬件编码与像素转换，不承诺通用硬件编码器一定可用，也不做静默软件回退。

本机已用现有 FFmpeg 完成一个独立能力探针：1280×720、30 fps、90 张合成图案，h264_videotoolbox、allow_sw=0、realtime=1、bf=0，编码到空输出成功。它只证明本机硬件编码路径可调用，不是 ccap → WebRTC 的端到端测试；项目没有因此新增 FFmpeg 运行依赖。

验收应同时记录：同分辨率与同交付帧率下的宿主／浏览器 CPU、码率、首帧时间、帧间隔分布、完整画面延迟、慢客户端丢帧、关键帧请求、连续重连、退出与多观众资源释放。先证明 720p30 的完整收益，再考虑取代当前 MJPEG，不同时维护两个默认预览传输。

## 一手资料

- [WHATWG multipart/x-mixed-replace](https://html.spec.whatwg.org/multipage/iana.html#multipart/x-mixed-replace)
- [WHATWG JPEG-push 与媒体时序](https://html.spec.whatwg.org/multipage/media.html)
- [GStreamer rswebrtc，功能与默认多观众编码边界](https://gstreamer.freedesktop.org/documentation/rswebrtc/index.html)
- [GStreamer webrtcsink，输入类型与编码控制](https://gstreamer.freedesktop.org/documentation/rswebrtc/webrtcsink.html)
- [GStreamer VideoToolbox H.264 硬件编码](https://gstreamer.freedesktop.org/documentation/applemedia/vtenc_h264_hw.html)
- [Mozilla WebRTC 编解码格式](https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Formats/WebRTC_codecs)
- [Mozilla WebCodecs 与支持检测](https://developer.mozilla.org/en-US/docs/Web/API/WebCodecs_API)
