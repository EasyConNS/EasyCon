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
向运行日志写入提醒，并通过桌面宿主配置的通知渠道推送。单片机上静默忽略。

| 参数 | 类型 | 说明 |
|------|------|------|
| message | STRING | 提醒内容；行尾 `\` 续行规则与 PRINT 相同 |

```ecs
ALERT "任务完成"
```

在「用户配置 → 推送设置」展开列表中的「qq bot」，填写 QQ 开放平台机器人的 AppID、AppSecret，验证凭据后绑定私聊或群聊接收方，并发送图文测试。使用该项右侧的开关启用通知，最后点击窗口底部的「保存」。QQ 与 PushPlus、Bark、自定义 Webhook 使用同一份推送列表、统一保存和发送入口；「添加配置」也可添加 QQ 推送项。验证、绑定和测试期间的设置只保留在当前编辑项中，点击「取消」不会应用这些修改。绑定码每 60 秒更新，只接受当前六位数字；群聊绑定需 @机器人。机器人须具有对应的私聊／群聊消息权限，开发阶段接收方须在 QQ 开放平台允许的测试范围内。

qq bot 编辑区顶部的「使用教程」可打开三阶段图文教程，包含 12 步注册截图、接入说明和 `ALERT` 示例；截图支持放大，返回配置保留当前输入。旁边的 [QQ 开放平台](https://q.qq.com/#/apps) 超链接会在默认浏览器中打开机器人管理页面。也可阅读 [qq bot 使用教程](QQ_BOT_SETUP.md)。

QQ 默认发送提醒文字和触发 `ALERT` 时的最新视频帧，图片会压缩为 JPEG；没有视频帧时使用 EasyCon Logo。发送在后台排队，失败会写入日志，不中断脚本。最多等待 20 条通知，满队列时当前通知会被拒绝并记录原因。关闭自动通知或更改凭据会取消旧队列，已提交的消息仍可能送达。

AppSecret 默认只在当前应用会话内保留；Windows 可勾选「记住 AppSecret」使用当前 Windows 用户的 DPAPI 加密保存。重启后未保存密钥时，需重新填写并启用通知。图文测试始终包含图片。

推送配置统一保存在 `%APPDATA%\easycon\alert.json`，QQ 接入参数属于该文件中的一个推送项。旧版独立 QQ 配置会在首次使用新版本时迁入列表；原有推送配置与已加密保存的凭据会保留。

如果列表提示还有配置未显示，点击「显示全部」即可管理迁入的 QQ 项。首次迁移无法写入文件时，软件会继续使用内存配置并提示重新保存，原文件会保留。

CLI `run` 使用相同用户配置，也支持 `ALERT` 图文通知，并在脚本结束后等待队列发送完成（最多 60 秒）。单片机独立运行时没有桌面网络与视频源，因此无法发送 QQ 通知。

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
- CLI 传参：`ezcon 脚本.ecs -- 参数1 参数2`；GUI 运行时也可填参数。

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
切换 Amiibo 槽位（v2.3 槽位选择；ESP32 本地执行 / 其他宿主静默）。
槽位合法范围 0~19，越界静默忽略。

| 参数 | 类型 | 说明 |
|------|------|------|
| index | INT | 槽位号（0~19） |

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

### NET_LOAD(path): PTR
加载 ONNX 模型，返回会话句柄（PTR）；失败返回 -1。

| 参数 | 类型 | 说明 |
|------|------|------|
| path | STRING | 模型文件路径 |

```ecs
$net = NET_LOAD("models/demo.onnx")
```

### NET_SCALE(tensor, scale, offset): PTR
对张量做逐元素仿射变换 `v * scale + offset`（如 PP-OCR 归一化），返回新张量句柄（PTR）。

| 参数 | 类型 | 说明 |
|------|------|------|
| tensor | PTR | NET_IMAGE / PPOCR_TENSOR 返回的张量句柄 |
| scale | DOUBLE | 乘法系数 |
| offset | DOUBLE | 加法偏移 |

```ecs
$t = NET_SCALE($img, 1.0 / 255, -0.25)
```

### NET_RUNH(net, tensor): INT
把张量句柄送入模型推理，返回**输出元素个数**（失败返回 0）。

| 参数 | 类型 | 说明 |
|------|------|------|
| net | PTR | NET_LOAD 返回的会话句柄 |
| tensor | PTR | 张量句柄 |

```ecs
$n = NET_RUNH($net, $t)
```

### NET_OUT(index): DOUBLE
读取最近一次推理输出的第 `index` 个元素（越界返回 0）。

| 参数 | 类型 | 说明 |
|------|------|------|
| index | INT | 输出元素下标（从 0 开始） |

设计为"标量协议"：先跑 NET_RUNH 拿个数，再逐个 NET_OUT 读取。

```ecs
IF $net >= 0
    $n = NET_RUNH($net, $t)
    $i = 0
    WHILE $i < $n
        $v = NET_OUT($i)
        PRINT $v
        $i = $i + 1
    END
ENDIF
```

### NET_IMAGE(x, y, w, h, rw, rh [, mode]): INT
抓当前画面（负坐标 = 全帧，正坐标 = ROI），缩放到 rw×rh 并转成张量，返回**张量句柄**（失败 0）。
大数组驻留宿主侧，脚本只见句柄。

| 参数 | 类型 | 说明 |
|------|------|------|
| x, y, w, h | INT | 采集区域；负值 = 全帧 |
| rw, rh | INT | 目标尺寸（多数 CNN 要求 32 的倍数） |
| mode | STRING | `"gray"`（[1,1,rh,rw]，缺省）、`"rgb"`（[1,3,rh,rw]，CHW）或 `"letter"`（YOLO letterbox：等比缩放至目标内、居中、四周垫 114 灰，RGB 通道）；`"rgb"`/`"gray"` 可加 `'c'` 后缀（如 `"rgbc"`）改为 cover 缩放 + 居中裁剪——不变形不垫边，分类模型常规 |

```ecs
$img = NET_IMAGE(-1, -1, -1, -1, 640, 640, "rgb")
```

也有 base64 版重载：`NET_IMAGE($imgB64, rw, rh [, mode])`（图像来自 CAPTURE/ROI 结果）。

### NET_SCALE(tensor, scale, offset): INT
逐元素变换 t2 = t·scale + offset，返回新张量句柄（原张量不变）。归一化在此组合。

```ecs
$t = NET_SCALE($img, 0.003921568627, 0.0)    # 0-255 → 0-1
```

### NET_RUNH(net, tensor): INT
以句柄张量为输入推理，返回输出元素个数（失败 0）。输出仍用 NET_OUT(i) 逐元素读取。

```ecs
$n = NET_RUNH($net, $t)
```

### NET_FREE(handle): INT
释放张量句柄（长脚本建议显式释放；会话结束宿主统一回收）。

### NET_UNLOAD(net): INT
卸载模型会话。

### NET_ARGMAX(row): INT
读取最近一次推理输出第 `row` 行中**概率最大的列下标**（分类/序列模型的逐行解码原语；
越界或无输出历史返回 -1）。**末维 = 类别维**：`[1,T,C]` 按 T 行 C 列逐行取；
向量输出 `[1,N]`（或 `[N]`）按 1 行 N 列——分类 logits 直接 `NET_ARGMAX(0)` 得类别。
PP-OCR rec 的 `[1,T,C]` 输出即按此逐帧取字符下标，或直接用 std `CTC_FOLD`（数字子集，
宿主加速）/ std `PP_OCR_TEXT`（整字典，标签表层）一次折叠。

### NET_ROWS(): INT
最近一次推理输出的行数（末维 = 类别维：`[1,T,C]` → T、`[1,N]` → 1；无输出历史返回 0）。

### NET_NORM(tensor, mean, std): PTR
逐通道归一化 `(x - mean[c]) / std[c]`（ResNet 类模型的硬需求），返回新张量句柄（失败 -1）。
通道号由宿主按张量形状推断（rank≥4 取 shape[1]，rank=3 取 shape[0]，rank<3 退化为全局仿射）；
mean/std 长度须为 1 或等于通道数。`/255` 量程折叠留给 NET_SCALE 链式承担——每步都是通用原语。

```ecs
$t = NET_SCALE(NET_IMAGE($img, 224, 224, "rgbc"), 1.0 / 255, 0)
$t = NET_NORM($t, [0.485, 0.456, 0.406], [0.229, 0.224, 0.225])
```

### NET_LABELS(path): INT
加载分类标签/字典文件，返回标签数；失败 -1（含文件不存在/空文件）。重复加载整体替换。
**字典级语义：行号即索引**——逐行原样占位（空行/空白字符行也是条目，如 PP-OCR 字典
首行的全角空格），仅去行尾 CR 与文件末尾换行。纯字符串处理，不依赖推理能力。

### NET_LABEL(index): STRING
第 `index` 个标签；未加载 / 越界返回 ""。CTC 字典配合：class id c → `NET_LABEL(c - 1)`（blank 占位）。

### PP-OCR det 文本找框（std 配方 PP_DET_FIND + DET_* 访问器）
det 全图推理**一次**找齐全部文本框（变动布局/未知文本位置的正解；固定 ROI 场景不必用），
再对每框跑 rec 配方：

```ecs
$det = NET_LOAD("det.onnx")                        # 部署约定名（代码不绑定模型代际）
$c = PP_DET_FIND($det, $frame, 0, 0, 1920, 1080)   # 返回框数（帧形态重载同 rec 配方）
$i = 0
WHILE $i < DET_COUNT()
    $n = PP_OCR_INFER($rec, $frame, DET_X($i), DET_Y($i), DET_W($i), DET_H($i), _REC_H)
    $t = CTC_FOLD(0, "0123456789", _DIGITS_BASE)   # 窗口起点随模型代际指定（见下）
    PRINT $t
    $i = $i + 1
END
```

- 预处理：限边缩放（长边 960、32 对齐，小图不放大）→ det 归一化（`PPOCR_NORM "det"`）；
  几何（scale/rw/rh）由 std 配方计算并传给洞——宿主不做任何形状假设。
- `__DET_BOXES__(thresh, scale, roiX, roiY, mapW, mapH)`：阈值二值化 + 水平膨胀（1×15
  愈合同行间隙）+ 连通域 + unclip（PaddleOCR 同式，v1 ratio=1.0；ratio=2.0 会把大框
  扩成巨型 rec 输入——上调前先加框尺寸护栏）。框按阅读序（y 优先 x）缓存。
- 访问器越界返回 -1；`DET_COUNT` 未找过为 0。完整用法见参考示例
  `examples/ppocr-det-find.ecs`（全页 det → 逐框 rec，收集页面数字与文本行）。

std 侧已备好的 PP-OCR rec 配方（模型/字典为外部资产，命名约定见 `test/assets/README.md`；
代码不绑定模型代际）：`PP_OCR_INFER(net, x, y, w, h, recH): INT`——通用推理步骤（采集 → rec
宽度换算 → 归一化 → 推理，输出驻留宿主，解码自由组合）；`PP_OCR_DIGITS(net, base, x, y, w, h, recH):
STRING`（数字子集窗口，宿主 CTC_FOLD 加速；`base` = '0' 的 class 下标，由脚本按模型代际
指定——PP-OCRv6 字典数字在表头（33 起）、v5 在表尾（16163 起），std 不钉任何代际常数）；
`PP_OCR_TEXT(net, x, y, w, h, dictPath, recH): STRING`（整字典文本，标签表层逐帧解码，
如宝可梦状态页名称识别；加载字典会整体替换 NET_LABELS 全局表）。
`recH` = rec 模型的输入行高，同为代际常数由调用方显式传（PP-OCRv6 资产 48、v5 旧资产 32；
喂错行高 onnxruntime 直接 INVALID_ARGUMENT，OpenCV DNN 不校验形状会静默产出劣化结果）。
三者均有**共享采集重载**（第二参传 `STRING` 整帧 base64，如 `FRAME()` 结果）：
同帧多区域只采集一次，逐区域 `__ROI__` 裁剪在宿主内完成（ROI 走桌面参考回落
`DesktopVisionService`，与采集路径共享裁剪核，零装配可用）——宝可梦状态页示例
7 区域仅采集 1 帧。

数字窗口下标自查（模型代际无关的通用写法，`NET_LABELS` 返回行数）：

```ecs
$count = NET_LABELS($dict)
$i = 0
WHILE $i < $count AND NET_LABEL($i) != "0"
    $i = $i + 1
END
# $i < $count 时：$i = '0' 的行号，class 下标 = $i + 1（blank 占 0）
```

### NET_IMAGE_FILE(path, rw, rh [, mode]): PTR
本地图像文件 → 同 `NET_IMAGE` 管线（mode 词表相同，含 `"letter"` 与 `'c'` 裁剪后缀），
返回张量句柄（失败 0）。与采集版互补：采集走 `NET_IMAGE`，flow/离线图片走本函数。

完整分类链路（ResNet 类模型）：

```ecs
$net = NET_LOAD("resnet18.onnx")
$t = NET_SCALE(NET_IMAGE_FILE("test.png", 224, 224, "rgbc"), 1.0 / 255, 0)
$t = NET_NORM($t, [0.485, 0.456, 0.406], [0.229, 0.224, 0.225])
NET_RUNH($net, $t)
NET_LABELS("imagenet_classes.txt")
PRINT NET_LABEL(NET_ARGMAX(0))       # → 类名
```

### YOLO26 检测（std 配方 YOLO_DETECT + YOLO_* 访问器）
面向 **YOLO26 端到端导出**（Ultralytics 默认导出即端到端，免 NMS）：
输出 `[1,300,6]` = `[x1, y1, x2, y2, conf, class_id]`，坐标在 640×640 letterbox 空间。
argmax 已由模型内部完成（列语义异质），NET_ARGMAX 不适用——解码走 std 配方 + 宿主加速洞：

```ecs
$net = NET_LOAD("models/yolo26n.onnx")
$c = YOLO_DETECT($net, 100, 80, 400, 300, 0.5)   # 返回命中数（输出缺失/非端到端形状 → -1）
$i = 0
WHILE $i < YOLO_COUNT()
    $cls = YOLO_CLS($i)
    $x = YOLO_X1($i)
    PRINT $cls
    $i = $i + 1
END
```

- `YOLO_DETECT(net, x, y, w, h, thresh): INT`——采集 ROI → letterbox → 归一化 → 推理 → 解码，
  返回命中数。逆映射 + 内容区裁剪已完成，访问器直接给屏幕坐标。
  端到端 stride 校验（输出元素数 / 行数 ≠ 6 → -1，防 `end2end=False` 导出错位解码）在配方层。
- `YOLO_COUNT()`——最近一次解码的命中数（未解码为 0）。
- `YOLO_CLS(i): INT` / `YOLO_SCORE(i): DOUBLE` / `YOLO_X1(i)` `YOLO_Y1(i)` `YOLO_X2(i)` `YOLO_Y2(i): INT`——
  按**命中列表**下标读取（非原始 300 行）；坐标裁剪回 ROI 内容区（Ultralytics predict 同约定）；
  访问器越界返回 -1（分数 -1.0）。
- 想自实现解码策略：`NET_ROWS` + `NET_OUT(i*6+j)` 扁平寻址原始输出。

完整示例见 `examples/pokemon-stats.ecs`（PP-OCRv6 rec 识别宝可梦状态页并计算能力值）。

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
| AMIIBO(i) | 切换 Amiibo 槽位（0~19，越界静默） | - | ✔ | ✔ |
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
| NET_LOAD(path) | 加载 ONNX 模型 | PTR | ✔ | 镜像拒跑 |
| NET_RUNH(net, tensor) | 推理，返回输出个数 | INT | ✔ | 镜像拒跑 |
| NET_OUT(i) | 读第 i 个输出 | DOUBLE | ✔ | 镜像拒跑 |
| NET_ARGMAX(row) | 第 row 行最大概率列下标（末维=类别维） | INT | ✔ | 镜像拒跑 |
| NET_ROWS() | 最近输出行数 | INT | ✔ | 镜像拒跑 |
| NET_NORM(t, mean, std) | 逐通道归一化，返回新张量 | PTR | ✔ | 镜像拒跑 |
| NET_LABELS(path)/NET_LABEL(i) | 标签表加载/按索引取类名 | INT/STRING | ✔ | — |
| PP_DET_FIND(net,x,y,w,h)/DET_COUNT/DET_X/Y/W/H(i) | det 找框（阅读序），返回框数 | INT | ✔ | 镜像拒跑 |
| YOLO_DETECT(net,x,y,w,h,thresh) | YOLO26 端到端检测，返回命中数 | INT | ✔ | 镜像拒跑 |
| YOLO_COUNT()/CLS/SCORE/X1/Y1/X2/Y2(i) | 读最近解码命中（屏幕坐标） | INT/DOUBLE | ✔ | 镜像拒跑 |

> - 桌面 = GUI / CLI 运行；单片机 = 编译 .ecx 烧录运行。
> - "镜像拒跑"：桌面专属能力在镜像中带有标记，单片机加载期即识别并拒绝运行——确保桌面/单片机脚本各得其所。
> - "静默"：调用不产生任何效果、不报错。
> - "—"：该平台未提供此函数。