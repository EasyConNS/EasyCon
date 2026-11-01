# 原生工作托管

`native_work::NativeWorker<T>` 在专用线程内创建、复用和销毁原生状态。当前优先覆盖模板匹配与 OCR：`vision::worker::NativeMatcher` 和 `NativeOcr` 实现已有能力接口，可直接借用到 `PrimitiveHost` 或 `RunBindings`，IR 格式和原语参数不变。可编译的宿主装配示例见 [API 文档](../src/vision/worker.rs)。

## 宿主装配

1. 上层编排选择本工作流使用的模型、字典、运行库和配置。
2. 宿主调用 `NativeOcr::open(config, &resource_lifetime).await`，或 `NativeMatcher::open_opencv(directory, &resource_lifetime).await`。初始化和库加载也发生在工作线程。自定义实现用 `spawn(factory, token)` 注入。
3. 把实例以所选名称借用到 `RunBindings.ocr`／`matchers`。IR 的 SourceRef 只引用该绑定名；输入图像和模板通过运行参数传入。
4. 等待 `ValidatedDocument::execute(..., &run_stop).await` 返回，再读取 outcome 和清理报告。需要复用时可将同一提供者借给下一次运行。
5. 宿主释放提供者时显式 `shutdown().await`，等待原生状态销毁和线程 join。入口参数校验失败也必须收尾，先保存 execute 结果，再 shutdown，最后传播错误。

资源生命周期 token 与单次运行 token 应分别持有。取消运行只取消这次请求，提供者仍可复用；取消资源生命周期会拒绝后续请求，活动请求在异步等待点获得取消通知，随后由所有者 shutdown。工作线程空闲时不会仅因 token 取消而自动销毁状态。

固定配置表示**单个提供者实例加载后保持固定**，不表示不同工作流必须共用模型。核心不下载模型、不维护强制资产注册表，也不要求内容摘要。上层需要版本验证、缓存或重放时，可自行记录资源摘要；文件路径只作诊断身份。

## 线程、队列与取消

| 契约 | 当前行为 |
|---|---|
| 原生状态 | 在同一专用线程创建、调用、销毁；T 无须 Send，不使用 unsafe Send |
| 跨线程数据 | 工厂、任务闭包、输入和输出必须 Send；图像共享不可变像素缓冲 |
| 容量 | 每个实例最多一个执行中任务和一个排队任务；排满时异步等待，不无限积压 |
| 请求取消 | 入队前取消不执行；入队后通知并等待实际结果，不分离工作 |
| 丢弃请求 future | 取消该请求；所有者继续持有线程，未开始的取消任务跳过 |
| 正常关闭 | 停止接收、通知取消、等待任务及状态销毁、join |
| Drop 兜底 | 同步取消并 join，可能阻塞；异步宿主应显式 shutdown |
| Rust panic | 工厂／任务／状态析构中的 panic 使线程失效；识别适配映射为 InternalError／RunFatal |

通用 NativeWorker 保留已执行操作的实际返回值，避免将可能已经交付的副作用隐藏为取消。Pure 匹配／OCR 适配则在返回时检查取消，丢弃过期结果。模型／识别错误保留 typed VisionError，可恢复；工作线程失效不能被 Catch／Fallback／Retry 吞掉。进程 abort、原生崩溃和卡死驱动不在 Rust panic 捕获范围内。

OpenCV 匹配／DNN forward 和 Tesseract 仍不可被强制抢占。托管使调度线程能处理定时器和取消；Timeout 到期后仍需等待原生调用完成，不能把通知取消当作已经停止。

原语目录将匹配／OCR 标为 `Scheduling::Provider`，调用异步入口。直接注入同步后端时，默认异步方法仍在调用线程执行；使用 NativeMatcher／NativeOcr 才会切到工作线程。同步 API 保留兼容，会阻塞调用者。

当前 IR 的运行资源锁仍跨越提供者调用持有，单次运行中的能力访问串行；Sleep 和控制调度可继续推进。独立工作线程的直接异步调用可以同时进行；本轮不引入线程池，也不改动 IR 的资源锁粒度。

## 验证

[工作线程单元测试](../src/native_work/tests.rs) 覆盖非 Send 状态、线程内析构、有界排队、取消／丢弃、初始化中取消、资源关闭、panic 失效和 join。[集成测试](../tests/native_work.rs) 覆盖 IR Timeout／Race 收拢、RunFatal 绕过恢复和同步 API 兼容。真实 OpenCV 匹配、Tesseract 与工作流指定 PP-OCR v6 的复用／关闭验收见 [记录](../vision/ACCEPTANCE.md)。
