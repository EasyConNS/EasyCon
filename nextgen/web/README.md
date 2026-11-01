# EasyCon Vue 3 设备控制台

浅色单页控制台，Vue 3 + TypeScript + Vite，普通 CSS。Rust 的 `web-host/` 通过 Axum 提供同源页面与本地设备 API，复用现有 device、ccap、CPAL、NativeWorker 和项目 IR 预检，不调用 .NET 或 Python Flow。

## 启动

需要 Node.js 22.12+ 和现有 Rust 原生构建环境。从仓库根目录执行：

```sh
cd nextgen/web
npm ci
npm run build
cd ..
cargo run --locked --release -p easycon-web -- --directory examples
```

浏览器打开 `http://127.0.0.1:19392`。ccap 枚举会短暂打开设备，首次枚举／连接视频或连接音频时，系统可能要求相机／麦克风权限；macOS 宿主已嵌入用途说明。退出宿主用 Ctrl+C。

开发时可另外在 `nextgen/web` 执行 `npm run dev`。Vite 的 `/api` 代理转发至 `127.0.0.1:19392`；Rust 宿主仍需运行。可以通过 `--port` 改端口，开发代理目标需同步修改。发布时携带 `web/dist/`，用 `--assets <静态文件目录>` 指定路径。

## 使用

1. “选择文件”打开页面内的本地文件选择器，可进入文件夹、返回上级，或输入目录路径。选择 JSON 工作流后由 `PreparedProject::load` 校验 IR、配置及资源，显示真实绝对路径。这里读取宿主机器的文件，不上传工作流，也不使用浏览器的 `C:\fakepath`。
2. 相邻 `config.json` 的 `video`、`gamepad` 为尚未连接的卡片预选设备；不修改配置文件，不自动连接。
3. 三张卡片独立刷新枚举列表、选择、连接、断开。串口仅传端口名，由 device 自动探查 EasyCon 115200／9600、Pokeldn 921600。视频只使用 ccap 的设备默认流，按首张实际帧确认尺寸；不提供后端切换，帧率未报告时显示“帧率未知”。音频使用输入设备默认格式。
4. 视频通过一条 HTTP MJPEG 流显示真实采集帧，浏览器直接解码，无逐帧 fetch、Blob 或对象 URL。预览保持比例、不放大，最长 640 × 360，以 24 fps 为处理目标；实际帧率取决于设备与编码耗时。点击“监视窗口”放大同一个画面，Esc 返回，不新增一条流。该缩放仅作用于预览，原语／IR 仍使用完整原始帧。
5. 音频显示 50 ms 连续窗口的整体 RMS，转换为 `20 * log10(RMS)` dBFS，色柱按 −60…0 dBFS 显示，绿色至黄色、红色；静音显示 `−∞`。不播放、录制音频。

当前界面只选择／预检工作流和管理设备，执行工作流仍使用 `easycon-run`。页面刷新不关闭宿主持有的设备，再次打开会读回连接状态；关闭宿主会取消监视器并等待原生工作线程销毁资源，手柄断开沿用已有的中立复位语义。多个页面共享同一宿主状态和设备。

多个观众共享一次缩放和编码，每个观众只读取最新帧，慢客户端不积累应用帧队列。没有视频流／快照请求时停止预览编码，采集和设备健康检查继续。页面隐藏时关闭视频流，恢复可见时重新连接；不会断开物理设备。实时使用请按上面的 Release 命令启动，未优化的 Debug 像素循环会显著增加 CPU 占用。实测与后续 WebRTC 方案见 [视频预览链路与 WebRTC 调研](VIDEO.md)。

## 边界与 API

宿主只绑定 IPv4 回环地址，校验 Host、Origin 和 Fetch Metadata，不开启跨域访问。必须使用启动时打印的 `127.0.0.1` 地址。文件选择器可浏览当前用户有权限访问的本机目录，适用于本地可信用户，不应经反向代理暴露到公网。

| API | 用途 |
|---|---|
| `GET /api/state` | 三个设备状态、整体 RMS 和 dBFS，前端约每 100 ms 更新 |
| `GET /api/devices/{gamepad,video,audio}` | 独立枚举相应设备 |
| `POST /api/connections/{kind}` + `{"id":"…"}` | 连接，原生资源在独立托管线程创建 |
| `DELETE /api/connections/{kind}` | 取消监视、释放设备并等待线程结束 |
| `GET /api/video/stream` | 持续 `multipart/x-mixed-replace` MJPEG 流，分片含 Content-Length 和 X-Frame-Id；未连接时返回 204；断开／关闭时结束 |
| `GET /api/video/frame` | 诊断用下一张预览 JPEG，最多等待 1 秒；未连接／超时返回 204 |
| `GET /api/files?path=…` | 列出目录和 JSON 文件；省略 path 使用 `--directory` |
| `POST /api/workflow` + `{"path":"…"}` | 校验工作流，返回绝对路径和设备配置 |

读取／编码失败会使对应卡片进入异常状态，不自动重新连接。采集驱动中的阻塞打开、读帧和停止仍受现有后端限制，不能承诺硬停止期限；音频窗口出现丢帧缺口时按既有契约报告异常，需要断开后重新连接。

## 验证

```sh
# nextgen/web
npm run build
npx playwright install chromium
npm test

# nextgen
cargo test --locked -p easycon-web
cargo clippy --locked --workspace --all-targets --all-features -- -D warnings
```

浏览器测试在测试文件中拦截 API，验证工作流路径、三种连接／断开、流式预览、监视窗口复用与 Esc 退出、页面隐藏暂停、音量更新、错误恢复、服务离线以及 1440／375 像素布局；应用代码没有模拟设备。截图输出到 `test-results/`。Rust 测试覆盖真实 API 路由、请求来源隔离、文件选择预检、设备操作互斥、预览 JPEG 编码、流多读者／慢读者／退出、无人观看时停编码与保留健康检查，以及 RMS 电平转换。真实实体采集卡／串口的持续采集、热插拔及首次权限弹窗需使用硬件验收。
