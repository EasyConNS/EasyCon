using EasyCon.Core.Capabilities;
using EasyCon.Core.Hosting;
using EasyCon.Core.Logging;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyScript;
using OpenCvSharp;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyCon.Core.Flow;

/// <summary>单节点执行结果：exec 出口端口（null = 单出口）、本次输出、是否命中 slow 复用。</summary>
public readonly record struct FlowNodeResult(string? Port, Dictionary<string, object?> Outputs, bool Reused);

/// <summary>
/// 节点运行时：节点语义（含慢感知复用与等待新帧）+ 数据入边解析（惰性求值 + 当次运行 memo）。
///
/// <para>
/// <see cref="FlowExecutor"/>（整图执行）与「单节点试跑」（服务层 / 画布右键执行此节点）共用本类——
/// 同一节点类型在两条路径上只有一处实现，试跑结果才可信。
/// </para>
/// <para>
/// 数据入边语义：端口值可为字面量或 <c>{"node","port"}</c> 引用；
/// 引用在首次被读取时惰性求值（递归执行上游节点），本次运行内不重复求值；
/// exec 边上的节点每次执行都会刷新自身输出，因此「数据来自 exec 路径上游」恒为最新帧。
/// 数据边成环会被拒绝，而不是递归到栈溢出。
/// </para>
/// </summary>
public sealed class FlowNodeRuntime(FlowGraph graph, FlowHostContext context)
{
    /// <summary>waitForNew 未给 timeoutMs 时的等待上限；超时取当前帧继续（不阻塞整图）。</summary>
    private const int DefaultWaitNewFrameMs = 2000;

    private readonly Dictionary<string, Dictionary<string, object?>> _outputs = [];
    private readonly HashSet<string> _memo = [];
    private readonly HashSet<string> _inFlight = [];
    private readonly Dictionary<string, SlowCacheEntry> _slow = [];
    private readonly Dictionary<string, long> _counters = [];
    private readonly Dictionary<string, string?> _lastFingerprint = [];

    /// <summary>已求值节点的最近输出（nodeId → port → 值）。</summary>
    public IReadOnlyDictionary<string, Dictionary<string, object?>> Outputs => _outputs;

    /// <summary>
    /// 节点执行许可：返回 null 表允许，返回字符串为拒绝原因。
    /// 单节点试跑用它兜住「经数据入边间接执行 actuation 节点」的越权路径——
    /// 只校验目标节点的 layer 不够，惰性求值会把上游节点一并执行。
    /// </summary>
    public Func<FlowNode, string?>? ExecuteGuard { get; init; }

    /// <summary>执行节点，返回 exec 出口端口与输出（slow 命中时直接返回上次输出，不重复执行）。</summary>
    public FlowNodeResult Execute(FlowNode node, CancellationToken token)
    {
        if (ExecuteGuard?.Invoke(node) is { } denied)
            throw new InvalidOperationException(denied);

        if (ShouldReuse(node, token))
        {
            SlowCacheEntry cached = _slow[node.Id];
            _outputs[node.Id] = cached.Outputs;
            return new FlowNodeResult(cached.Port, cached.Outputs, true);
        }

        var outputs = new Dictionary<string, object?>();
        if (!_inFlight.Add(node.Id))
            throw new InvalidOperationException($"数据入边存在环，节点被重复求值: {node.Id}");

        string? port;
        try
        {
            port = ExecuteCore(node, outputs, token);
        }
        finally
        {
            _inFlight.Remove(node.Id);
        }

        _outputs[node.Id] = outputs;
        _memo.Add(node.Id);
        if (node.Slow != null)
        {
            // 指纹只在配了 onChange 时算：否则每步都会白抓一帧
            var fingerprint = node.Slow.OnChange == true ? SenseFingerprint(node, token) : null;
            _slow[node.Id] = new SlowCacheEntry(
                port, outputs, context.Capture?.FrameIndex, Environment.TickCount64, fingerprint);
        }
        return new FlowNodeResult(port, outputs, false);
    }

    /// <summary>
    /// slow 判定：任一已配置条件判定「必须重算」就重算；只有全部条件都判「可复用」且存在上次缓存时才复用。
    /// 判定不了的情形（源不给帧号）按「必须重算」处理——慢感知只是省开销，不得改变结果的新鲜度语义。
    /// </summary>
    private bool ShouldReuse(FlowNode node, CancellationToken token)
    {
        if (node.Slow is not { } slow || !_slow.TryGetValue(node.Id, out SlowCacheEntry? prev))
            return false;   // 无上次输出 → 必须执行

        var configured = false;
        var mustRun = false;

        if (slow.IntervalMs is int ms && ms > 0)
        {
            configured = true;
            mustRun |= Environment.TickCount64 - prev.StampMs >= ms;
        }

        if (slow.EveryFrames is int every && every > 0)
        {
            configured = true;
            var now = context.Capture?.FrameIndex;
            mustRun |= now is null || prev.FrameIndex is null || now - prev.FrameIndex >= every;
        }

        if (slow.OnChange == true)
        {
            configured = true;
            mustRun |= !string.Equals(SenseFingerprint(node, token), prev.Fingerprint, StringComparison.Ordinal);
        }

        return configured && !mustRun;
    }

    /// <summary>
    /// 感知输入指纹：显式 image 入边优先（内容即输入），否则取当前帧的编码字节。
    /// 采集帧为定长编码时同一画面指纹稳定，可作为 onChange 判据。
    /// </summary>
    private string? SenseFingerprint(FlowNode node, CancellationToken token)
    {
        if (node.Inputs.ContainsKey("image"))
            return Input(node, "image", token) as string;
        return context.Capture?.CaptureFrame(-1, -1, -1, -1);
    }

    /// <summary>按端口读取数据入边（字面量或引用）；引用触发上游惰性求值。</summary>
    public object? Input(FlowNode node, string port, CancellationToken token)
    {
        if (!node.Inputs.TryGetValue(port, out var el))
            return null;

        // 引用形式 {"node": "id", "port": "name"}
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("node", out var nEl))
        {
            var refId = nEl.GetString() ?? "";
            var refPort = el.TryGetProperty("port", out var pEl) ? pEl.GetString() ?? "" : "";
            var refNode = graph.Node(refId)
                ?? throw new InvalidOperationException($"数据入边引用了不存在的节点: {refId}");

            if (!_memo.Contains(refId))
            {
                // 惰性求值结果必须回填 _outputs，否则纯数据边（上游不在 exec 路径上）永远取不到值
                FlowNodeResult upstream = Execute(refNode, token);
                _outputs[refId] = upstream.Outputs;
            }
            return _outputs.TryGetValue(refId, out var o) && o.TryGetValue(refPort, out var v) ? v : null;
        }

        return Literal(el);
    }

    private static object? Literal(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt32(out var i) ? i : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText(),
    };

    private int IntInput(FlowNode node, string port, CancellationToken token, int fallback) =>
        Input(node, port, token) switch
        {
            int i => i,
            double d => (int)d,
            string s when int.TryParse(s, out var v) => v,
            _ => fallback,
        };

    private string? StrInput(FlowNode node, string port, CancellationToken token) =>
        Input(node, port, token) switch
        {
            string s => s,
            null => null,
            var v => v.ToString(),
        };

    /// <summary>整数入边优先，缺省回落节点参数。</summary>
    private int IntInputOrParam(FlowNode node, string port, CancellationToken token) =>
        IntInput(node, port, token, IntParam(node, port));

    private string? StrParam(FlowNode node, string name) =>
        node.Params.TryGetValue(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static int IntParam(FlowNode node, string name, int fallback = 0) =>
        node.Params.TryGetValue(name, out var el)
            && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : fallback;

    /// <summary>执行节点本体，返回 exec 出口端口名（null = 单出口）。</summary>
    private string? ExecuteCore(FlowNode node, Dictionary<string, object?> outputs, CancellationToken token)
    {
        switch (node.Type)
        {
            case "start":
                return null;

            case "capture.frame":
                {
                    var src = context.Capture
                        ?? throw new InvalidOperationException("视频源未连接，capture.frame 不可用");
                    if (BoolParam(node, "waitForNew"))
                        WaitForNewFrame(node, src, token);
                    int x = IntInput(node, "x", token, -1);
                    int y = IntInput(node, "y", token, -1);
                    int w = IntInput(node, "w", token, -1);
                    int h = IntInput(node, "h", token, -1);
                    outputs["image"] = src.CaptureFrame(x, y, w, h)
                        ?? throw new InvalidOperationException("采集源未返回画面");
                    outputs["frameIndex"] = src.FrameIndex;
                    return null;
                }

            case "image.file":
                {
                    var path = StrParam(node, "path");
                    if (string.IsNullOrWhiteSpace(path))
                        throw new InvalidOperationException("image.file 缺少 path 参数");
                    var full = Path.IsPathRooted(path) ? path : Path.Combine(context.AppDir, path);
                    using var m = Cv2.ImRead(full, ImreadModes.Color);
                    if (m.Empty()) throw new InvalidOperationException($"图片解码失败: {full}");
                    outputs["image"] = Convert.ToBase64String(m.ToBytes(".png"));
                    return null;
                }

            case "ocr.text":
                {
                    var ocr = context.Ocr
                        ?? throw new InvalidOperationException("OCR 服务未装配");
                    var image = StrInput(node, "image", token)
                        ?? throw new InvalidOperationException("ocr.text 缺少 image 输入");
                    var text = ocr.Recognize(ImageRef.FromBase64(image), new OcrQuery
                    {
                        Language = StrParam(node, "lang"),
                        X = IntInputOrParam(node, "x", token),
                        Y = IntInputOrParam(node, "y", token),
                        Width = IntInputOrParam(node, "w", token),
                        Height = IntInputOrParam(node, "h", token),
                    });
                    outputs["text"] = text;
                    outputs["conf"] = ocr.LastConfidence;
                    return null;
                }

            case "compare":
                return CompareValues(
                    Input(node, "a", token),
                    Input(node, "b", token),
                    StrParam(node, "op") ?? "==");

            case "text.contains":
                return TextContains(node, outputs, token);

            case "state.step":
                return StateStep(node, outputs, token);

            case "vision.changed":
                return VisionChanged(node, outputs, token);

            case "script.run":
                return RunScript(node, outputs, token);

            case "pad.key":
                {
                    var pad = RequirePad();
                    var key = ParseKey(StrParam(node, "key") ?? throw new InvalidOperationException("pad.key 缺少 key"));
                    var duration = IntParam(node, "durationMs", 100);
                    var times = IntParam(node, "times", 1);
                    var interval = IntParam(node, "intervalMs", 100);
                    for (var i = 0; i < times; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        pad.ClickButtons(key, Math.Clamp(duration, 1, 10_000), token);
                        if (i < times - 1 && interval > 0)
                            token.WaitHandle.WaitOne(Math.Clamp(interval, 1, 10_000));
                    }
                    return null;
                }

            case "pad.sequence":
                {
                    var pad = RequirePad();
                    var seq = StrParam(node, "seq") ?? "";
                    foreach (var step in seq.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        token.ThrowIfCancellationRequested();
                        var parts = step.Split(',', StringSplitOptions.TrimEntries);
                        var key = ParseKey(NormalizeKeyName(parts[0]));
                        var dur = parts.Length > 1 && int.TryParse(parts[1], out var d) ? Math.Clamp(d, 1, 10_000) : 100;
                        pad.ClickButtons(key, dur, token);
                        token.WaitHandle.WaitOne(30);
                    }
                    return null;
                }

            case "wait":
                {
                    var ms = IntParam(node, "ms", 0);
                    if (ms > 0)
                        token.WaitHandle.WaitOne(Math.Clamp(ms, 1, MaxWaitMs));
                    return null;
                }

            case "end":
            case "stop":
                return null;

            default:
                throw new InvalidOperationException($"未知节点类型: {node.Type}");
        }
    }

    /// <summary>
    /// script.run：能力集必须经唯一装配点组装；本次运行的能力（采集/OCR/推理/手柄）以
    /// **借用**方式传入（所有权仍属整图运行），否则节点租约释放会拆掉后续节点还在用的服务。
    /// </summary>
    private string? RunScript(FlowNode node, Dictionary<string, object?> outputs, CancellationToken token)
    {
        var file = StrParam(node, "file");
        if (string.IsNullOrWhiteSpace(file))
            throw new InvalidOperationException("script.run 缺少 file 参数");
        var full = Path.IsPathRooted(file) ? file : Path.Combine(context.AppDir, file);
        if (!File.Exists(full))
            throw new InvalidOperationException($"脚本文件不存在: {full}");

        var scriptArgs = new[] { "flow" }
            .Concat(new[] { "arg1", "arg2", "arg3" }.Select(p => StrInput(node, p, token) ?? ""))
            .ToArray();

        var lines = new List<string>();
        var engine = new EasyScriptEngine();
        var session = engine.LoadFile(full, new ScriptHostOptions
        {
            Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false },
        });
        using var lease = ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            Console = new FlowConsole(lines),
            CaptureSource = context.Capture,
            VisionService = context.Vision,
            PadInput = context.Pad,
            Ocr = context.Ocr,
            Inference = context.Inference,
            BorrowResources = true,
            Args = scriptArgs,
            AppDir = context.AppDir,
        });
        session.Run(token, lease.Capabilities);

        outputs["ok"] = 1;
        outputs["logs"] = string.Join("\n", lines);
        return null;
    }

    private IPadInput RequirePad() => context.Pad
        ?? throw new InvalidOperationException("单片机未连接，pad.* 节点不可用");

    /// <summary>regex 匹配上限：图作者写坏正则（灾难性回溯）不得挂死整图。</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>文本判定（决策层）：contains / startsWith / endsWith / equals / regex，双出口。</summary>
    private string TextContains(FlowNode node, Dictionary<string, object?> outputs, CancellationToken token)
    {
        var text = StrInput(node, "text", token) ?? "";
        var pattern = StrInput(node, "pattern", token) ?? StrParam(node, "pattern") ?? "";
        var mode = (StrParam(node, "mode") ?? "contains").ToLowerInvariant();
        var comparison = BoolParam(node, "ignoreCase", true)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        bool hit;
        switch (mode)
        {
            case "contains":
                hit = text.Contains(pattern, comparison);
                break;
            case "startswith":
                hit = text.StartsWith(pattern, comparison);
                break;
            case "endswith":
                hit = text.EndsWith(pattern, comparison);
                break;
            case "equals":
                hit = text.Equals(pattern, comparison);
                break;
            case "regex":
                try
                {
                    hit = Regex.IsMatch(text, pattern,
                        BoolParam(node, "ignoreCase", true) ? RegexOptions.IgnoreCase : RegexOptions.None,
                        RegexTimeout);
                }
                catch (RegexMatchTimeoutException)
                {
                    CoreLog.Warn($"text.contains 正则匹配超时（>{RegexTimeout.TotalMilliseconds}ms）: {pattern}");
                    hit = false;
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException($"text.contains 正则不合法: {ex.Message}");
                }
                break;
            default:
                throw new InvalidOperationException($"text.contains 未知 mode: {mode}");
        }

        outputs["hit"] = hit ? 1 : 0;
        outputs["text"] = text;
        return hit ? "true" : "false";
    }

    /// <summary>
    /// 运行内命名计数器（决策层）：inc/set/reset，达到 max 时走 done 出口。
    /// 状态随本次运行存亡（PC 侧执行状态，不进字节码、不落盘）。
    /// </summary>
    private string StateStep(FlowNode node, Dictionary<string, object?> outputs, CancellationToken token)
    {
        var name = StrParam(node, "name") ?? "step";
        var op = (StrParam(node, "op") ?? "inc").ToLowerInvariant();
        var amount = IntInputOrParam(node, "value", token);
        var max = IntParam(node, "max", 0);

        _counters.TryGetValue(name, out var current);
        current = op switch
        {
            "set" => amount,
            "inc" => current + amount,
            "reset" => 0,
            _ => throw new InvalidOperationException($"state.step 未知 op: {op}（set | inc | reset）"),
        };

        _counters[name] = current;
        outputs["value"] = current;
        outputs["name"] = name;
        return max > 0 && current >= max ? "done" : "out";
    }

    /// <summary>
    /// 画面变化检测（感知层）：与本次运行内上一次观察到的画面比较，双出口。
    /// 首次执行只建立基线（changed = false）——「等待画面变化」的语义下，第一帧不算变化。
    /// 注意：本节点自带状态，不要给它配 slow（复用会跳过基线更新）。
    /// </summary>
    private string VisionChanged(FlowNode node, Dictionary<string, object?> outputs, CancellationToken token)
    {
        var fingerprint = SenseFingerprint(node, token)
            ?? throw new InvalidOperationException("vision.changed 需要 image 输入或已连接的视频源");

        _lastFingerprint.TryGetValue(node.Id, out var previous);
        var changed = previous != null && !string.Equals(previous, fingerprint, StringComparison.Ordinal);
        _lastFingerprint[node.Id] = fingerprint;

        outputs["changed"] = changed ? 1 : 0;
        return changed ? "true" : "false";
    }

    /// <summary>
    /// 等待采集源发布新帧（帧号推进）；源不提供帧号时无法判定，直接取当前帧。
    /// 等待可被停止令牌打断，并在 timeoutMs（缺省 2s）后按当前帧继续——图不会因视频源停帧而挂死。
    /// </summary>
    private void WaitForNewFrame(FlowNode node, ICaptureSource src, CancellationToken token)
    {
        if (src.FrameIndex is not { } baseline)
            return;

        var timeoutMs = Math.Max(1, IntParam(node, "timeoutMs", DefaultWaitNewFrameMs));
        var sw = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var now = src.FrameIndex;
            if (now is null || now > baseline)
                return;
            if (sw.ElapsedMilliseconds >= timeoutMs)
            {
                CoreLog.Warn($"capture.frame waitForNew 等待新帧超时（{timeoutMs}ms），改用当前帧");
                return;
            }
            token.WaitHandle.WaitOne(5);
        }
    }

    private static bool BoolParam(FlowNode node, string name, bool fallback = false) =>
        node.Params.TryGetValue(name, out var el)
        && el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var v) && v,
            _ => fallback,
        };

    /// <summary>slow 复用缓存：上次输出 + 判定基准（帧号/时刻/输入指纹）。</summary>
    private sealed record SlowCacheEntry(
        string? Port, Dictionary<string, object?> Outputs, long? FrameIndex, long StampMs, string? Fingerprint);

    private static GamePadKey ParseKey(string name)
    {
        var normalized = NormalizeKeyName(name);
        if (!Enum.TryParse(normalized, ignoreCase: true, out GamePadKey key) || key == GamePadKey.None)
            throw new InvalidOperationException($"未知按键名: {name}");
        return key;
    }

    private static string NormalizeKeyName(string name) => name.Trim() switch
    {
        "↑" or "UP" => "TOP",
        "↓" or "DOWN" => "DOWN",
        "←" or "LEFT" => "LEFT",
        "→" or "RIGHT" => "RIGHT",
        var k => k,
    };

    private static string CompareValues(object? a, object? b, string op)
    {
        // 数值优先（int/double/可解析字符串），否则字符串序比较
        var da = ToDouble(a);
        var db = ToDouble(b);
        int cmp = da.HasValue && db.HasValue
            ? da.Value.CompareTo(db.Value)
            : string.CompareOrdinal(a?.ToString() ?? "", b?.ToString() ?? "");

        return op switch
        {
            "==" => cmp == 0 ? "true" : "false",
            "!=" => cmp != 0 ? "true" : "false",
            ">" => cmp > 0 ? "true" : "false",
            "<" => cmp < 0 ? "true" : "false",
            ">=" => cmp >= 0 ? "true" : "false",
            "<=" => cmp <= 0 ? "true" : "false",
            _ => throw new InvalidOperationException($"未知比较符: {op}"),
        };
    }

    private static double? ToDouble(object? v) => v switch
    {
        int i => i,
        double d => d,
        string s when double.TryParse(s, out var d) => d,
        _ => null,
    };

    /// <summary>wait 单次上限（毫秒），防呆。</summary>
    private const int MaxWaitMs = 60_000;

    /// <summary>脚本 PRINT/ALERT 捕获（与整图运行同一实现）。</summary>
    private sealed class FlowConsole(List<string> lines) : IConsoleIo
    {
        public void Print(string message, bool newline = true)
        {
            lines.Add(message);
            if (lines.Count > 500)
                lines.RemoveAt(0);
        }

        public void Alert(string message) => lines.Add("[ALERT] " + message);

        public string ReadLine() => "";

        public bool TryReadLine(out string line)
        {
            line = "";
            return false;
        }
    }
}