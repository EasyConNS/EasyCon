# 脚本函数手册

> 面向脚本编写者的函数使用说明：覆盖脚本可直接调用的全部内置函数与 stdlib 函数。
> 每个函数给出签名、参数说明、返回值与示例。

## 阅读约定

- 类型标注：`INT` 整数、`DOUBLE` 小数、`BOOL` 布尔、`STRING` 字符串、`BYTE[]` 字节数组
- `[参数]` 表示可选参数，省略时使用默认值
- 两种调用写法都合法：
  - 语句式（不接返回值）：`WAIT 100`、`ALERT "你好"`
  - 表达式（接返回值）：`$x = LEN($arr)`
- 平台标注：【桌面】= GUI / CLI 运行；【单片机】= 编译 `.ecx` 烧录后运行

---

## 1. 输出与提醒

### PRINT([message])
向运行日志输出一行。

| 参数 | 类型 | 说明 |
|------|------|------|
| message | STRING | 要输出的内容，可省略（省略时输出空行） |

```ecs
PRINT "开始执行"
$x = 5
PRINT $x                 # 输出 5
PRINT "计数: " & $x      # 用 & 拼接，输出 计数: 5
```

- 参数按"文本片段"解析：`$变量`、`"字符串"`、`&` 拼接直接求值；
  函数调用与数组索引的结果，先存入变量再打印。
- 文本以 `\` 结尾表示续行：下一段输出接在同一行。

### ALERT(message)
弹出提醒（GUI 弹窗/推送）。单片机上静默忽略。

| 参数 | 类型 | 说明 |
|------|------|------|
| message | STRING | 提醒内容；行尾 `\` 续行规则与 PRINT 相同 |

```ecs
ALERT "任务完成"
```

### BEEP(freq, duration)
蜂鸣。仅 Windows 桌面发声；其他平台静默。

| 参数 | 类型 | 说明 |
|------|------|------|
| freq | INT | 频率，37~32767 Hz（超出范围运行时报错） |
| duration | INT | 时长，毫秒 |

```ecs
BEEP 1000, 200
```

---

## 2. 延时与时间

### WAIT([duration = 50])
延时指定毫秒——按键之间留出游戏响应时间最常用的函数。

| 参数 | 类型 | 说明 |
|------|------|------|
| duration | INT | 延时毫秒数，可省略（默认 50） |

```ecs
WAIT          # 默认 50ms
WAIT 1000     # 1 秒
```

### TIME(): INT
本次脚本运行已经过的毫秒数（计时/限帧用）。单片机上恒为 0。

| 参数 | 说明 |
|------|------|
| （无参数） | |

```ecs
$start = TIME()
... do something ...
$cost = TIME() - $start
```

---

## 3. 随机

### RAND([max = 100]): INT
返回 `[0, max)` 的随机整数（不包含 max）。

| 参数 | 类型 | 说明 |
|------|------|------|
| max | INT | 上界（不含），可省略（默认 100） |

```ecs
$r = RAND(10)          # 0~9
$w = 500 + RAND(500)
WAIT $w                # 随机延时 500~999ms（两步写法，见"实用要点"）
```

---

## 4. 脚本参数与环境

### ARG(index): STRING
取第 `index` 个脚本参数（从 0 开始）。越界返回空串。

| 参数 | 类型 | 说明 |
|------|------|------|
| index | INT | 参数序号，0 表示第一个参数 |

```ecs
$name = ARG(0)
PRINT $name
```
- CLI 传参：`ecs-run 脚本.ecs -- 参数1 参数2`；GUI 运行时也可填参数。

### ENV(name): STRING
读取环境变量，不存在返回空串。

| 参数 | 类型 | 说明 |
|------|------|------|
| name | STRING | 环境变量名 |

```ecs
$home = ENV("HOME")
```

---

## 5. Amiibo

### AMIIBO(index)
切换到手柄的 Amiibo 槽位。`index > 9` 时静默忽略。

| 参数 | 类型 | 说明 |
|------|------|------|
| index | INT | 槽位号（0~9） |

```ecs
AMIIBO 3
WAIT 500
```

---

## 6. 图像识别与截屏【桌面，需连接采集卡】

本章能力面向桌面运行；单片机端专注纯按键/延时类自动化（脚本能力在加载期自动识别）。

### @标签名 —— 找图/找色
`@名称` 返回该标签的匹配度（0~100 整数）。标签文件放在脚本同目录 `imglabel/` 下。

| 参数 | 说明 |
|------|------|
| 标签名 | `imglabel/` 目录下的标签文件名（不含扩展名） |

```ecs
$score = @闪光特征
IF @确认按钮 > 80
    A
    WAIT 300
ENDIF
```

### FRAME([x, y, w, h]): STRING
截取当前画面，返回 Base64 PNG 图像。

| 参数 | 类型 | 说明 |
|------|------|------|
| x, y | INT | 区域左上角坐标；四个参数都省略时截取整屏 |
| w, h | INT | 区域宽高 |

```ecs
$shot = FRAME()
$part = FRAME(100, 200, 300, 150)
```

### ROI(img, x, y, w, h): STRING
从一张 Base64 图像中裁出子区域，返回新的 Base64 PNG。
常用于：先 `FRAME()` 截整屏，再对同一张图反复 `ROI` 分析。

| 参数 | 类型 | 说明 |
|------|------|------|
| img | STRING | 源图像（Base64 PNG） |
| x, y | INT | 子区域左上角坐标 |
| w, h | INT | 子区域宽高 |

```ecs
$shot = FRAME()
$head = ROI($shot, 800, 100, 300, 200)
```

### OCR(x, y, w, h) / OCR(x, y, w, h, lang): STRING
识别画面指定区域的文字，返回文本。语言缺省 `chi_sim`（简中）。
首次识别自动按语言初始化引擎，无需手动调用 OCR_INIT。

| 参数 | 类型 | 说明 |
|------|------|------|
| x, y | INT | 识别区域左上角坐标 |
| w, h | INT | 识别区域宽高 |
| lang | STRING | 识别语言（5 参版本）；如 `"chi_sim"`、`"eng"` |

```ecs
$text = OCR(100, 200, 400, 60, "eng")
IF $text != ""
    PRINT $text
ENDIF
```

### OCR_INIT(lang): BOOL
预初始化某语言的识别引擎（使用运行环境的默认模型目录）。
普通脚本直接使用 OCR 即可；本函数用于提前热身、避免首次识别的初始化等待。

| 参数 | 类型 | 说明 |
|------|------|------|
| lang | STRING | 识别语言 |

```ecs
OCR_INIT "chi_sim"
```

### OCR_CONF(): INT
最近一次 OCR 的置信度（0~100）。

| 参数 | 说明 |
|------|------|
| （无参数） | |

```ecs
$t = OCR(0, 0, 400, 60)
$c = OCR_CONF()
IF $c < 60
    PRINT "识别不可靠: " & $t
ENDIF
```

---

## 7. 数组、字符串与转换

### LEN(v): INT
字符串长度或数组元素个数。

| 参数 | 类型 | 说明 |
|------|------|------|
| v | STRING / 数组 | 被测量的字符串或数组 |

```ecs
$n = LEN("abc")        # 3
$m = LEN($arr)         # 数组元素数
```

### APPEND(arr, value)
向数组追加元素，**返回新数组**（原数组保持不变，需要接住返回值）。

| 参数 | 类型 | 说明 |
|------|------|------|
| arr | 数组 | 源数组 |
| value | 与数组元素同类型 | 追加的元素 |

```ecs
$arr = [1, 2, 3]
$arr = APPEND($arr, 4)     # [1, 2, 3, 4]
```

### STRING(v): STRING
转为字符串（整数/小数/布尔均可）。

| 参数 | 类型 | 说明 |
|------|------|------|
| v | 任意数值/布尔/字符串 | 被转换的值 |

```ecs
$s = STRING(42)      # "42"
```

### INT(v): INT
转为整数：数值直接截断取整（`3.9` → `3`）；数字字符串直接解析
（`"123"` → `123`，支持前后空白与正负号）；解析失败时返回 `0`。

| 参数 | 类型 | 说明 |
|------|------|------|
| v | 数值 / STRING | 被转换的值 |

```ecs
$a = INT("123")     # 123
$b = INT(3.9)       # 3
$c = INT("abc")     # 0
```

### ENCODE(bytes, [type = "utf8"]): STRING
把字节数组按编码还原成字符串。

| 参数 | 类型 | 说明 |
|------|------|------|
| bytes | BYTE[] | 字节数组 |
| type | STRING | 编码名：`"utf8"`（默认）或 `"unicode"` |

```ecs
$bytes = [72, 101, 108, 108, 111]
$s = ENCODE($bytes)        # "Hello"
```

### JQ(json, query): STRING
用路径表达式查询 JSON 文本。路径：`.字段名` 取字段、`[序号]` 取数组元素，可连写。
解析失败返回空串。

| 参数 | 类型 | 说明 |
|------|------|------|
| json | STRING | JSON 文本 |
| query | STRING | 路径表达式，如 `.data.list[0].name` |

```ecs
$j = JQ("{\"a\":{\"b\":[10,20]}}", ".a.b[1]")    # "20"
```

---

## 8. 文件与标准流

标准流句柄：`1` = 运行日志、`2` = 错误通道、`0` = 控制台输入。

### FWRITE(handle, data): INT
向目标通道写数据，返回写入长度。
- `handle = 1`：输出到运行日志（PRINT 的底层就是它），行尾 `\` 续行规则相同；
- `handle = 2`：输出到错误通道；
- 其余句柄值属于文件句柄族，为后续版本预留。

| 参数 | 类型 | 说明 |
|------|------|------|
| handle | INT | 目标通道句柄 |
| data | STRING | 写入的文本 |

```ecs
FWRITE 1, "写进运行日志"
FWRITE 2, "写进错误通道"
```

### FREAD(handle, count): STRING
从目标通道读文本。
- `handle = 0`：从控制台读一行（交互式脚本用；GUI 下无输入源时立即返回空串）；
- 其余句柄值属于文件句柄族，为后续版本预留。

| 参数 | 类型 | 说明 |
|------|------|------|
| handle | INT | 来源通道句柄 |
| count | INT | 读取字符数；`0` 或负数表示读全部 |

```ecs
$line = FREAD(0, 0)
```

### 文件句柄族（后续版本开放）
`FOPEN(path, mode)` / `FCLOSE(handle)` / `FEOF(handle)` /
`READFILE(path)` / `WRITEFILE(path, data)` / `APPENDFILE(path, data)` /
`FILE_EXISTS(path)` 为文件读写预留接口，当前版本开放的是上面的标准流读写。
涉及外部数据时，现阶段推荐通过脚本参数（`ARG`）等宿主通道传入。

| 函数 | 参数 | 返回 |
|------|------|------|
| FOPEN | path：STRING 文件路径；mode：STRING 打开方式（`"r"` 读 / `"w"` 写 / `"a"` 追加） | INT 句柄 |
| FCLOSE | handle：INT 文件句柄 | - |
| FEOF | handle：INT 文件句柄 | BOOL 是否到末尾 |
| READFILE | path：STRING 文件路径 | STRING 全部内容 |
| WRITEFILE | path：STRING 文件路径；data：STRING 内容 | - |
| APPENDFILE | path：STRING 文件路径；data：STRING 追加内容 | - |
| FILE_EXISTS | path：STRING 文件路径 | BOOL 是否存在 |

---

## 9. ONNX 推理实验【桌面，GUI 已内置推理能力】

> 实验功能。脚本带 VISION 标记，面向桌面运行；单片机加载期即识别为桌面专属脚本。
> 推理结果与具体模型强相关，模型文件由运行环境提供。

### NET_LOAD(path): INT
加载 ONNX 模型，返回会话句柄；失败返回 -1。

| 参数 | 类型 | 说明 |
|------|------|------|
| path | STRING | 模型文件路径 |

```ecs
$net = NET_LOAD("models/demo.onnx")
```

### NET_RUN(net, inputArr): INT
把数值数组送入模型推理，返回**输出元素个数**（失败返回 0）。

| 参数 | 类型 | 说明 |
|------|------|------|
| net | INT | NET_LOAD 返回的会话句柄 |
| inputArr | 数组 | 输入数据（数值数组，展平成一维） |

```ecs
$in = [1, 2, 3]
$n = NET_RUN($net, $in)
```

### NET_OUT(index): DOUBLE
读取最近一次推理输出的第 `index` 个元素（越界返回 0）。

| 参数 | 类型 | 说明 |
|------|------|------|
| index | INT | 输出元素下标（从 0 开始） |

设计为"标量协议"：先跑 NET_RUN 拿个数，再逐个 NET_OUT 读取。

```ecs
IF $net >= 0
    $in = [1, 2, 3]
    $n = NET_RUN($net, $in)
    $i = 0
    WHILE $i < $n
        $v = NET_OUT($i)
        PRINT $v
        $i = $i + 1
    END
ENDIF
```

---

## 10. 实用要点

1. **PRINT/ALERT 的参数是"文本片段"**：`$变量`、`"字符串"`、`&` 拼接直接可用；
   函数调用（如 `ARG(0)`）、数组索引（如 `$a[0]`）的结果先存入变量再打印。
2. **APPEND 返回新数组**：写 `$a = APPEND($a, 1)` 让追加生效。
3. **INT 兼顾数值与字符串**：`INT(3.9)` 截断为 `3`，`INT("123")` 解析为 `123`，
   解析失败时返回 `0`。
4. **布尔值传参两步走**：先存入 BOOL 变量（`$flag = 1 > 0`），再传入函数。
5. **`\` 结尾 = 续行**：PRINT/ALERT/FWRITE 的文本以反斜杠结尾时，下一段输出接在同一行。
6. **OCR 自动初始化**：`OCR(...)` 按语言自动初始化引擎；OCR_INIT 用于提前热身。
7. **桌面/单片机各展所长**：含 `@标签`、`FRAME`、`OCR`、`ROI`、`NET_*` 的脚本面向桌面运行；
   单片机端专注纯按键/延时自动化（加载期即识别脚本类型）。
8. **随机延时分两步**：`$w = 500 + RAND(500)` → `WAIT $w`。

---

## 11. 函数速查表

| 函数 | 作用 | 返回 | 桌面 | 单片机 |
|------|------|------|:----:|:------:|
| PRINT([msg]) | 输出日志 | - | ✔ | ✔ |
| ALERT(msg) | 弹出提醒 | - | ✔ | 静默 |
| BEEP(freq, ms) | 蜂鸣（仅 Windows） | - | ✔ | 静默 |
| WAIT([ms=50]) | 延时 | - | ✔ | ✔ |
| TIME() | 运行经过毫秒 | INT | ✔ | 恒 0 |
| RAND([max=100]) | 随机整数 [0,max) | INT | ✔ | ✔ |
| ARG(i) | 第 i 个脚本参数 | STRING | ✔ | ✔ |
| ENV(name) | 环境变量 | STRING | ✔ | ✔ |
| AMIIBO(i) | 切换 Amiibo 槽位 | - | ✔ | ✔ |
| LEN(v) | 字符串/数组长度 | INT | ✔ | ✔ |
| APPEND(arr, v) | 追加元素（返回新数组） | ARRAY | ✔ | ✔ |
| STRING(v) | 转字符串 | STRING | ✔ | ✔ |
| INT(v) | 转整数（数值截断/数字字符串解析） | INT | ✔ | ✔ |
| ENCODE(bytes,[enc]) | 字节数组 → 字符串 | STRING | ✔ | — |
| JQ(json, path) | JSON 查询 | STRING | ✔ | — |
| @标签名 | 图像匹配度 | INT | ✔ | 镜像拒跑 |
| FRAME([x,y,w,h]) | 截屏 Base64 PNG | STRING | ✔ | 镜像拒跑 |
| ROI(img,x,y,w,h) | 裁剪图像 | STRING | ✔ | 镜像拒跑 |
| OCR(x,y,w,h[,lang]) | 识别文字 | STRING | ✔ | 镜像拒跑 |
| OCR_INIT(lang) | 预初始化识别引擎 | BOOL | ✔ | 镜像拒跑 |
| OCR_CONF() | 最近识别置信度 | INT | ✔ | — |
| FWRITE(h, data) | 写通道（1 日志 / 2 错误） | INT | ✔ | ✔ |
| FREAD(h, count) | 读通道（h=0 读一行输入） | STRING | ✔ | 空串 |
| FOPEN/FCLOSE/FEOF | 文件句柄（后续版本开放） | - | 预留 | — |
| READFILE/WRITEFILE/APPENDFILE/FILE_EXISTS | 整文件读写（后续版本开放） | - | 预留 | — |
| NET_LOAD(path) | 加载 ONNX 模型 | INT | ✔ | 镜像拒跑 |
| NET_RUN(net, in) | 推理，返回输出个数 | INT | ✔ | 镜像拒跑 |
| NET_OUT(i) | 读第 i 个输出 | DOUBLE | ✔ | 镜像拒跑 |

> - 桌面 = GUI / CLI 运行；单片机 = 编译 .ecx 烧录运行。
> - "镜像拒跑"：桌面专属能力在镜像中带有标记，单片机加载期即识别并拒绝运行——确保桌面/单片机脚本各得其所。
> - "静默"：调用不产生任何效果、不报错。
> - "—"：该平台未提供此函数。
