using EasyCon.Capture;
using EasyCon.Core.Flow;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyCon.Tests.Flow;

/// <summary>
/// Flow 服务：节点目录、编排图运行管理（启动/停止/状态/事件）、HTTP 路由回环。
/// 设备连接路径不依赖真硬件（视频源用不存在的索引验证失败语义，单片机用 mock）。
/// </summary>
[TestFixture]
public class FlowServiceTests
{
    private const string TinyGraph = """
    {
      "name": "tiny",
      "nodes": [
        { "id": "a", "type": "wait", "params": { "ms": 1 } },
        { "id": "end", "type": "end" }
      ],
      "exec": [ ["a", "end"] ]
    }
    """;

    private const string LoopGraph = """
    {
      "name": "loop",
      "maxSteps": 100000,
      "nodes": [ { "id": "a", "type": "wait", "params": { "ms": 20 } } ],
      "exec": [ ["a", "a"] ]
    }
    """;

    [Test]
    public void NodeCatalog_ContainsCoreNodes()
    {
        using var doc = JsonDocument.Parse(FlowServiceState.NodeCatalogJson());
        var types = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.That(types, Does.Contain("capture.frame"));
        Assert.That(types, Does.Contain("ocr.text"));
        Assert.That(types, Does.Contain("compare"));
        Assert.That(types, Does.Contain("script.run"));
        Assert.That(types, Does.Contain("pad.sequence"));
    }

    // ---- 单节点试跑 ----

    private static Dictionary<string, JsonElement> Args(string json)
    {
        var dict = new Dictionary<string, JsonElement>();
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.EnumerateObject())
            dict[p.Name] = p.Value.Clone();
        return dict;
    }

    [Test]
    public void RunNode_StandaloneType_ReturnsOutputsAndPort()
    {
        using var state = new FlowServiceState();

        // compare 只有 exec 出口、没有数据输出
        var compare = state.RunNode(new FlowServiceState.FlowNodeRunRequest(
            Type: "compare", Params: Args("""{"op":">="}"""), Inputs: Args("""{"a":5,"b":3}""")));
        Assert.Multiple(() =>
        {
            Assert.That(compare.Ok, Is.True, compare.Error ?? "");
            Assert.That(compare.Port, Is.EqualTo("true"));
            Assert.That(compare.Ms, Is.GreaterThanOrEqualTo(0));
        });

        // 缺参数/缺能力 → 可读错误而不是抛异常
        var noPath = state.RunNode(new FlowServiceState.FlowNodeRunRequest(Type: "image.file"));
        Assert.That(noPath.Ok, Is.False);
        Assert.That(noPath.Error, Does.Contain("path"));

        var noCapture = state.RunNode(new FlowServiceState.FlowNodeRunRequest(Type: "capture.frame"));
        Assert.That(noCapture.Ok, Is.False);
        Assert.That(noCapture.Error, Does.Contain("视频源"));

        var noType = state.RunNode(new FlowServiceState.FlowNodeRunRequest());
        Assert.That(noType.Ok, Is.False);
    }

    [Test]
    public void RunNode_RejectsActuationLayer()
    {
        using var state = new FlowServiceState();
        var error = state.RunNode(new FlowServiceState.FlowNodeRunRequest(
            Type: "pad.key", Params: Args("""{"key":"A"}""")));
        Assert.That(error.Ok, Is.False);
        Assert.That(error.Error, Does.Contain("actuation"));
    }

    [Test]
    public void RunNode_WithGraphResolvesDataEdges()
    {
        using var state = new FlowServiceState();

        // 上游 image.file 不在 exec 路径上，仅作为 chk 的数据来源 → 试跑时须被惰性求值
        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-trial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);
        var imagePath = Path.Combine(appDir, "probe.png");
        using (var mat = new OpenCvSharp.Mat(8, 8, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.Blue))
            OpenCvSharp.Cv2.ImWrite(imagePath, mat);

        var graph = $$"""
        {
          "name": "trial",
          "nodes": [
            { "id": "chk", "type": "compare", "params": { "op": "==" },
              "inputs": { "a": { "node": "file", "port": "image" }, "b": { "node": "file", "port": "image" } } },
            { "id": "file", "type": "image.file", "params": { "path": "{{imagePath.Replace("\\", "\\\\")}}" } }
          ],
          "exec": [ ["file", "chk"] ]
        }
        """;

        var result = state.RunNode(new FlowServiceState.FlowNodeRunRequest(Graph: graph, NodeId: "chk"));
        Assert.That(result.Ok, Is.True, result.Error ?? "");
        Assert.That(result.Port, Is.EqualTo("true"), "同一上游输出比较自身必然相等（证明数据边已求值）");

        var missing = state.RunNode(new FlowServiceState.FlowNodeRunRequest(Graph: graph, NodeId: "nope"));
        Assert.That(missing.Ok, Is.False);
        Assert.That(missing.Error, Does.Contain("nope"));
    }

    [Test]
    public void RunNode_RefusesActuationReachedThroughDataEdge()
    {
        using var state = new FlowServiceState();

        // 目标节点是决策层，但数据入边引用 script.run（actuation）：不得经惰性求值间接执行
        var graph = """
        {
          "name": "trial",
          "nodes": [
            { "id": "chk", "type": "compare", "params": { "op": "==" },
              "inputs": { "a": { "node": "run", "port": "ok" }, "b": 1 } },
            { "id": "run", "type": "script.run", "params": { "file": "whatever.ecs" } }
          ],
          "exec": [ ["run", "chk"] ]
        }
        """;

        var result = state.RunNode(new FlowServiceState.FlowNodeRunRequest(Graph: graph, NodeId: "chk"));
        Assert.That(result.Ok, Is.False, "试跑必须拒绝经数据边到达的 actuation 节点");
        Assert.That(result.Error, Does.Contain("script.run"));
    }

    [Test]
    public void OcrBackend_SwitchValidatesOptions()
    {
        using var state = new FlowServiceState();
        Assert.That(state.OcrBackend, Is.EqualTo("none"), "默认不装配 OCR（显式开关）");

        Assert.That(state.SetOcrBackend("bogus"), Is.Not.Null);
        Assert.That(state.SetOcrBackend("ppocr"), Does.Contain("modelDir"), "ppocr 必须给模型目录");
        Assert.That(state.SetOcrBackend("none"), Is.Null);
        Assert.That(state.OcrBackend, Is.EqualTo("none"));

        var info = JsonSerializer.Serialize(state.OcrInfo());
        Assert.That(info, Does.Contain("\"backend\":\"none\""));
    }

    [Test]
    public async Task HttpService_ServesNodeRunAndOptionsRoutes()
    {
        var port = 25000 + Random.Shared.Next(2000);
        using var state = new FlowServiceState();
        await using var http = new FlowServiceHttp(state, port);
        await http.StartAsync(CancellationToken.None);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        // 单节点试跑（decision 层）
        var run = await client.PostAsJsonAsync("/api/node/run",
            new { type = "compare", @params = new { op = ">" }, inputs = new { a = 2, b = 1 } });
        Assert.That((int)run.StatusCode, Is.EqualTo(200));
        var payload = await run.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(payload.GetProperty("ok").GetBoolean(), Is.True);
        Assert.That(payload.GetProperty("port").GetString(), Is.EqualTo("true"));

        // actuation 层 → 400 + 可读原因
        var denied = await client.PostAsJsonAsync("/api/node/run", new { type = "pad.key" });
        Assert.That((int)denied.StatusCode, Is.EqualTo(400));
        Assert.That((await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString(),
            Does.Contain("actuation"));

        // 选项：OCR 后端开关
        var options = await client.GetFromJsonAsync<JsonElement>("/api/options");
        Assert.That(options.GetProperty("ocr").GetProperty("backend").GetString(), Is.EqualTo("none"));

        var set = await client.PostAsJsonAsync("/api/options", new { ocrBackend = "ppocr" });
        Assert.That((int)set.StatusCode, Is.EqualTo(400), "ppocr 缺 modelDir 时必须拒绝");

        var ok = await client.PostAsJsonAsync("/api/options", new { ocrBackend = "none" });
        Assert.That((int)ok.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task NodeRunTool_TriesNodeAndRefusesActuation()
    {
        using var state = new FlowServiceState();
        var registry = new EasyCon.Core.LLM.Agent.Tools.ToolRegistry();
        FlowServiceTools.RegisterAll(registry, state);

        var ok = await registry.Get("flow_node_run")!.ExecuteAsync(ToolArgs(
            ("type", "compare"), ("params", new { op = "!=" }), ("inputs", new { a = 1, b = 2 })));
        Assert.That(ok.Status, Is.EqualTo(EasyCon.Core.LLM.Agent.Tools.ToolResultStatus.Success), ok.Content);
        Assert.That(ok.Content, Does.Contain("\"port\":\"true\""));

        var denied = await registry.Get("flow_node_run")!.ExecuteAsync(ToolArgs(("type", "pad.sequence")));
        Assert.That(denied.Status, Is.EqualTo(EasyCon.Core.LLM.Agent.Tools.ToolResultStatus.Error));
        Assert.That(denied.Content, Does.Contain("actuation"));
    }

    [Test]
    public async Task StartFlow_CompletesAndReportsStatus()
    {
        using var state = new FlowServiceState();
        var runId = state.StartFlow(TinyGraph);
        Assert.That(runId, Does.StartWith("run-"));

        await WaitForAsync(() => StatusOf(state, runId) == "completed");
        var status = JsonSerializer.Serialize(state.FlowStatus(runId));
        Assert.That(status, Does.Contain("\"status\":\"completed\""));
        Assert.That(status, Does.Contain("\"steps\":2"), "a + end 两步");
    }

    [Test]
    public async Task StopFlow_CancelsRunningLoop()
    {
        using var state = new FlowServiceState();
        var runId = state.StartFlow(LoopGraph);
        await Task.Delay(80);
        state.StopFlow(runId);
        await WaitForAsync(() => StatusOf(state, runId) == "stopped");
        Assert.That(StatusOf(state, runId), Is.EqualTo("stopped"));
    }

    [Test]
    public void StartFlow_InvalidJson_ThrowsParseException()
    {
        using var state = new FlowServiceState();
        Assert.Throws<FlowParseException>(() => state.StartFlow("{ not json"));
    }

    [Test]
    public void DeviceConnections_ReportClearErrors()
    {
        using var state = new FlowServiceState();

        // 不存在的视频源索引：返回可读错误而非抛异常
        var videoError = state.ConnectVideo(9999);
        Assert.That(videoError, Is.Not.Null);
        Assert.That(videoError, Does.Contain("9999"));
        Assert.That(state.VideoConnected, Is.False);

        // mock 单片机：无硬件即可连接
        Assert.That(state.ConnectMcu("mock"), Is.Null);
        state.DisconnectMcu();

        // 不存在的串口：可读错误
        var mcuError = state.ConnectMcu("COM_NOT_EXIST_999");
        Assert.That(mcuError, Is.Not.Null);
    }

    // ---- 设备桥接（GUI 宿主：画布/agent 与 GUI 面板共享同一实例）----

    /// <summary>宿主侧假设备：记录桥接调用，模拟 GUI 监控页持有的实例。</summary>
    private sealed class FakeBridge : FlowDeviceBridge
    {
        public bool Video;
        public int? VideoIndex;
        public string? NextVideoError;
        public int DisconnectVideoCount;
        public bool Mcu;
        public int DisconnectMcuCount;
        public FrameStore? Store;

        [SetsRequiredMembers]
        public FakeBridge()
        {
            IsVideoConnected = () => Video;
            ConnectVideo = index =>
            {
                VideoIndex = index;
                if (NextVideoError != null)
                {
                    Video = false;   // 与 CaptureService 一致：失败留在干净的无连接状态
                    return NextVideoError;
                }
                Video = true;
                return null;
            };
            DisconnectVideo = () => { DisconnectVideoCount++; Video = false; };
            GetFrameStore = () => Store;
            IsMcuConnected = () => Mcu;
            ConnectMcu = _ => { Mcu = true; return null; };
            DisconnectMcu = () => { DisconnectMcuCount++; Mcu = false; };
            GetPad = () => null;
        }
    }

    [Test]
    public void BridgedVideoConnect_DelegatesToHostInstance()
    {
        var bridge = new FakeBridge();
        using var state = new FlowServiceState(bridge);

        // 连接走宿主实例，状态实时一致
        Assert.That(state.ConnectVideo(2), Is.Null);
        Assert.That(bridge.VideoIndex, Is.EqualTo(2));
        Assert.That(state.VideoConnected, Is.True);

        // 宿主连接失败 → 错误原样回传调用方（画布/agent 可读），状态保持未连接
        bridge.NextVideoError = "视频源打开失败: [3]";
        Assert.That(state.ConnectVideo(3), Is.EqualTo("视频源打开失败: [3]"));
        Assert.That(state.VideoConnected, Is.False);

        state.DisconnectVideo();
        Assert.That(bridge.DisconnectVideoCount, Is.EqualTo(1));
        Assert.That(state.VideoConnected, Is.False);
    }

    [Test]
    public void BridgedMcuConnect_DelegatesRealPortsAndKeepsMockLocal()
    {
        var bridge = new FakeBridge();
        using var state = new FlowServiceState(bridge);

        // 真实串口 → 桥接宿主实例
        Assert.That(state.ConnectMcu("COM3"), Is.Null);
        Assert.That(state.McuConnected, Is.True);
        Assert.That(bridge.Mcu, Is.True);

        state.DisconnectMcu();
        Assert.That(bridge.DisconnectMcuCount, Is.EqualTo(1), "真实串口断开传导到宿主");
        Assert.That(state.McuConnected, Is.False);

        // mock → 状态自持；断开不得顺带断掉宿主侧串口
        Assert.That(state.ConnectMcu("mock"), Is.Null);
        Assert.That(state.McuConnected, Is.True);
        state.DisconnectMcu();
        Assert.That(bridge.DisconnectMcuCount, Is.EqualTo(1), "mock 断开不传导到宿主");
        Assert.That(state.McuConnected, Is.False);
    }

    [Test]
    public void BridgedRunContext_WrapsHostFrameStore()
    {
        var store = new FrameStore();   // 非 IDisposable：帧由发布方（FrameProducer）释放
        var bridge = new FakeBridge { Store = store, Video = true };
        using var state = new FlowServiceState(bridge);

        var (context, lease) = state.BuildRunContext();
        try
        {
            Assert.That(context.Capture, Is.Not.Null, "宿主已连接 → 运行上下文带采集源");
            Assert.That(context.Capture!.FrameIndex, Is.EqualTo(store.FrameCount),
                "帧号语义保留（等帧/慢感知依赖 FrameIndex）");
        }
        finally
        {
            lease?.Dispose();
        }
    }

    [Test]
    public async Task HttpService_ServesCatalogDeviceAndFlowRoutes()
    {
        var port = 23000 + Random.Shared.Next(2000);
        using var state = new FlowServiceState();
        await using var http = new FlowServiceHttp(state, port);
        await http.StartAsync(CancellationToken.None);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        var health = await client.GetFromJsonAsync<JsonElement>("/api/health");
        Assert.That(health.GetProperty("ok").GetBoolean(), Is.True);

        var nodes = await client.GetFromJsonAsync<JsonElement>("/api/nodes");
        Assert.That(nodes.GetProperty("nodes").GetArrayLength(), Is.GreaterThan(5));

        var video = await client.GetFromJsonAsync<JsonElement>("/api/device/video");
        Assert.That(video.GetProperty("connected").GetBoolean(), Is.False);

        // mock 单片机连接
        var connect = await client.PostAsJsonAsync("/api/device/mcu/connect", new { port = "mock" });
        Assert.That((int)connect.StatusCode, Is.EqualTo(200));

        // 运行图 + 轮询状态
        var run = await client.PostAsJsonAsync("/api/flow/run", new { json = TinyGraph });
        Assert.That((int)run.StatusCode, Is.EqualTo(200));
        var runId = (await run.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetString()!;

        var completed = false;
        for (var i = 0; i < 50 && !completed; i++)
        {
            await Task.Delay(50);
            var status = await client.GetFromJsonAsync<JsonElement>($"/api/flow/status?runId={runId}");
            var first = status.GetProperty("runs").EnumerateArray().First();
            completed = first.GetProperty("status").GetString() == "completed";
            if (completed)
                Assert.That(first.GetProperty("events").GetArrayLength(), Is.GreaterThan(0), "应有节点事件");
        }
        Assert.That(completed, Is.True, "HTTP 提交的图应执行完成");

        // 非法图 → 400 + 可读错误
        var bad = await client.PostAsync("/api/flow/run",
            new StringContent("{\"json\":\"{ bad\"}", Encoding.UTF8, "application/json"));
        Assert.That((int)bad.StatusCode, Is.EqualTo(400));
    }

    static string? StatusOf(FlowServiceState state, string runId)
    {
        var json = JsonSerializer.Serialize(state.FlowStatus(runId));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("runs").EnumerateArray().First().GetProperty("status").GetString();
    }

    static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(25);
    }

    // ---- MCP/Agent 工具（与 HTTP 同源）----

    private static Dictionary<string, System.Text.Json.JsonElement> ToolArgs(params (string Key, object Value)[] pairs)
    {
        var dict = new Dictionary<string, System.Text.Json.JsonElement>();
        foreach (var (key, value) in pairs)
            dict[key] = System.Text.Json.JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.Clone();
        return dict;
    }

    [Test]
    public async Task Tools_QueryConnectAndRunFlow()
    {
        using var state = new FlowServiceState();
        var registry = new EasyCon.Core.LLM.Agent.Tools.ToolRegistry();
        FlowServiceTools.RegisterAll(registry, state);

        // 节点目录查询
        var nodes = await registry.Get("flow_device_query")!
            .ExecuteAsync(ToolArgs(("kind", "nodes")));
        Assert.That(nodes.Content, Does.Contain("capture.frame"));

        // mock 单片机连接 + 状态查询
        var connect = await registry.Get("flow_device_connect")!
            .ExecuteAsync(ToolArgs(("target", "mcu"), ("action", "connect"), ("port", "mock")));
        Assert.That(connect.Status, Is.EqualTo(EasyCon.Core.LLM.Agent.Tools.ToolResultStatus.Success), connect.Content);
        var mcu = await registry.Get("flow_device_query")!.ExecuteAsync(ToolArgs(("kind", "mcu")));
        Assert.That(mcu.Content, Does.Contain("\"connected\":true"));

        // 跑图 + 状态
        var run = await registry.Get("flow_run")!.ExecuteAsync(ToolArgs(("graph", TinyGraph)));
        Assert.That(run.Status, Is.EqualTo(EasyCon.Core.LLM.Agent.Tools.ToolResultStatus.Success), run.Content);
        var runId = run.Content[(run.Content.IndexOf("runId=", StringComparison.Ordinal) + 6)..].Split('（')[0];

        await WaitForAsync(() => StatusOf(state, runId) == "completed");
        var status = await registry.Get("flow_control")!
            .ExecuteAsync(ToolArgs(("action", "status"), ("runId", runId)));
        Assert.That(status.Content, Does.Contain("completed"));
        Assert.That(status.Content, Does.Contain("nodeEnd"));

        // 非法图 → 可读错误
        var bad = await registry.Get("flow_run")!.ExecuteAsync(ToolArgs(("graph", "{ bad")));
        Assert.That(bad.Status, Is.EqualTo(EasyCon.Core.LLM.Agent.Tools.ToolResultStatus.Error));
        Assert.That(bad.Content, Does.Contain("图不合法"));
    }

    [Test]
    public void DangerousTools_AreFilteredFromMcpUnlessOptedIn()
    {
        using var state = new FlowServiceState();
        var registry = new EasyCon.Core.LLM.Agent.Tools.ToolRegistry();
        FlowServiceTools.RegisterAll(registry, state);

        var safe = EasyCon.Core.LLM.Agent.Mcp.McpServerHost.ExportTools(registry);
        var all = EasyCon.Core.LLM.Agent.Mcp.McpServerHost.ExportTools(registry, includeDangerous: true);
        var safeNames = safe.Select(t => t.ProtocolTool.Name).ToList();
        var allNames = all.Select(t => t.ProtocolTool.Name).ToList();

        Assert.That(safeNames, Does.Contain("flow_device_query"), "只读工具默认导出");
        Assert.That(safeNames, Does.Not.Contain("flow_run"), "危险工具默认不导出（fail-closed）");
        Assert.That(allNames, Does.Contain("flow_run"), "显式授权后导出");
    }
}