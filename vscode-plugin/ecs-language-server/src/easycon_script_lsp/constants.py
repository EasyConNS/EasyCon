"""Constants for EasyCon Script language server."""

ECS_KEYWORDS = [
    "IMPORT",
    "EXTERN",
    "FROM",
    "IF",
    "ELIF",
    "ELSE",
    "ENDIF",
    "WHILE",
    "FOR",
    "TO",
    "IN",
    "STEP",
    "BREAK",
    "CONTINUE",
    "NEXT",
    "FUNC",
    "RETURN",
    "ENDFUNC",
    "END",
    "TRUE",
    "FALSE",
    "RESET",
    "WAIT",
    "CALL",
]

ECS_FFI_TYPES = ["INT", "BOOL", "STRING", "VOID", "PTR", "DOUBLE"]

ECS_BUILTIN_FUNCTIONS = [
    "WAIT",
    "PRINT",
    "ALERT",
    "RAND",
    "TIME",
    "LEN",
    "APPEND",
    "BEEP",
    "AMIIBO",
]

ECS_BUTTON_KEYS = [
    "LCLICK",
    "RCLICK",
    "CAPTURE",
    "MINUS",
    "HOME",
    "PLUS",
    "ZL",
    "ZR",
    "L",
    "R",
    "A",
    "B",
    "X",
    "Y",
    "UP",
    "DOWN",
    "LEFT",
    "RIGHT",
]

ECS_STICK_KEYS = ["LS", "RS"]

ECS_DIRECTION_KEYS = [
    "DOWNLEFT",
    "DOWNRIGHT",
    "UPLEFT",
    "UPRIGHT",
    "UP",
    "DOWN",
    "LEFT",
    "RIGHT",
]

ECS_KEY_MODS = ["DOWN", "UP"]

ECS_FILE_EXTENSION = ".ecs"
ECS_LANGUAGE_ID = "easycon-script"

BUILTIN_DOCS = {
    "WAIT": "暂停执行指定的时间（毫秒）。例如：`WAIT 1000` 暂停 1 秒。",
    "PRINT": "输出信息到控制台。例如：`PRINT \"Hello\"`。",
    "ALERT": "显示弹窗提示。例如：`ALERT \"Message\"`。",
    "RAND": "生成随机数。例如：`RAND(10)` 返回 0~9 的随机整数。",
    "TIME": "返回当前时间戳（毫秒）。",
    "LEN": "返回数组长度。例如：`LEN($arr)`。",
    "APPEND": "向数组末尾添加元素。例如：`APPEND($arr, 1)`。",
    "BEEP": "蜂鸣器发声。例如：`BEEP 1000, 200` 以 1kHz 频率鸣叫 200ms。",
    "AMIIBO": "切换 Amiibo 槽位（ESP32 专属）。例如：`AMIIBO 0`。",
}

KEYWORD_DOCS: dict[str, str] = {
    "WAIT": "延时等待：脚本在此停顿，直到设定的时间过去后继续执行。",
    "IMPORT": "引入模块：将另一个脚本文件的功能导入当前脚本。",
    "IF": "条件判断：如果条件成立，执行下方操作；不成立则跳过。",
    "ELIF": "否则如果：在前一个条件不成立时，判断另一个条件。",
    "ELSE": "否则：当以上所有条件都不成立时，执行的操作。",
    "ENDIF": "结束条件判断块。",
    "WHILE": "循环：只要条件成立，就反复执行内部操作。",
    "FOR": "计数循环：按照设定的次数或范围重复执行。",
    "TO": "配合 FOR 使用，指定循环的结束值。",
    "IN": "配合 FOR 使用，在集合中逐个取出元素。",
    "STEP": "配合 FOR 使用，指定每次循环的增量步长。",
    "BREAK": "跳出：立即退出当前循环，继续执行循环后的操作。",
    "CONTINUE": "跳过：跳过本次循环的剩余步骤，进入下一次循环。",
    "NEXT": "进入下一次循环迭代。",
    "FUNC": "定义动作：创建一个自定义动作，后续可用 CALL 来执行。",
    "RETURN": "返回：从自定义动作中返回结果，并结束该动作。",
    "ENDFUNC": "结束自定义动作的定义。",
    "END": "结束当前的循环或代码块。",
    "TRUE": "真：表示条件成立。",
    "FALSE": "假：表示条件不成立。",
    "RESET": "复位：重置所有按键状态，释放所有按下的按键。",
    "CALL": "调用：执行一个自定义动作或内置功能。",
    "EXTERN": "外部函数声明：声明一个来自原生动态链接库（DLL）的外部函数，配合 FUNC 和 FROM 使用。",
    "FROM": "指定库路径：在 EXTERN FUNC 声明中指定外部函数所在的 DLL 文件路径。",
}
