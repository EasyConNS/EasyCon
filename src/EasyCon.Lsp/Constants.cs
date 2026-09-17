namespace EasyCon.Lsp;

internal static class Constants
{
    public static readonly string[] Keywords =
    [
        "IMPORT", "IF", "ELIF", "ELSE", "ENDIF",
        "WHILE", "END", "FOR", "TO", "IN", "STEP",
        "BREAK", "CONTINUE", "NEXT",
        "FUNC", "ENDFUNC", "RETURN",
        "EXTERN", "AS", "FROM",
        "TRUE", "FALSE", "RESET",
        "AND", "OR", "NOT",
        "CALL",
    ];

    public static readonly (string Name, string Signature, string Doc)[] BuiltinFunctions =
    [
        ("WAIT", "WAIT duration?", "等待指定毫秒数（默认50ms）"),
        ("PRINT", "PRINT message?", "打印消息到控制台"),
        ("ALERT", "ALERT message", "弹窗显示消息"),
        ("RAND", "RAND max?", "生成随机整数（0~max，默认100）"),
        ("TIME", "TIME()", "获取当前时间戳"),
        ("AMIIBO", "AMIIBO index", "模拟Amiibo扫描"),
        ("BEEP", "BEEP freq, duration", "发出蜂鸣声"),
        ("OCR", "OCR x, y, width, height", "对指定区域进行光学字符识别，返回识别出的文本"),
        ("JQ", "JQ json, query", "使用JQ查询语法解析JSON字符串并返回结果"),
        ("APPEND", "APPEND array, value", "向数组末尾添加元素"),
        ("LEN", "LEN var", "获取数组长度"),
        ("ENCODE", "STRING array [, type]", "将byte数组转换为字符串(可选type: unicode, utf8)"),
        ("INT", "INT var", "将变量转换为数字类型"),
        ("STRING", "STRING var", "将变量转换为字符串类型"),
    ];

    public static readonly string[] FfiTypes = ["INT", "BOOL", "STRING", "VOID", "PTR", "DOUBLE", "BYTE", "UINT", "UINT64"];

    public static readonly string[] GamepadKeys =
    [
        "A", "B", "X", "Y", "L", "R", "ZL", "ZR",
        "MINUS", "PLUS", "HOME", "CAPTURE", "LCLICK", "RCLICK",
        "UP", "DOWN", "LEFT", "RIGHT",
        "DOWNLEFT", "DOWNRIGHT", "UPLEFT", "UPRIGHT",
    ];

    public static readonly string[] StickKeys = ["LS", "RS"];
    public static readonly string[] Directions = ["UP", "DOWN", "LEFT", "RIGHT", "DOWNLEFT", "DOWNRIGHT", "UPLEFT", "UPRIGHT"];
    public static readonly string[] KeyMods = ["UP", "DOWN"];
}