using EasyCon.Capture;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Hosting;
using EasyCon.Core.Logging;
using EasyCon.Core.Script;
using EasyDevice;
using EasyScript;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace EasyCon.Core.Flow;

/// <summary>
/// Flow 服务状态机：设备（视频源/单片机）的连接管理 + 编排图运行管理 + 单节点试跑。
/// 前端（Python 画布）与外部 agent 经 HTTP/MCP 访问本状态；设备状态由 EasyCon 单一持有。
/// 每次运行/试跑的能力集经 <see cref="ScriptHostAssembler"/> 唯一装配点组装，并按次释放。
/// </summary>
public sealed class FlowServiceState : IDisposable
{
    private readonly NintendoSwitch _ns = new();
    private readonly object _gate = new();
    private OpenCVCapture? _capture;
    private FrameProducer? _producer;
    private IPadInput? _pad;
    private bool _mockMcu;
    private readonly ConcurrentDictionary<string, FlowRunHandle> _runs = new();
    private int _runSeq;
    private int _disposed;

    /// <summary>服务侧 OCR 后端（none | tesseract | ppocr）；跨运行复用同一引擎实例。</summary>
    public string OcrBackend { get; private set; } = "none";

    /// <summary>PP-OCR 模型/字符集目录（<see cref="OcrBackend"/> = ppocr 时使用）。</summary>
    public string? OcrModelDir { get; private set; }

    private IOcrService? _ocr;
    private string? _ocrError;

    // ---- 节点目录（前端画布渲染的单一事实源）----

    /// <summary>节点目录 JSON（结构见 <see cref="FlowNodeCatalog.Payload"/>）。</summary>
    public static string NodeCatalogJson() => FlowNodeCatalog.Json();

    // ---- 视频源 ----

    public bool VideoConnected { get; private set; }

    public object VideoInfo() => new
    {
        connected = VideoConnected,
        sources = EasyCon.Capture.ECCapture.GetCaptureCamera()
            .Select(c => new { index = c.index, name = c.name }).ToArray(),
    };

    /// <summary>连接视频源；返回错误消息（null = 成功）。</summary>
    public string? ConnectVideo(int index, int api = 0)
    {
        lock (_gate)
        {
            if (VideoConnected) DisconnectVideo();
            var cap = new OpenCVCapture(index, (OpenCvSharp.VideoCaptureAPIs)api);
            if (!cap.Open(index, api))
                return $"视频源打开失败: [{index}]";
            cap.SetProperties(1920, 1080);
            _capture = cap;
            _producer = new FrameProducer(cap);
            _producer.Start();
            VideoConnected = true;
            return null;
        }
    }

    public void DisconnectVideo()
    {
        lock (_gate)
        {
            _producer?.Dispose();
            _producer = null;
            _capture?.Dispose();
            _capture = null;
            VideoConnected = false;
        }
    }

    // ---- 单片机 ----

    public bool McuConnected => _mockMcu || _ns.IsConnected();

    public object McuInfo() => new
    {
        connected = McuConnected,
        ports = ECCore.GetDeviceNames().ToArray(),
    };

    /// <summary>连接单片机；"mock" 为无硬件虚拟手柄。返回错误消息（null = 成功）。</summary>
    public string? ConnectMcu(string port)
    {
        lock (_gate)
        {
            if (port.Equals("mock", StringComparison.OrdinalIgnoreCase))
            {
                _pad = new MockFlowPad();
                _mockMcu = true;
                return null;
            }
            if (_ns.TryConnect(port) != NintendoSwitch.ConnectResult.Success)
                return $"单片机连接失败: {port}";
            _pad = new PadInputAdapter(new GamePadAdapter(_ns));
            _mockMcu = false;
            return null;
        }
    }

    public void DisconnectMcu()
    {
        lock (_gate)
        {
            _pad = null;
            _mockMcu = false;
            _ns.Disconnect();
        }
    }

    // ---- 能力装配 ----

    /// <summary>
    /// 配置服务侧 OCR 后端（none | tesseract | ppocr）。
    /// 切换会释放旧引擎并延迟到下次运行/试跑时重建；返回错误消息（null = 成功）。
    /// </summary>
    public string? SetOcrBackend(string backend, string? modelDir = null)
    {
        var normalized = (backend ?? "").Trim().ToLowerInvariant();
        if (!FlowNodeCatalog.OcrBackends.Contains(normalized))
            return $"未知 OCR 后端: {backend}（可选 {string.Join(" | ", FlowNodeCatalog.OcrBackends)}）";
        if (normalized == "ppocr" && string.IsNullOrWhiteSpace(modelDir))
            return "ppocr 后端需要 modelDir（PP-OCR ONNX 模型与字典目录）";

        lock (_gate)
        {
            _ocr?.Dispose();
            _ocr = null;
            _ocrError = null;
            OcrBackend = normalized;
            OcrModelDir = modelDir;
            return null;
        }
    }

    /// <summary>OCR 后端自检信息（前端设备面板展示）。</summary>
    public object OcrInfo()
    {
        lock (_gate)
        {
            return new
            {
                backend = OcrBackend,
                modelDir = OcrModelDir,
                // 引擎实例是懒建的：这里只说明「后端已选定且实例已创建」，
                // 是否真能识别取决于 tessdata / 模型目录是否就位（失败原因走 error）
                engineCreated = _ocr != null,
                error = _ocrError,
                options = FlowNodeCatalog.OcrBackends,
            };
        }
    }

    /// <summary>取（必要时创建）服务侧共享 OCR 引擎；跨运行复用，避免每次重载模型。</summary>
    private IOcrService? AcquireOcr()
    {
        if (OcrBackend == "none")
            return null;
        lock (_gate)
        {
            if (_ocr != null || _ocrError != null)
                return _ocr;

            try
            {
                _ocr = OcrBackend switch
                {
                    "ppocr" => new PaddleOnnxOcr(OcrModelDir!),
                    _ => new TesseractOcrService(new OcrEngineCache
                    {
                        DefaultDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tessdata"),
                    }),
                };
            }
            catch (Exception ex)
            {
                _ocrError = ex.Message;
                CoreLog.Error($"Flow 服务 OCR 后端装配失败 (backend={OcrBackend}): {ex.Message}");
            }
            return _ocr;
        }
    }

    /// <summary>
    /// 依据当前设备状态构建一次运行的能力上下文（附租约；调用方负责释放）。
    /// 能力集统一经 <see cref="ScriptHostAssembler"/> 组装（组合根唯一装配点）：
    /// 采集源/手柄以端口传入，OCR/推理按配置共享（借用语义，不得由单次运行释放）。
    /// </summary>
    public (FlowHostContext Context, CapabilityLease? Lease) BuildRunContext()
    {
        lock (_gate)
        {
            ICaptureSource? capture = VideoConnected && _producer != null
                ? new FrameStoreCaptureSource(_producer.Store)
                : null;
            IOcrService? ocr = AcquireOcr();

            CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
            {
                CaptureSource = capture,
                PadInput = _pad,
                Ocr = ocr,
                BorrowResources = true,      // 共享 OCR/推理：所有权在本状态，运行结束不释放
                EnableOcr = false,           // 服务侧后端由 SetOcrBackend 决定，装配器不造默认替身
                AppDir = AppDomain.CurrentDomain.BaseDirectory,
            });

            var context = new FlowHostContext
            {
                Capture = lease.Capabilities.Capture,
                Vision = lease.Capabilities.Vision,
                Ocr = lease.Capabilities.Ocr,
                Inference = lease.Capabilities.Inference,
                Pad = lease.Capabilities.Input,
                AppDir = AppDomain.CurrentDomain.BaseDirectory,
            };
            return (context, lease);
        }
    }

    // ---- 编排图运行管理 ----

    public string StartFlow(string json)
    {
        var graph = FlowGraph.Parse(json);
        var runId = $"run-{Interlocked.Increment(ref _runSeq):000}";
        var handle = new FlowRunHandle(runId, graph);
        _runs[runId] = handle;
        handle.Task = Task.Run(() =>
        {
            var (context, lease) = BuildRunContext();
            try
            {
                handle.Report = new FlowExecutor(graph, context).Run(handle.Cts.Token);
                handle.Status = handle.Report.Error != null ? "error"
                    : handle.Report.WatchdogTriggered ? "watchdog" : "completed";
            }
            catch (OperationCanceledException)
            {
                handle.Status = "stopped";
            }
            catch (Exception ex)
            {
                handle.Status = "error";
                handle.Error = ex.Message;
                handle.Report.Events.Enqueue("fatal " + ex.Message);
            }
            finally
            {
                lease?.Dispose();
            }
        });
        return runId;
    }

    public void StopFlow(string runId)
    {
        if (_runs.TryGetValue(runId, out var handle))
            handle.Cts.Cancel();
    }

    /// <summary>运行状态快照（线程安全拷贝）；runId 为空返回全部运行的摘要。</summary>
    public object FlowStatus(string? runId)
    {
        var keys = runId is null ? _runs.Keys.ToArray() : [runId];
        var runs = new List<object>();
        foreach (var key in keys)
        {
            if (!_runs.TryGetValue(key, out var handle)) continue;
            lock (handle.Sync)
            {
                var report = handle.Report;
                runs.Add(new
                {
                    runId = handle.RunId,
                    name = handle.Graph.Name,
                    status = handle.Status,
                    steps = report?.Steps ?? 0,
                    totalMs = report?.TotalMs ?? 0,
                    error = handle.Error ?? report?.Error,
                    errorNode = report?.ErrorNodeId,
                    events = handle.Report?.Events.ToArray() ?? [],
                    records = report?.Records.Values
                        .Select(r => new
                        {
                            id = r.NodeId,
                            type = r.Type,
                            count = r.ExecCount,
                            reused = r.ReusedCount,
                            lastMs = r.LastMs,
                            totalMs = r.TotalMs,
                        })
                        .ToArray() ?? [],
                });
            }
        }
        return new { runs };
    }

    // ---- 单节点试跑（画布右键「执行此节点」/ agent 试跑）----

    /// <summary>独立模式下的保留节点 id。</summary>
    private const string TrialNodeId = "__node__";

    /// <summary>
    /// 单节点试跑：语义 = 「执行画布上这一个节点」，与整图执行共用 <see cref="FlowNodeRuntime"/>。
    ///
    /// <para>
    /// 两种模式：给 <c>graph</c> + <c>nodeId</c> 时从图中取节点（数据入边可引用上游，
    /// 上游会被惰性求值——这正是画布上「跑这个节点看输出」的期望行为）；
    /// 否则用 <c>type</c>/<c>params</c>/<c>inputs</c> 构造独立节点（inputs 只接受字面量）。
    /// </para>
    /// <para>
    /// actuation 层（pad.* / script.run）拒绝试跑：试跑不得驱动设备，这是 fail-closed 的只读边界。
    /// </para>
    /// </summary>
    public FlowNodeRunResult RunNode(FlowNodeRunRequest request, int timeoutSec = 15)
    {
        FlowGraph graph;
        FlowNode node;

        try
        {
            if (!string.IsNullOrWhiteSpace(request.Graph))
            {
                graph = FlowGraph.Parse(request.Graph);
                if (string.IsNullOrWhiteSpace(request.NodeId))
                    return FlowNodeRunResult.Fail("graph 模式需要 nodeId");
                var found = graph.Node(request.NodeId);
                if (found == null)
                    return FlowNodeRunResult.Fail($"图中不存在节点: {request.NodeId}");
                node = found;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Type))
                    return FlowNodeRunResult.Fail("需要 type，或 graph + nodeId");
                node = new FlowNode
                {
                    Id = TrialNodeId,
                    Type = request.Type,
                    Params = request.Params ?? [],
                    Inputs = request.Inputs ?? [],
                };
                graph = new FlowGraph { Name = "trial", Nodes = [node] };
            }
        }
        catch (FlowParseException ex)
        {
            return FlowNodeRunResult.Fail(ex.Message);
        }

        if (!FlowNodeCatalog.IsTrialAllowed(node.Type))
            return FlowNodeRunResult.Fail($"节点类型 {node.Type} 属于 actuation 层（会驱动设备），不支持单节点试跑");

        var (context, lease) = BuildRunContext();
        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSec)));
        try
        {
            // 执行许可覆盖惰性求值到的上游节点：试跑不得经数据边间接驱动设备
            var runtime = new FlowNodeRuntime(graph, context)
            {
                ExecuteGuard = candidate => FlowNodeCatalog.IsTrialAllowed(candidate.Type)
                    ? null
                    : $"试跑不得执行 {candidate.Type}（actuation 层，会驱动设备）",
            };
            var result = runtime.Execute(node, cts.Token);
            sw.Stop();
            return new FlowNodeRunResult(true, sw.Elapsed.TotalMilliseconds, result.Reused, null, result.Port, result.Outputs);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return FlowNodeRunResult.Fail($"试跑超时（{timeoutSec} 秒）", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return FlowNodeRunResult.Fail(ex.Message, sw.Elapsed.TotalMilliseconds);
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        foreach (var handle in _runs.Values)
            handle.Cts.Cancel();
        DisconnectVideo();
        DisconnectMcu();
        lock (_gate)
        {
            _ocr?.Dispose();
            _ocr = null;
        }
    }

    /// <summary>单节点试跑请求。</summary>
    public sealed record FlowNodeRunRequest(
        string? Type = null,
        string? NodeId = null,
        string? Graph = null,
        Dictionary<string, JsonElement>? Params = null,
        Dictionary<string, JsonElement>? Inputs = null);

    /// <summary>
    /// 单节点试跑结果：outputs 为数据输出（含 image 的 Base64，便于前端直接预览），
    /// port 为 exec 出口端口（compare 的 true/false 等条件节点只有出口、没有数据输出）。
    /// </summary>
    public sealed record FlowNodeRunResult(
        bool Ok, double Ms, bool Reused, string? Error, string? Port, Dictionary<string, object?> Outputs)
    {
        public static FlowNodeRunResult Fail(string error, double ms = 0) =>
            new(false, ms, false, error, null, []);
    }

    /// <summary>运行句柄。</summary>
    public sealed class FlowRunHandle(string runId, FlowGraph graph)
    {
        public string RunId { get; } = runId;
        public FlowGraph Graph { get; } = graph;
        public CancellationTokenSource Cts { get; } = new();
        public Task? Task { get; set; }
        public volatile string Status = "running";
        public volatile string? Error;
        public volatile FlowRunReport? Report;
        public readonly object Sync = new();
    }

    /// <summary>无硬件虚拟手柄（mock 连接用）。</summary>
    internal sealed class MockFlowPad : IPadInput
    {
        public void ClickButtons(GamePadKey key, int duration, CancellationToken token) { }

        public void PressButtons(GamePadKey key) { }

        public void ReleaseButtons(GamePadKey key) { }

        public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token) { }

        public void SetStick(GamePadKey key, byte x, byte y) { }

        public void ChangeAmiibo(uint index) { }
    }
}