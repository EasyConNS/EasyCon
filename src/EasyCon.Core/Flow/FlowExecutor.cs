using EasyCon.Core.Capabilities;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace EasyCon.Core.Flow;

/// <summary>
/// Flow 执行宿主上下文：执行器所需的设备/能力原料（宿主装配原则同 ScriptHostContext）。
/// 端口一律可为 null，语义为「该能力不可用」——相关节点报错而不是静默降级。
/// </summary>
public sealed class FlowHostContext
{
    /// <summary>实时采集源（null = 视频源未连接，感知节点报错）。</summary>
    public ICaptureSource? Capture { get; init; }

    /// <summary>图像处理 / 标签匹配（vision.* 节点通路；null = 未装配）。</summary>
    public IVisionService? Vision { get; init; }

    /// <summary>宿主 OCR 服务（ocr.* 节点通路；null = 未装配）。</summary>
    public IOcrService? Ocr { get; init; }

    /// <summary>ONNX 通用推理（script.run 内 NET_* 族通路；null = 未装配）。</summary>
    public IInference? Inference { get; init; }

    /// <summary>手柄输入（pad.* 节点通路；null = 单片机未连接，执行报错）。</summary>
    public IPadInput? Pad { get; init; }

    /// <summary>__APP__ 基准目录（script.run 相对路径与文件解析基准）。</summary>
    public string AppDir { get; init; } = AppDomain.CurrentDomain.BaseDirectory;
}

/// <summary>单节点的执行记录（计时 + 最后输出）。</summary>
public sealed class FlowNodeRecord
{
    public string NodeId { get; init; } = "";
    public string Type { get; init; } = "";
    public int ExecCount { get; set; }

    /// <summary>其中命中 slow 复用（本步未真正执行，直接沿用上次输出）的次数。</summary>
    public int ReusedCount { get; set; }

    public double LastMs { get; set; }
    public double TotalMs { get; set; }
    public Dictionary<string, object?> LastOutputs { get; set; } = new();
}

/// <summary>一次运行的汇总报告。</summary>
public sealed class FlowRunReport
{
    public bool Completed { get; set; }
    public bool WatchdogTriggered { get; set; }
    public string? Error { get; set; }
    public string? ErrorNodeId { get; set; }
    public int Steps { get; set; }
    public double TotalMs { get; set; }
    public Dictionary<string, FlowNodeRecord> Records { get; } = new();

    /// <summary>节点级事件流（供服务轮询/前端高亮；线程安全）。</summary>
    public ConcurrentQueue<string> Events { get; } = new();

    public FlowNodeRecord Record(string nodeId, string type) =>
        Records.TryGetValue(nodeId, out var r)
            ? r
            : Records[nodeId] = new FlowNodeRecord { NodeId = nodeId, Type = type };
}

/// <summary>
/// Flow 图执行引擎：从 start 沿 exec 边推进；节点语义与数据入边求值全部落在 <see cref="FlowNodeRuntime"/>。
/// 看门狗：图级 maxSteps + timeoutSec；支持外部停止令牌；每节点计时入报告。
/// </summary>
public sealed class FlowExecutor(FlowGraph graph, FlowHostContext context)
{
    /// <summary>exec 步数上限（防图作者漏配看门狗）。</summary>
    private const int HardStepLimit = 100_000;

    public FlowRunReport Run(CancellationToken token)
    {
        var report = new FlowRunReport();
        var runtime = new FlowNodeRuntime(graph, context);
        var sw = Stopwatch.StartNew();

        var next = FindStart();

        while (true)
        {
            token.ThrowIfCancellationRequested();

            if (report.Steps >= Math.Min(graph.MaxSteps, HardStepLimit))
            {
                report.WatchdogTriggered = true;
                report.Events.Enqueue("watchdog maxSteps");
                break;
            }
            if (sw.Elapsed.TotalSeconds >= graph.TimeoutSec)
            {
                report.WatchdogTriggered = true;
                report.Events.Enqueue("watchdog timeout");
                break;
            }

            report.Steps++;
            var record = report.Record(next.Id, next.Type);
            var nodeSw = Stopwatch.StartNew();
            try
            {
                FlowNodeResult result = runtime.Execute(next, token);
                record.ExecCount++;
                if (result.Reused)
                    record.ReusedCount++;
                record.LastMs = nodeSw.ElapsedMilliseconds;
                record.TotalMs += record.LastMs;
                record.LastOutputs = result.Outputs;
                report.Events.Enqueue(result.Reused
                    ? $"nodeReuse {next.Id} {next.Type} {record.LastMs}ms"
                    : $"nodeEnd {next.Id} {next.Type} {record.LastMs}ms");

                FlowExecEdge? edge = SelectEdge(next, result.Port);
                if (edge == null)
                {
                    report.Completed = true;   // 走到尽头视为正常结束
                    break;
                }
                next = graph.Node(edge.To)!;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                record.LastMs = nodeSw.ElapsedMilliseconds;
                record.TotalMs += record.LastMs;
                report.Error = ex.Message;
                report.ErrorNodeId = next.Id;
                report.Events.Enqueue($"error {next.Id} {ex.Message}");
                break;
            }
        }

        report.TotalMs = sw.ElapsedMilliseconds;
        return report;
    }

    /// <summary>
    /// 选择下一条 exec 边：节点给出出口端口时优先精确匹配该端口，其次才是无标签边。
    /// （顺序反了会让 compare 的 .true/.false 分支被同名无标签边抢走。）
    /// </summary>
    private FlowExecEdge? SelectEdge(FlowNode node, string? port)
    {
        var outgoing = graph.Exec.Where(e => e.FromNode == node.Id).ToList();
        if (port != null)
        {
            var exact = outgoing.FirstOrDefault(e => e.FromPort == port);
            if (exact != null)
                return exact;
        }
        return outgoing.FirstOrDefault(e => e.FromPort == null);
    }

    private FlowNode FindStart()
    {
        var start = graph.Nodes.FirstOrDefault(n => n.Type == "start");
        if (start != null) return start;

        // 无显式 start：取唯一无 exec 入边的节点
        var targets = graph.Exec.Select(e => e.To).ToHashSet();
        var roots = graph.Nodes.Where(n => !targets.Contains(n.Id)).ToList();
        if (roots.Count == 1) return roots[0];
        if (roots.Count == 0 && graph.Nodes.Count > 0)
            return graph.Nodes[0];   // 纯自环等全环图：从第一个节点进入
        throw new FlowParseException(
            $"图中未定义 start 节点，且无 exec 入边的节点不唯一（{string.Join(", ", roots.Select(r => r.Id))}），无法确定起点");
    }
}