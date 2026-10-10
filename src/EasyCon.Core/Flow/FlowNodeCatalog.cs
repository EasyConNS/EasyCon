using EasyCon.Script;
using EasyScript;
using System.Text.Json;

namespace EasyCon.Core.Flow;

/// <summary>节点端口（exec 控制流或数据流）。</summary>
/// <param name="Name">端口名（数据入边的键；exec 端口仅作语义标注）。</param>
/// <param name="Kind">exec-in | exec-out | data-in | data-out。</param>
/// <param name="Type">数据类型：exec | image | text | number | bool | any。</param>
/// <param name="Description">中文说明（前端 tooltip）。</param>
public sealed record FlowPortSpec(string Name, string Kind, string Type, string Description);

/// <summary>节点参数说明（前端据此生成表单控件）。</summary>
public sealed record FlowParamSpec(
    string Name,
    string Type,
    string Description,
    JsonElement? Default = null,
    IReadOnlyList<string>? Options = null);

/// <summary>节点类型说明。Layer 决定试跑许可与前端默认分组。</summary>
public sealed record FlowNodeSpec(
    string Type,
    string Layer,
    string Summary,
    IReadOnlyList<FlowPortSpec> Ports,
    IReadOnlyList<FlowParamSpec> Params);

/// <summary>
/// 编排节点目录——**前端画布渲染与后端执行共用的单一事实源**。
///
/// <para>
/// 新增节点必须同时登记到本目录与 <see cref="FlowNodeRuntime"/>；
/// <c>FlowNodeCatalogTests</c> 会遍历目录逐个执行，任何「目录里有、运行时没有」的节点都会被抓出来。
/// </para>
/// <para>
/// layer 语义：flow（控制流）/ sense（感知）/ decision（决策）/ actuation（驱动设备，试跑禁止）。
/// </para>
/// </summary>
public static class FlowNodeCatalog
{
    /// <summary>层名常量。</summary>
    public const string LayerFlow = "flow";
    public const string LayerSense = "sense";
    public const string LayerDecision = "decision";
    public const string LayerActuation = "actuation";

    private static FlowPortSpec ExecIn() => new("in", "exec-in", "exec", "控制流入口");

    private static FlowPortSpec ExecOut(string name = "out", string description = "控制流出口") =>
        new(name, "exec-out", "exec", description);

    private static FlowParamSpec P(string name, string type, string description, object? @default = null,
        IReadOnlyList<string>? options = null) =>
        new(name, type, description,
            @default is null ? null : JsonSerializer.SerializeToElement(@default, FlowGraph.JsonOpts),
            options);

    /// <summary>全部已实现节点（顺序即前端分组内展示顺序）。</summary>
    public static IReadOnlyList<FlowNodeSpec> Nodes { get; } =
    [
        new(Layer: LayerFlow, Type: "start", Summary: "起点（图执行的入口）",
            Ports: [ExecOut()], Params: []),

        new(Layer: LayerFlow, Type: "end", Summary: "终点（正常结束）",
            Ports: [ExecIn()], Params: []),

        new(Layer: LayerFlow, Type: "wait", Summary: "延时",
            Ports: [ExecIn(), ExecOut()],
            Params: [P("ms", "int", "延时毫秒（上限 60000，可被停止令牌打断）", 100)]),

        new(Layer: LayerSense, Type: "capture.frame", Summary: "抓取采集卡当前帧",
            Ports:
            [
                ExecIn(), ExecOut(),
                new("image", "data-out", "image", "Base64 PNG 画面"),
                new("frameIndex", "data-out", "number", "帧号（采集源不提供时为 null）"),
                new("x", "data-in", "number", "ROI 左边界（缺省整帧）"),
                new("y", "data-in", "number", "ROI 上边界（缺省整帧）"),
                new("w", "data-in", "number", "ROI 宽（缺省整帧）"),
                new("h", "data-in", "number", "ROI 高（缺省整帧）"),
            ],
            Params:
            [
                P("waitForNew", "bool", "等待采集源发布新帧后再抓（需帧号；超时按当前帧继续）", false),
                P("timeoutMs", "int", "等待新帧上限毫秒", 2000),
            ]),

        new(Layer: LayerSense, Type: "image.file", Summary: "读取单张图片文件",
            Ports: [ExecIn(), ExecOut(), new("image", "data-out", "image", "Base64 PNG 画面")],
            Params: [P("path", "path", "图片路径（相对路径按 __APP__ 解析）")]),

        new(Layer: LayerSense, Type: "ocr.text", Summary: "OCR 识别文字（整图或 ROI）",
            Ports:
            [
                ExecIn(), ExecOut(),
                new("image", "data-in", "image", "输入画面"),
                new("x", "data-in", "number", "ROI 左边界"),
                new("y", "data-in", "number", "ROI 上边界"),
                new("w", "data-in", "number", "ROI 宽（0 = 到边界）"),
                new("h", "data-in", "number", "ROI 高（0 = 到边界）"),
                new("text", "data-out", "text", "识别文本"),
                new("conf", "data-out", "number", "置信度（0-100）"),
            ],
            Params:
            [
                P("lang", "string", "语言/模型选择（tesseract 语言包名或 PP-OCR 模型前缀）", "eng"),
                P("x", "int", "ROI 左边界", 0),
                P("y", "int", "ROI 上边界", 0),
                P("w", "int", "ROI 宽（0 = 到边界）", 0),
                P("h", "int", "ROI 高（0 = 到边界）", 0),
            ]),

        new(Layer: LayerDecision, Type: "compare", Summary: "比较两个值，true/false 双出口",
            Ports:
            [
                ExecIn(),
                ExecOut("true", "条件成立"),
                ExecOut("false", "条件不成立"),
                new("a", "data-in", "any", "左值"),
                new("b", "data-in", "any", "右值"),
            ],
            Params: [P("op", "enum", "比较运算符", "==", ["==", "!=", ">", "<", ">=", "<="])]),

        new(Layer: LayerDecision, Type: "text.contains", Summary: "文本判定（包含/前缀/后缀/相等/正则），双出口",
            Ports:
            [
                ExecIn(),
                ExecOut("true", "命中"),
                ExecOut("false", "未命中"),
                new("text", "data-in", "text", "待判定文本（常接 ocr.text 的 text）"),
                new("pattern", "data-in", "text", "匹配内容"),
                new("hit", "data-out", "number", "命中标记（1/0）"),
            ],
            Params:
            [
                P("mode", "enum", "判定方式", "contains", ["contains", "startsWith", "endsWith", "equals", "regex"]),
                P("pattern", "string", "匹配内容（与 pattern 入边二选一，入边优先）", ""),
                P("ignoreCase", "bool", "忽略大小写", true),
            ]),

        new(Layer: LayerDecision, Type: "state.step", Summary: "运行内命名计数器（循环/重试），达到 max 走 done",
            Ports:
            [
                ExecIn(),
                ExecOut("out", "未达上限"),
                ExecOut("done", "已达上限"),
                new("value", "data-in", "number", "增量 / 设定值"),
                new("value", "data-out", "number", "计数后的当前值"),
                new("name", "data-out", "text", "计数器名"),
            ],
            Params:
            [
                P("name", "string", "计数器名（同一次运行内共享）", "step"),
                P("op", "enum", "操作", "inc", ["inc", "set", "reset"]),
                P("value", "int", "增量（inc）或设定值（set）", 1),
                P("max", "int", "达到该值走 done 出口（0 = 永不触发）", 0),
            ]),

        new(Layer: LayerSense, Type: "vision.changed", Summary: "与上次观察到的画面比较，双出口",
            Ports:
            [
                ExecIn(),
                ExecOut("true", "画面已变化"),
                ExecOut("false", "画面未变（含首次建立基线）"),
                new("image", "data-in", "image", "输入画面（缺省取当前帧）"),
                new("changed", "data-out", "number", "变化标记（1/0）"),
            ],
            Params: []),

        new(Layer: LayerActuation, Type: "script.run", Summary: "执行 ECS 脚本（内联多行或 .ecs 文件；ARG 传参，PRINT 回传）",
            Ports:
            [
                ExecIn(), ExecOut(),
                new("arg1", "data-in", "text", "ARG(1)"),
                new("arg2", "data-in", "text", "ARG(2)"),
                new("arg3", "data-in", "text", "ARG(3)"),
                new("ok", "data-out", "number", "执行完成标记（1）"),
                new("logs", "data-out", "text", "PRINT 输出（\\n 连接）"),
            ],
            Params:
            [
                P("script", "text", "内联 ECS 脚本（多行文本，非空时优先于 file；无脚本目录上下文，不支持模块导入）"),
                P("file", "path", "脚本路径（相对路径按 __APP__ 解析；与 script 二选一）"),
            ]),

        new(Layer: LayerActuation, Type: "pad.key", Summary: "按键",
            Ports: [ExecIn(), ExecOut()],
            Params:
            [
                P("key", "enum", "键名（A/B/X/Y/L/R/ZL/ZR/PLUS/MINUS/HOME/CAPTURE/TOP/DOWN/LEFT/RIGHT/LS/RS）", "A"),
                P("durationMs", "int", "按住毫秒", 100),
                P("times", "int", "重复次数", 1),
                P("intervalMs", "int", "重复间隔毫秒", 100),
            ]),

        new(Layer: LayerActuation, Type: "pad.sequence", Summary: "按键序列宏（\"A,100; ↓,200\"）",
            Ports: [ExecIn(), ExecOut()],
            Params: [P("seq", "string", "序列：键名,毫秒 以 ; 分隔；方向键可写 ↑↓←→")]),
    ];

    /// <summary>节点级 slow 策略的字段说明（所有节点通用，写在节点上而非 params 里）。</summary>
    public static IReadOnlyList<FlowParamSpec> SlowFields { get; } =
    [
        P("everyFrames", "int", "帧号推进不足 N 帧即复用上次输出（需采集源提供帧号）"),
        P("onChange", "bool", "感知输入未变化即复用（指纹取 image 入边，缺省取当前帧）"),
        P("intervalMs", "int", "距上次执行不足 N 毫秒即复用"),
    ];

    /// <summary>手柄键名（前端下拉选项来源）。</summary>
    public static IReadOnlyList<string> KeyNames { get; } =
        [.. Enum.GetNames<GamePadKey>().Where(n => n != nameof(GamePadKey.None))];

    /// <summary>OCR 后端选项（服务侧装配开关的取值）。</summary>
    public static IReadOnlyList<string> OcrBackends { get; } = ["none", "tesseract", "ppocr"];

    /// <summary>按类型查目录项。</summary>
    public static FlowNodeSpec? Find(string type) => Nodes.FirstOrDefault(n => n.Type == type);

    /// <summary>该类型是否允许单节点试跑（actuation 层会驱动设备，禁止）。</summary>
    public static bool IsTrialAllowed(string type) =>
        Find(type)?.Layer != LayerActuation;

    /// <summary>目录 JSON（/api/nodes 与 MCP flow_device_query(kind=nodes) 共用）。</summary>
    public static string Json() => JsonSerializer.Serialize(Payload(), FlowGraph.JsonOpts);

    /// <summary>目录载荷（结构化形式，便于测试与复用）。</summary>
    public static object Payload() => new
    {
        version = 1,
        nodes = Nodes,
        slow = SlowFields,
        keys = KeyNames,
        ocrBackends = OcrBackends,
    };
}