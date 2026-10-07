using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Tools;
using System.Text.Json;

namespace EasyCon.Core.Flow;

/// <summary>
/// Flow 服务的 MCP/Agent 工具集：设备查询与连接管理 + 编排图运行管理。
/// 与 HTTP 服务同源（同一 <see cref="FlowServiceState"/> 实例）——外部 agent 与前端画布能力对等。
/// </summary>
public static class FlowServiceTools
{
    /// <summary>把设备/编排图工具注册到工具注册中心。</summary>
    public static void RegisterAll(ToolRegistry registry, FlowServiceState state)
    {
        registry.Register(new DeviceQueryTool(state));
        registry.Register(new DeviceConnectTool(state));
        registry.Register(new FlowRunTool(state));
        registry.Register(new FlowControlTool(state));
        registry.Register(new FlowNodeRunTool(state));
    }

    private static JsonSchema NoParams() => new() { Type = "object", Properties = new() };

    /// <summary>查询视频源/单片机列表与连接状态。</summary>
    private sealed class DeviceQueryTool(FlowServiceState state) : IAiTool
    {
        public string Name => "flow_device_query";

        public string Description =>
            "查询设备状态：视频源列表+连接状态、单片机端口列表+连接状态。也可查询节点目录（kind=nodes）。" +
            "用于决定连接哪个采集卡/单片机，或了解画布可用节点类型。";

        public ToolConcurrency Concurrency => ToolConcurrency.Parallel;

        public JsonSchema Parameters => new()
        {
            Type = "object",
            Properties = new()
            {
                ["kind"] = new JsonSchemaProperty { Type = "string", Description = "video | mcu | nodes（缺省 video）" },
            }
        };

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
        {
            var kind = args.TryGetValue("kind", out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() : "video";
            var payload = kind switch
            {
                "mcu" => JsonSerializer.Serialize(state.McuInfo()),
                "nodes" => FlowServiceState.NodeCatalogJson(),
                _ => JsonSerializer.Serialize(state.VideoInfo()),
            };
            return Task.FromResult(ToolResult.Ok(payload));
        }
    }

    /// <summary>连接/断开视频源与单片机。</summary>
    private sealed class DeviceConnectTool(FlowServiceState state) : IAiTool
    {
        public string Name => "flow_device_connect";

        public string Description =>
            "连接或断开设备。target=video 时 action=connect 需 index（采集设备索引）；" +
            "target=mcu 时 action=connect 需 port（串口名，\"mock\" = 无硬件虚拟手柄）。";

        public bool RequiresConfirmation => true;   // 连接真实设备属状态变更，需人工确认

        public JsonSchema Parameters => new()
        {
            Type = "object",
            Properties = new()
            {
                ["target"] = new JsonSchemaProperty { Type = "string", Description = "video | mcu" },
                ["action"] = new JsonSchemaProperty { Type = "string", Description = "connect | disconnect" },
                ["index"] = new JsonSchemaProperty { Type = "integer", Description = "视频源索引（target=video）" },
                ["port"] = new JsonSchemaProperty { Type = "string", Description = "串口名（target=mcu；mock = 虚拟）" },
            },
            Required = ["target", "action"]
        };

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
        {
            var target = Str(args, "target")?.ToLowerInvariant() ?? "";
            var action = Str(args, "action")?.ToLowerInvariant() ?? "";
            string? error;
            switch (target, action)
            {
                case ("video", "connect"):
                    error = state.ConnectVideo(Int(args, "index", -1));
                    break;
                case ("video", "disconnect"):
                    state.DisconnectVideo();
                    error = null;
                    break;
                case ("mcu", "connect"):
                    error = state.ConnectMcu(Str(args, "port") ?? "mock");
                    break;
                case ("mcu", "disconnect"):
                    state.DisconnectMcu();
                    error = null;
                    break;
                default:
                    return Task.FromResult(ToolResult.Error("[错误] target 为 video|mcu，action 为 connect|disconnect"));
            }

            return Task.FromResult(error == null
                ? ToolResult.Ok($"[成功] {target} {action}（video 已连接={state.VideoConnected}，mcu 已连接={state.McuConnected}）")
                : ToolResult.Error($"[错误] {error}"));
        }
    }

    /// <summary>运行编排图（传图 JSON，或仓库内图文件路径）。</summary>
    private sealed class FlowRunTool(FlowServiceState state) : IAiTool
    {
        public string Name => "flow_run";

        public string Description =>
            "执行一张编排图（flow.json）：传 graph（图 JSON 字符串）或 path（图文件路径）。返回 runId，" +
            "随后用 flow_control(action=status) 查询进度/事件/节点计时。";

        public bool RequiresConfirmation => true;   // 图可驱动单片机，需人工确认

        public JsonSchema Parameters => new()
        {
            Type = "object",
            Properties = new()
            {
                ["graph"] = new JsonSchemaProperty { Type = "string", Description = "图 JSON（与 path 二选一）" },
                ["path"] = new JsonSchemaProperty { Type = "string", Description = "图文件路径（与 graph 二选一）" },
            }
        };

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
        {
            string? json = Str(args, "graph");
            var path = Str(args, "path");
            if (json is null && path is not null)
            {
                var full = Path.IsPathRooted(path) ? path : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
                if (!File.Exists(full))
                    return Task.FromResult(ToolResult.Error($"[错误] 图文件不存在: {full}"));
                json = File.ReadAllText(full);
            }
            if (string.IsNullOrWhiteSpace(json))
                return Task.FromResult(ToolResult.Error("[错误] 需要 graph 或 path 参数"));

            try
            {
                var runId = state.StartFlow(json);
                return Task.FromResult(ToolResult.Ok($"[成功] 已启动，runId={runId}（用 flow_control status 查询）"));
            }
            catch (FlowParseException ex)
            {
                return Task.FromResult(ToolResult.Error($"[错误] 图不合法: {ex.Message}"));
            }
        }
    }

    /// <summary>单节点试跑（只读：actuation 层被服务端拒绝）。</summary>
    private sealed class FlowNodeRunTool(FlowServiceState state) : IAiTool
    {
        public string Name => "flow_node_run";

        public string Description =>
            "试跑单个编排节点并返回其输出（感知/决策层；actuation 层的 pad.*/script.run 会被拒绝，本工具天然只读）。" +
            "用法一：type + params + inputs（inputs 为字面量）独立试跑，例如 type=capture.frame、type=ocr.text 配 inputs.image。 " +
            "用法二：graph + nodeId，从图中取节点试跑，其数据入边引用的上游节点会被一并求值——" +
            "适合校准 ROI、验证识别结果、检查某个节点输出。节点类型与参数见 flow_device_query(kind=nodes)。";

        public ToolConcurrency Concurrency => ToolConcurrency.Exclusive;

        public JsonSchema Parameters => new()
        {
            Type = "object",
            Properties = new()
            {
                ["type"] = new JsonSchemaProperty { Type = "string", Description = "节点类型（如 capture.frame / ocr.text / compare）" },
                ["params"] = new JsonSchemaProperty { Type = "object", Description = "节点参数字典（与目录 params 同名）" },
                ["inputs"] = new JsonSchemaProperty { Type = "object", Description = "数据入边字面量字典（端口名 → 值）" },
                ["graph"] = new JsonSchemaProperty { Type = "string", Description = "图 JSON（与 nodeId 配合，替代 type）" },
                ["nodeId"] = new JsonSchemaProperty { Type = "string", Description = "图中节点 id（graph 模式必填）" },
            }
        };

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
        {
            var request = new FlowServiceState.FlowNodeRunRequest(
                Type: Str(args, "type"),
                NodeId: Str(args, "nodeId"),
                Graph: Str(args, "graph"),
                Params: Dict(args, "params"),
                Inputs: Dict(args, "inputs"));

            var result = state.RunNode(request);
            if (!result.Ok)
                return Task.FromResult(ToolResult.Error($"[错误] {result.Error}"));

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(new
            {
                ok = true,
                ms = Math.Round(result.Ms, 1),
                reused = result.Reused,
                port = result.Port,
                outputs = result.Outputs,
            })));
        }
    }

    /// <summary>查询运行状态 / 停止运行。</summary>
    private sealed class FlowControlTool(FlowServiceState state) : IAiTool
    {
        public string Name => "flow_control";

        public string Description =>
            "编排图运行管理：action=status 查询（可选 runId，缺省列全部运行）；action=stop 停止指定运行（需 runId）。" +
            "status 返回节点事件流与每节点计时。";

        public ToolConcurrency Concurrency => ToolConcurrency.Parallel;

        public JsonSchema Parameters => new()
        {
            Type = "object",
            Properties = new()
            {
                ["action"] = new JsonSchemaProperty { Type = "string", Description = "status | stop" },
                ["runId"] = new JsonSchemaProperty { Type = "string", Description = "运行 id（stop 必填；status 可选）" },
            },
            Required = ["action"]
        };

        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
        {
            var action = Str(args, "action")?.ToLowerInvariant();
            var runId = Str(args, "runId");
            switch (action)
            {
                case "status":
                    return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(state.FlowStatus(
                        string.IsNullOrEmpty(runId) ? null : runId))));
                case "stop":
                    if (string.IsNullOrEmpty(runId))
                        return Task.FromResult(ToolResult.Error("[错误] stop 需要 runId"));
                    state.StopFlow(runId);
                    return Task.FromResult(ToolResult.Ok($"[成功] 已请求停止 {runId}"));
                default:
                    return Task.FromResult(ToolResult.Error("[错误] action 为 status | stop"));
            }
        }
    }

    private static string? Str(Dictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static Dictionary<string, JsonElement>? Dict(Dictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var el) && el.ValueKind == JsonValueKind.Object
            ? el.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : null;

    private static int Int(Dictionary<string, JsonElement> args, string name, int fallback) =>
        args.TryGetValue(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)
            ? v : fallback;
}