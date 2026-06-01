# 伊机控 ECS 脚本语言说明

伊机控ECS脚本语言是基于现代编译器技术设计的自动化脚本语言，兼容v1脚本格式并进行了全面升级。

## 语言特点

- **行式语法**: 脚本按行分隔，每行作为完整语句
- **块式结构**: if/for/func等支持多行代码块
- **现代设计**: 完整的词法分析、语法分析、语义绑定、AST生成
- **类型安全**: 支持整数、布尔、字符串、数组、结构体等数据类型
- **扩展性强**: 支持FFI模块扩展和库导入

## 编码规范

- **关键字**: 全大写（IF、FOR、FUNC等）或全小写，不支持混合大小写
- **逻辑运算符**: 全小写（and、or、not）
- **内置函数**: 全大写（PRINT、ALERT、TIME等）
- **变量命名**: 以 `$` 开头，区分大小写，支持中文字符

---

## 第一部分：基础入门

### 1.1 注释

使用 `#` 符号添加单行注释，注释从 `#` 开始到行尾。

```ecs
# 这是单行注释
$a=1  # 也可以在代码后面添加注释
```

### 1.2 输出函数

#### PRINT - 控制台输出

由于历史原因，PRINT 不支持表达式，只能打印变量或字符串，使用 `&` 拼接。

```ecs
# 输出文本（引号可选）
PRINT Hello World
PRINT "Hello World"

# 输出变量
$count = 10
PRINT $count

# 使用 & 连接文本和变量
PRINT 总数: & $count & 个对象
# 输出: 总数: 10个对象

# 不支持表达式，需先计算再打印
$sum = 1 + 2
PRINT $sum
```

#### ALERT - 外部推送

```ecs
# 发送推送通知（引号可选，变量用 & 连接）
ALERT 操作完成！
ALERT 已刷 & $count & 次闪光
ALERT 你好 & $1
```

### 1.3 等待延时

```ecs
# 使用 WAIT 关键字
WAIT 100         # 延时100ms
WAIT $duration   # 使用变量

# 直接写数字也等效于 WAIT
500              # 等效于 WAIT 500

# 无参数调用默认50ms
WAIT             # 延时50ms
```

---

## 第二部分：按键与摇杆

### 2.1 基本按键

```ecs
# 按键操作（默认延时50ms）
A              # 按A键
B              # 按B键
X              # 按X键
Y              # 按Y键
L              # 按L键
R              # 按R键
ZL             # 按ZL键
ZR             # 按ZR键
HOME           # 按HOME键
CAPTURE        # 按截屏键
PLUS           # 按+键
MINUS          # 按-键
LCLICK         # 按下左摇杆
RCLICK         # 按下右摇杆

# 指定延时（毫秒）
A 100          # 按A键100ms
B 200          # 按B键200ms

# 使用变量控制延时
$delay = 150
A $delay       # 按A键$delay毫秒
```

### 2.2 按键状态控制

```ecs
# 按住按键（需要手动松开）
HOME DOWN      # 按住HOME键
# 保持按住状态...
HOME UP        # 松开HOME键
```

### 2.3 摇杆方向

```ecs
# 左右摇杆 - 方向控制
LS UP          # 左摇杆向上推
LS DOWN        # 左摇杆向下推
LS LEFT        # 左摇杆向左推
LS RIGHT       # 左摇杆向右推

RS UP          # 右摇杆向上推
RS DOWN        # 右摇杆向下推
RS LEFT        # 右摇杆向左推
RS RIGHT       # 右摇杆向右推

# 对角方向
LS UPLEFT      # 左摇杆左上
LS UPRIGHT     # 左摇杆右上
LS DOWNLEFT    # 左摇杆左下
LS DOWNRIGHT   # 左摇杆右下

# 重置摇杆（回中）
LS RESET       # 左摇杆回中
RS RESET       # 右摇杆回中
```

### 2.4 摇杆角度

```ecs
# 角度控制（0-360度）
LS 0           # 左摇杆向右（0度）
LS 45          # 左摇杆右上方（45度）
LS 90          # 左摇杆向上（90度）
LS 180         # 左摇杆向左（180度）
LS 270         # 左摇杆向下（270度）

RS 45          # 右摇杆45度角
RS 90          # 右摇杆90度角
```

### 2.5 摇杆时序

```ecs
# 摇杆推到指定方向，延时后自动回中
LS UP,100      # 左摇杆向上推100ms后回中
RS 45,200      # 右摇杆45度角200ms后回中
LS DOWN,$dur   # 使用变量控制延时

# 手动控制摇杆状态
LS UP          # 推左摇杆向上
WAIT 1000      # 保持1秒
LS RESET       # 手动回中
```

---

## 第三部分：流程控制

### 3.1 条件语句 (IF)

#### 基本语法

```ecs
# 简单条件
IF $a > 0
    PRINT "正数"
ENDIF
```

#### IF-ELSE 结构

```ecs
IF $a > 0
    PRINT "正数"
ELSE
    PRINT "负数或零"
ENDIF
```

#### IF-ELIF-ELSE 结构

```ecs
IF $score >= 90
    PRINT "优秀"
ELIF $score >= 80
    PRINT "良好"
ELIF $score >= 60
    PRINT "及格"
ELSE
    PRINT "不及格"
ENDIF
```

#### 嵌套条件

```ecs
IF $a > 0
    IF $a < 100
        PRINT "a在0-100之间"
    ELSE
        PRINT "a大于等于100"
    ENDIF
ELSE
    PRINT "a小于等于0"
ENDIF
```

### 3.2 FOR 循环

#### 计次循环

```ecs
# 重复执行指定次数
FOR 10
    A
    WAIT 100
NEXT
```

#### 变量计次循环

```ecs
$count = 5
FOR $count
    PRINT "循环"
    WAIT 100
NEXT
```

#### 范围循环

```ecs
# 指定变量和范围
FOR $i = 1 TO 10
    PRINT $i
NEXT

# 从0开始
FOR $i = 0 TO 100
    PRINT $i
NEXT
```

#### 无限循环

```ecs
FOR
    # 循环体
    IF $condition
        BREAK      # 跳出循环
    ENDIF
    WAIT 100
NEXT
```

### 3.3 WHILE 循环

先判断条件，条件为真时执行循环体。

```ecs
$count = 0
WHILE $count < 10
    PRINT $count
    $count += 1
END
```

```ecs
# 等待条件满足
$match = 0
WHILE $match < 90
    $match = @准备就绪
    WAIT 500
END
```

### 3.4 UNTIL 循环

先执行循环体，再判断条件，条件为真时继续循环。

```ecs
$count = 0
UNTIL $count >= 10
    PRINT $count
    $count += 1
END
```

```ecs
# 至少执行一次
$found = @目标图像
UNTIL $found > 90
    A
    WAIT 100
    $found = @目标图像
END
```

### 3.5 循环控制

#### BREAK - 跳出循环

```ecs
# 跳出当前循环
FOR 100
    IF $stop
        BREAK
    ENDIF
NEXT

# 跳出多层循环（指定层数，最大3层）
FOR $i = 1 TO 10
    FOR $j = 1 TO 10
        IF $emergency
            BREAK 2    # 跳出2层循环
        ENDIF
    NEXT
NEXT
```

#### CONTINUE - 跳过本次循环

```ecs
FOR $i = 1 TO 10
    IF $i == 5
        CONTINUE    # 跳过第5次
    ENDIF
    PRINT "当前: " & $i
NEXT
```

### 3.6 循环嵌套示例

```ecs
# 双层循环
FOR $i = 1 TO 3
    FOR $j = 1 TO 3
        PRINT "i=" & $i & ", j=" & $j
    NEXT
NEXT

# 条件跳出嵌套循环
FOR $i = 1 TO 10
    FOR $j = 1 TO 10
        $found = @目标图像
        IF $found > 90
            BREAK 2
        ENDIF
    NEXT
NEXT
```

---

## 第四部分：图像识别

### 4.1 基础语法

`@` 标签语法用于图像识别，需要在脚本的 `imglabel` 目录下存在对应文件名的 IL 标签文件。标签的制作和配置请参考搜图控制台。

```ecs
# 图像识别语法：@标签名称
$result = @目标图像

# 获取匹配度（0-100整数）
$confidence = @闪光特征
PRINT "匹配度: " & $confidence
```

### 4.2 在条件中使用

```ecs
# 根据图像识别结果做判断
IF @目标图像 > 90
    PRINT "找到目标！"
    A
    WAIT 100
ELSE
    PRINT "未找到目标"
ENDIF
```

### 4.3 匹配度含义

- **90-100**: 高度匹配，可以确认
- **80-89**: 较好匹配，通常可用
- **70-79**: 一般匹配，需要验证
- **< 70**: 匹配度较低，可能不准确

### 4.4 等待图像出现

```ecs
# 等待特定图像出现，最多等待30秒
$max_attempts = 30
FOR $i = 1 TO $max_attempts
    $match = @对话框确认
    IF $match > 90
        PRINT "对话框出现"
        BREAK
    ENDIF
    WAIT 1000
NEXT
```

### 4.5 循环检测

```ecs
# 循环检测闪光宝可梦
FOR 1000
    $shiny = @闪光特征
    IF $shiny > 95
        ALERT "发现闪光！"
        PRINT "闪光匹配度: " & $shiny
        BREAK
    ENDIF

    # 执行操作以继续寻找
    A
    WAIT 2000
NEXT
```

### 4.6 多阶段识别

```ecs
# 粗略定位 + 精确识别
IF @大范围特征 > 75
    PRINT "可能找到目标区域"
    WAIT 500     # 等待画面稳定

    # 在区域内精确识别
    IF @精确特征 > 90
        PRINT "确认找到目标"
        A
        WAIT 100
    ELSE
        PRINT "特征不匹配，继续寻找"
    ENDIF
ELSE
    PRINT "未找到目标区域"
ENDIF
```

### 4.7 视觉库函数

当图像识别可用时，以下函数自动可用：

```ecs
# FRAME - 截取屏幕
$img = FRAME()                    # 截取全屏
$img = FRAME($x, $y, $w, $h)     # 截取指定区域

# OCR - 文字识别
$text = OCR($x, $y, $w, $h)              # 默认中文识别
$text = OCR($x, $y, $w, $h, $lang)      # 指定语言

# ROI - 区域提取
$cropped = ROI($img, $x, $y, $w, $h)
```

---

## 第五部分：变量与数据类型

### 5.1 变量

以 `$` 开头，可重新赋值。变量名支持中文字符。

```ecs
$a = 1
$test = 233
$真 = 2 > 1       # 布尔值 true
$count = 0
$name = "伊机控"

# 中文变量名
$生命值 = 100
$攻击力 = 50
$名字 = "勇者"
```

### 5.2 数据类型

#### 整数 (INT)

默认32位有符号整数，范围：-2,147,483,648 到 2,147,483,647

```ecs
$number = 42
$negative = -10
$zero = 0
```

#### 布尔值 (BOOL)

布尔值通过比较表达式产生，不支持 `true`/`false` 字面量。

```ecs
$is_ready = 1 > 0     # true
$isValid = 1 > 2      # false
$result = 1 > 2       # false
$flag = 5 == 5        # true
```

#### 字符串 (STRING)

支持双引号和单引号，支持转义字符。

```ecs
$str1 = "内容1"
$str2 = 'abc'
$empty = ""

# 转义字符
$newline = "第一行\n第二行"
$tab = "列1\t列2"
$quote = "他说\"你好\""
$backslash = "路径\\到\\文件"
```

#### 数组

支持同类型数组，使用方括号定义。

```ecs
$numbers = [1, 2, 3, 4, 5]
$empty = []
$names = ["Alice", "Bob", "Charlie"]
```

---

## 第六部分：运算符

### 6.1 算术运算符

```ecs
$result = 10 + 5      # 加法: 15
$result = 10 - 3      # 减法: 7
$result = 6 * 7       # 乘法: 42
$result = 20 / 4      # 除法: 5（浮点结果）
$result = 20 \ 3      # 整除: 6（向下取整）
$result = 10 % 3      # 取余: 1
```

### 6.2 位运算符

```ecs
$result = $a & $b     # 按位与
$result = $a | $b     # 按位或
$result = $a ^ $b     # 按位异或
$result = ~$a         # 按位取反
$result = $a << 2     # 左移
$result = $a >> 2     # 右移
```

### 6.3 比较运算符

```ecs
$a > $b       # 大于
$a >= $b      # 大于等于
$a < $b       # 小于
$a <= $b      # 小于等于
$a == $b      # 等于
$a != $b      # 不等于
```

### 6.4 逻辑运算符

注意：逻辑运算符必须全小写。

```ecs
$result = $a and $b    # 逻辑与
$result = $a or $b     # 逻辑或
$result = not $a       # 逻辑非

# 复杂条件
IF ($score >= 60) and ($attendance >= 80)
    PRINT "合格"
ENDIF
```

### 6.5 字符串运算符

```ecs
# 字符串连接（& 或 +）
$msg = "Hello" & " " & "World"
$msg2 = "Hello" + " " + "World"

# 字符串包含检测（in）
$hasKeyword = "error" in $errorMessage    # 返回 bool
$isSubstr = "abc" in "abcdef"             # true
```

### 6.6 数组运算符

```ecs
# 数组包含检测（in）
$found = 3 in $numbers         # 检查3是否在数组中
```

### 6.7 赋值运算符

```ecs
$a = 10        # 基本赋值
$a += 5        # 加法赋值: $a = $a + 5
$a -= 3        # 减法赋值: $a = $a - 3
$a *= 2        # 乘法赋值: $a = $a * 2
$a /= 4        # 除法赋值: $a = $a / 4
$a \= 3        # 整除赋值: $a = $a \ 3
$a %= 3        # 取余赋值: $a = $a % 3
$a &= 0xFF     # 按位与赋值
$a |= 0x01     # 按位或赋值
$a ^= 0x02     # 按位异或赋值
$a <<= 2       # 左移赋值
$a >>= 1       # 右移赋值
```

### 6.8 运算符优先级

从高到低：

| 优先级 | 运算符 | 说明 |
|--------|--------|------|
| 6 | `-` `~` `not` | 一元运算符（取反、位取反、逻辑非） |
| 5 | `*` `/` `\` `%` `&` `<<` `>>` | 乘除、位运算 |
| 4 | `+` `-` `\|` `^` | 加减、位运算 |
| 3 | `==` `!=` `<` `<=` `>` `>=` `in` | 比较运算 |
| 2 | `and` | 逻辑与 |
| 1 | `or` | 逻辑或 |

---

## 第七部分：函数

### 7.1 函数定义

#### 无参数函数

```ecs
FUNC say_hello
    PRINT "Hello!"
ENDFUNC
```

#### 有参数函数

```ecs
FUNC greet($name)
    PRINT "Hello, " & $name
ENDFUNC
```

参数类型标注是可选的，类型为 `INT` 时可省略。

#### 带类型标注的函数

```ecs
# INT 类型省略标注
FUNC add($a, $b):INT
    RETURN $a + $b
ENDFUNC

# 非 INT 类型需要标注
FUNC greet($name:STRING)
    PRINT "Hello, " & $name
ENDFUNC

FUNC is_positive($number):BOOL
    RETURN $number > 0
ENDFUNC

# 调用
$flag = is_positive(5)   # true (1 > 0)
$flag2 = is_positive(-1) # false (1 > 2)
```

#### 空参数列表

```ecs
FUNC sayHello()
    PRINT "Hello!"
ENDFUNC

FUNC getRandom():INT
    RETURN RAND(100)
ENDFUNC
```

### 7.2 函数调用

```ecs
# 调用无返回值函数 - CALL 语法
CALL say_hello

# 调用有参数函数 - 语句式调用
greet "World"
greet $name

# 调用有返回值函数 - 表达式式调用
$result = add(10, 20)
PRINT "结果: " & $result

# 返回布尔值
FUNC isPositive($n):BOOL
    RETURN $n > 0
ENDFUNC

$flag = isPositive(5)   # true (1 > 0)
```

### 7.3 函数特性

```ecs
# 递归函数
FUNC factorial($n):INT
    IF $n <= 1
        RETURN 1
    ENDIF
    RETURN $n * factorial($n - 1)
ENDFUNC

# 函数重载（按参数数量区分）
FUNC log($msg)
    PRINT $msg
ENDFUNC

FUNC log($msg, $level)
    PRINT "[" & $level & "] " & $msg
ENDFUNC
```

### 7.4 返回值

```ecs
# 有返回值函数必须在所有路径都有 RETURN
FUNC max($a, $b):INT
    IF $a > $b
        RETURN $a
    ELSE
        RETURN $b
    ENDIF
ENDFUNC

# void 函数可以省略 RETURN
FUNC printInfo($name)
    PRINT "Name: " & $name
    # 自动结束
ENDFUNC
```

---

## 第八部分：结构体

### 8.1 结构体定义

使用 `STRUCT` 关键字定义结构体，`END` 结束。

```ecs
STRUCT Point
    $x:INT
    $y:INT
END

STRUCT Player
    $name:STRING
    $hp:INT
    $mp:INT
    $level:INT
END
```

### 8.2 结构体字段类型

```ecs
STRUCT Config
    $width:INT
    $height:INT
    $title:STRING
    $enabled:BOOL
    $data:BYTE[10]      # 固定长度数组字段
END
```

### 8.3 结构体实例化

```ecs
# 创建结构体实例
$p = Point{}
$player = Player{}

# 访问和赋值字段
$p.x = 100
$p.y = 200
$player.name = "勇者"
$player.hp = 100
```

### 8.4 结构体字段访问

```ecs
# 读取字段
PRINT $p.x
PRINT $player.name

# 链式访问
STRUCT Rect
    $origin:Point
    $size:Point
END

$rect = Rect{}
$rect.origin.x = 10
$rect.origin.y = 20

# 在表达式中使用
$sum = $p.x + $p.y
```

### 8.5 内置结构体 Pixel

```ecs
# Pixel 结构体预定义，包含 R, G, B, A 四个 INT 字段
$pixel = Pixel{}
$pixel.R = 255
$pixel.G = 128
$pixel.B = 0
$pixel.A = 255
```

---

## 第九部分：数组与切片

### 9.1 数组操作

```ecs
# 创建数组
$numbers = [1, 2, 3, 4, 5]

# 访问元素（从0开始）
$first = $numbers[0]
$third = $numbers[2]

# 修改元素
$numbers[0] = 10

# 数组长度
$len = LEN($numbers)    # 5

# 添加元素（返回新数组）
$numbers = APPEND($numbers, 6)
```

### 9.2 切片操作

```ecs
$arr = [10, 20, 30, 40, 50]

# 获取切片 [start:end]
$Slice = $arr[1:4]      # [20, 30, 40]

# 省略 start（从头开始）
$first3 = $arr[:3]      # [10, 20, 30]

# 省略 end（到末尾）
$last2 = $arr[3:]       # [40, 50]

# 字符串也支持切片
$str = "Hello World"
$sub = $str[0:5]        # "Hello"
```

### 9.3 数组包含检测

```ecs
$arr = [1, 2, 3, 4, 5]
$has3 = 3 in $arr       # true
$has9 = 9 in $arr       # false
```

---

## 第十部分：内置函数

### 10.1 系统函数

```ecs
WAIT 50               # 延时50ms（默认50ms）
$t = TIME()           # 获取运行时间（毫秒）
$r = RAND(100)        # 随机数0-99
BEEP 1000, 200        # 蜂鸣器：频率1kHz，持续200ms（Windows）
$dir = ENV("PATH")    # 获取环境变量
```

### 10.2 集合操作函数

```ecs
# APPEND - 数组添加元素（返回新数组）
$numbers = [1, 2, 3]
$numbers = APPEND($numbers, 4)   # [1, 2, 3, 4]

# LEN - 获取数组或字符串长度
$count = LEN($numbers)           # 4
$strlen = LEN("Hello")           # 5
```

### 10.3 类型转换函数

```ecs
# STRING - 转换为字符串
$str = STRING(42)                # "42"
$str = STRING(true)              # "true"
$str = STRING(3.14)              # "3.14"

# INT - 转换为整数
$num = INT("123")                # 123
$num = INT(3.14)                 # 3
$num = INT(true)                 # 1
```

### 10.4 字符串编码函数

```ecs
# ENCODE - 将字节数组编码为字符串
$bytes = [72, 101, 108, 108, 111]
$str = ENCODE($bytes)            # "Hello"
$str = ENCODE($bytes, "utf8")    # 指定编码
```

### 10.5 JSON 查询函数

```ecs
# JQ - JSON 数据查询
$json = '{"name": "test", "value": 42}'
$name = JQ($json, ".name")       # "test"
$value = JQ($json, ".value")     # 42
```

### 10.6 Amiibo 函数

```ecs
# AMIIBO - 切换 Amiibo 槽位（ESP32专属）
AMIIBO 0             # 切换到槽位0
AMIIBO 5             # 切换到槽位5
AMIIBO $slot         # 使用变量
```

---

## 第十一部分：模块与导入

### 11.1 导入库脚本

```ecs
# 导入库文件（必须放在脚本开头）
IMPORT "utils.ecs"
IMPORT "math.ecs"

# 库文件放在脚本同目录的 lib/ 文件夹中
```

### 11.2 库脚本规范

库脚本只能包含：
- 函数定义（FUNC/ENDFUNC）
- 结构体定义（STRUCT/END）
- 外部函数声明（EXTERN）
- 全局变量或常量定义

```ecs
# lib/utils.ecs
FUNC max($a, $b):INT
    IF $a > $b
        RETURN $a
    ELSE
        RETURN $b
    ENDIF
ENDFUNC

FUNC min($a, $b):INT
    IF $a < $b
        RETURN $a
    ELSE
        RETURN $b
    ENDIF
ENDFUNC
```

### 11.3 标准库

以下函数自动可用，无需导入：

```ecs
$t = TIME()           # 获取脚本运行时间（毫秒）
```

---

## 第十二部分：FFI 外部函数

### 12.1 声明外部函数

使用 `EXTERN FUNC` 声明 DLL 中的原生函数：

```ecs
# 基本语法
EXTERN FUNC Sleep($ms:INT) FROM "kernel32.dll"
EXTERN FUNC GetForegroundWindow():PTR FROM "user32.dll"
```

### 12.2 支持的类型

| ECS 类型 | 说明 |
|----------|------|
| INT | 32位整数 |
| BOOL | 布尔值 |
| STRING | 字符串（默认 UTF-8 编码） |
| DOUBLE | 双精度浮点 |
| PTR | 原生指针 |

### 12.3 使用示例

```ecs
# 声明
EXTERN FUNC Sleep($ms:INT) FROM "kernel32.dll"
EXTERN FUNC GetForegroundWindow():PTR FROM "user32.dll"
EXTERN FUNC MessageBoxW($hwnd:PTR, $text:STRING, $caption:STRING, $flags:INT):INT FROM "user32.dll"
EXTERN FUNC sqrt($x:DOUBLE):DOUBLE FROM "msvcrt.dll"

# 调用（与普通函数一致）
Sleep 1000
$hwnd = GetForegroundWindow()
MessageBoxW $hwnd, "Hello", "Title", 0
$x = sqrt(2.0)
```

### 12.4 指定导出名称

```ecs
# 使用 AS 指定实际导出函数名
EXTERN FUNC mySleep($ms:INT) AS "Sleep" FROM "kernel32.dll"
mySleep 1000
```

**注意：** 调用原生函数存在崩溃风险，请确保参数类型和数量正确。

---

## 第十三部分：高级特性

### 13.1 表达式中的函数调用

```ecs
# 函数调用可以作为表达式使用
$result = add(1, 2)
PRINT $result

# 嵌套函数调用
$result = add(multiply(2, 3), 1)

# 在条件中使用
IF isReady()
    PRINT "Ready"
ENDIF
```

### 13.2 括号表达式

```ecs
# 使用括号控制运算优先级
$result = (1 + 2) * 3    # 9
$flag = ($a > 0) and ($b < 100)
```

### 13.3 字符串切片

```ecs
$str = "Hello World"
$sub1 = $str[0:5]       # "Hello"
$sub2 = $str[6:]        # "World"
$sub3 = $str[:5]        # "Hello"
```

---

## 第十四部分：实用示例

### 14.1 基础自动化

```ecs
# 基础按键和延时
A
WAIT 100
B
WAIT 100
```

### 14.2 智能脚本

```ecs
# 根据条件执行不同操作
$is_ready = @准备就绪
IF $is_ready > 80
    A
    WAIT 100
ELSE
    B
    WAIT 200
ENDIF
```

### 14.3 循环操作

```ecs
# 重复执行操作
FOR 10
    A
    WAIT 50
NEXT

# 条件循环
$counter = 0
WHILE $counter < 100
    $match = @目标图像
    IF $match > 90
        PRINT "找到目标"
        BREAK
    ENDIF
    $counter += 1
    WAIT 500
END
```

### 14.4 综合应用：自动刷闪光

```ecs
# 定义辅助函数
FUNC check_shiny():BOOL
    $match = @闪光特征
    RETURN $match > 95
ENDFUNC

FUNC safe_press($key, $delay)
    $key
    WAIT $delay
ENDFUNC

# 主循环
$found = 1 > 2
FOR 1000
    # 遇到宝可梦
    A
    WAIT 2000

    # 检查是否闪光
    IF check_shiny()
        ALERT "发现闪光！"
        $found = 1 > 0
        BREAK
    ENDIF

    # 重置
    HOME
    WAIT 1000
NEXT

IF $found
    ALERT "脚本完成：找到闪光！"
ELSE
    ALERT "脚本完成：未找到闪光"
ENDIF
```

### 14.5 使用结构体管理状态

```ecs
STRUCT GameState
    $phase:INT
    $attempt:INT
    $found:BOOL
END

$state = GameState{}
$state.phase = 0
$state.attempt = 0
$state.found = 1 > 2

WHILE not $state.found
    IF $state.phase == 0
        # 阶段0：准备
        PRINT "准备中..."
        $state.phase = 1
    ELIF $state.phase == 1
        # 阶段1：搜索
        $match = @目标
        IF $match > 90
            $state.found = 1 > 0
        ENDIF
        $state.attempt += 1
    END

    IF $state.attempt >= 100
        BREAK
    ENDIF
    WAIT 500
END
```

### 14.6 库文件使用示例

```ecs
# 主脚本
IMPORT "math.ecs"
IMPORT "utils.ecs"

$a = 10
$b = 20
$max = max($a, $b)
PRINT "最大值: " & $max
```

```ecs
# lib/math.ecs
FUNC add($a, $b):INT
    RETURN $a + $b
ENDFUNC

FUNC multiply($a, $b):INT
    RETURN $a * $b
ENDFUNC
```

---

## 附录

### A. 关键字完整列表

| 类别 | 关键字 |
|------|--------|
| 控制流 | IF / ELIF / ELSE / ENDIF |
| 循环 | FOR / TO / NEXT / WHILE / UNTIL / END / BREAK / CONTINUE |
| 函数 | FUNC / ENDFUNC / RETURN |
| 导入 | IMPORT / EXTERN / FROM / AS |
| 结构 | STRUCT |
| 逻辑 | and / or / not |
| 按键 | A / B / X / Y / L / R / ZL / ZR / MINUS / PLUS / HOME / CAPTURE / LCLICK / RCLICK |
| 摇杆 | LS / RS / UP / DOWN / LEFT / RIGHT / UPLEFT / UPRIGHT / DOWNLEFT / DOWNRIGHT / RESET |

### B. 内置函数速查

| 函数 | 说明 |
|------|------|
| WAIT(ms) | 延时指定毫秒 |
| TIME() | 获取运行时间 |
| RAND(max) | 随机数 0~max-1 |
| LEN(var) | 获取数组/字符串长度 |
| APPEND(arr, val) | 数组添加元素 |
| STRING(var) | 转换为字符串 |
| INT(var) | 转换为整数 |
| PRINT(msg) | 控制台输出 |
| ALERT(msg) | 外部推送 |
| BEEP(freq, dur) | 蜂鸣器 |
| ENV(name) | 获取环境变量 |
| ENCODE(arr, type) | 字节数组编码 |
| JQ(json, query) | JSON查询 |
| AMIIBO(index) | Amiibo槽位 |

### C. 类型对照表

| ECS 类型 | 说明 | 大小 |
|----------|------|------|
| INT | 32位有符号整数 | 4字节 |
| UINT | 32位无符号整数 | 4字节 |
| UINT64 | 64位无符号整数 | 8字节 |
| BYTE | 8位无符号整数 | 1字节 |
| DOUBLE | 双精度浮点 | 8字节 |
| BOOL | 布尔值 | 4字节 |
| STRING | 字符串 | 指针大小 |
| PTR | 原生指针 | 指针大小 |
| TYPE[] | 动态数组 | - |
| TYPE[N] | 固定长度数组 | - |

### D. 运算符完整列表

| 类型 | 运算符 |
|------|--------|
| 算术 | + - * / \ % |
| 位运算 | & \| ^ ~ << >> |
| 比较 | == != < > <= >= |
| 逻辑 | and or not |
| 字符串 | & + in |
| 赋值 | = += -= *= /= \= %= &= \|= ^= <<= >>= |
