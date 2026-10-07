using EasyCon.Core.Capabilities;
using EasyCon.Core.LLM.Tools;
using EasyCon.Core.Runner;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyScript;
using System.Collections.Immutable;
using System.Text.Json;

namespace EasyCon.Core.LLM.Agent.Tools;

/// <summary>
/// eval_ecs：即时执行一段 ECS 脚本片段并返回 PRINT 输出。
/// 标准库（NET_IMAGE / PPOCR_TENSOR / PPOCR_NORM / NET_RUNH / OCR 等）自动可用，
/// 使 Agent 能以"调用库函数"的方式对实际采集卡画面做识别与计算——
/// 宿主不提供任何领域特例，与脚本作者走完全相同的代码路径。
///
/// 安全边界：能力集**不装配 Pad**——KEY/STICK 等设备指令运行即失败，
/// 本工具天然只读（感知 + 计算），因此无需人工确认门；
/// 会动设备的动作必须走写脚本 + run_script（已有 RequiresConfirmation 门）。
/// 片段无状态：每次调用独立会话，变量不跨调用保持。
/// </summary>
public class EvalEcsTool : IAiTool
{
    private const int TimeoutSeconds = 15;
    private const int MaxLines = 200;

    private readonly Func<ICaptureSource?> _captureProvider;
    private readonly Func<IOcrService?> _ocrProvider;

    /// <param name="captureProvider">返回实时采集源（GUI 为采集卡帧委托；测试可注入静态图片）。</param>
    /// <param name="ocrProvider">返回宿主 OCR 服务（脚本 OCR_INIT/OCR 通路；可为 null）。</param>
    public EvalEcsTool(Func<ICaptureSource?> captureProvider, Func<IOcrService?> ocrProvider)
    {
        _captureProvider = captureProvider;
        _ocrProvider = ocrProvider;
    }

    public string Name => "eval_ecs";

    public string Description =>
        "即时执行一段 ECS 脚本片段（标准库全量可用：NET_IMAGE/PPOCR_TENSOR/PPOCR_NORM/NET_RUNH/NET_ARGMAX/OCR 等），" +
        "返回 PRINT 输出。画面来自当前采集卡实时帧——适合校准 ROI 坐标、验证识别结果、做数值计算。" +
        "注意：仅感知与计算（无手柄指令，KEY/STICK 不可用）；变量不跨调用保持，请在一段代码内完成取帧→识别→输出。";

    public ToolConcurrency Concurrency => ToolConcurrency.Exclusive;

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["code"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "ECS 语句序列（不含 FUNC 定义头；标准库函数可直接调用）。用 PRINT 输出需要回传的结果。"
            }
        },
        Required = ["code"]
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        var code = args.TryGetValue("code", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? ""
            : "";
        if (string.IsNullOrWhiteSpace(code))
            return Task.FromResult(ToolResult.Error("[错误] 缺少必填参数 code"));

        try
        {
            var engine = new EasyScriptEngine();
            var session = engine.FromSource(code, new ScriptHostOptions
            {
                Compile = new CompileOptions { ExtVars = ImmutableHashSet<string>.Empty, UseDiskCache = false }
            });

            if (session.Info.Image is not { } image)
            {
                var diags = string.Join("\n", session.Info.Diagnostics
                    .Where(d => d.IsError)
                    .Select(d => d.Message)
                    .Take(10));
                return Task.FromResult(ToolResult.Error($"[错误] 片段编译失败:\n{diags}"));
            }

            var lines = new List<string>();
            var caps = new CapabilitySet
            {
                Console = new ConsoleRecorder(lines),
                Capture = _captureProvider(),
                Inference = new DnnInference(),
                Ocr = _ocrProvider(),
            };

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
            try
            {
                EcxVm.Run(image, caps, timeoutCts.Token, null, session.Info.NativeSymbols);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Task.FromResult(ToolResult.Error($"[错误] eval 超时（{TimeoutSeconds} 秒），请简化片段或缩小识别区域。"));
            }
            catch (ScriptException ex)
            {
                return Task.FromResult(ToolResult.Error($"[错误] 运行出错: {ex.Message}"));
            }

            if (lines.Count == 0)
                return Task.FromResult(ToolResult.Ok("(无 PRINT 输出)"));

            var shown = lines.Take(MaxLines);
            var text = string.Join("\n", shown);
            if (lines.Count > MaxLines)
                text += $"\n[输出过长已截断：共 {lines.Count} 行，仅保留前 {MaxLines} 行]";
            return Task.FromResult(ToolResult.Ok(text));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"[错误] eval 执行异常: {ex.Message}"));
        }
    }

    /// <summary>捕获 PRINT/ALERT 输出的轻量 IO 适配。</summary>
    sealed class ConsoleRecorder(List<string> lines) : IConsoleIo
    {
        public void Print(string message, bool newline = true)
        {
            lines.Add(message);
            if (lines.Count > MaxLines + 50)
                lines.RemoveAt(0);   // 防失控脚本撑爆内存；正式截断在结果组装处
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