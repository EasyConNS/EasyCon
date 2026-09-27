using EasyCon.Script.Syntax;
using Bf = EasyCon.Script.Binding.BuiltinFunctions;

namespace EasyCon.Lsp;

/// <summary>
/// 语言关键字与内建函数表——从 EasyCon.Script 的 Lexer 词表与 BuiltinFunctions.Manifest
/// 派生（单一事实源）。手抄副本曾发生双向漂移（提示不存在的 CALL、缺 UNTIL/STRUCT、
/// OCR 签名失真），因此这里禁止再写死任何关键字/函数名；新增语言元素只改 Script 侧。
/// 中文 Doc 是 LSP 特有展示信息，按名映射，缺失时回退通用提示。
/// </summary>
internal static class Constants
{
    static readonly Dictionary<string, string> Docs = new(StringComparer.Ordinal)
    {
        ["WAIT"] = "等待指定毫秒数（默认50ms）",
        ["PRINT"] = "打印消息到控制台",
        ["ALERT"] = "弹窗显示消息",
        ["RAND"] = "生成随机整数（0~max，默认100）",
        ["AMIIBO"] = "模拟Amiibo扫描",
        ["BEEP"] = "发出蜂鸣声",
        ["OCR_CONF"] = "查询/设置 OCR 置信度阈值，返回当前值",
        ["JQ"] = "使用JQ查询语法解析JSON字符串并返回结果",
        ["APPEND"] = "向数组末尾添加元素",
        ["LEN"] = "获取数组长度",
        ["ENCODE"] = "将byte数组转换为字符串(可选type: unicode, utf8)",
        ["INT"] = "将变量转换为数字类型",
        ["STRING"] = "将变量转换为字符串类型",
        ["ENV"] = "读取环境变量",
        ["ARG"] = "读取脚本启动参数",
        ["FOPEN"] = "打开文件，返回句柄",
        ["FREAD"] = "从文件句柄读取内容",
        ["FWRITE"] = "向文件句柄写入，返回写入长度",
        ["FCLOSE"] = "关闭文件句柄",
        ["FEOF"] = "文件是否已读到末尾",
        ["READFILE"] = "读取整个文本文件",
        ["WRITEFILE"] = "覆盖写入文本文件",
        ["APPENDFILE"] = "追加写入文本文件",
        ["FILE_EXISTS"] = "文件是否存在",
        ["NET_LOAD"] = "加载 ONNX 模型，返回网络句柄",
        ["NET_RUN"] = "执行一次推理",
        ["NET_OUT"] = "读取输出张量元素",
    };

    public static readonly string[] Keywords =
    [
        .. Lexer.KeywordNames.Select(k => k.ToUpperInvariant()),
        .. Lexer.LogicWordNames.Select(k => k.ToUpperInvariant()),
    ];

    public static readonly (string Name, string Signature, string Doc)[] BuiltinFunctions =
    [
        .. Bf.Manifest
            .Where(d => !d.Symbol.Name.StartsWith("__", StringComparison.Ordinal))   // 内建洞函数不对用户暴露
            .Select(d =>
            {
                var name = d.Symbol.Name;
                var ps = string.Join(", ", d.Symbol.Parameters.Select(p => p.HasDefaultValue ? $"{p.Name}?" : p.Name));
                var sig = ps.Length > 0 ? $"{name} {ps}" : $"{name}()";
                return (name, sig, Docs.GetValueOrDefault(name, "内建函数"));
            }),
    ];

    public static readonly string[] FfiTypes = ["INT", "BOOL", "STRING", "VOID", "PTR", "DOUBLE", "BYTE", "UINT", "UINT64"];

    public static readonly string[] GamepadKeys = [.. Lexer.GamepadKeywordNames];
    public static readonly string[] StickKeys = [.. Lexer.StickKeywordNames];
    public static readonly string[] Directions = [.. Lexer.DirectionKeywordNames];
    public static readonly string[] KeyMods = ["UP", "DOWN"];
}