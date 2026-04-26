# 伊机控 ECS 脚本语言说明

伊机控ECS脚本语言是基于现代编译器技术设计的自动化脚本语言，兼容v1脚本格式并进行了全面升级。

## 语言特点

- **行式语法**: 脚本按行分隔，每行作为完整表达式
- **块式结构**: if/for/func等支持多行代码块
- **现代设计**: 完整的词法分析、语法分析、AST生成
- **类型安全**: 支持整数、布尔、字符串、数组等数据类型
- **扩展性强**: 支持FFI模块扩展

## 编码规范

- **关键字**: 全大写（IF、FOR、FUNC等）
- **逻辑运算符**: 全小写（and、or、not）
- **内置函数**: 全大写（PRINT、ALERT、TIME等）
- **变量命名**: 区分大小写

## 快速开始

### 注释

使用 `#` 符号添加单行注释，注释从 `#` 开始到行尾。

```ecs
# 这是单行注释
$a=1  # 也可以在代码后面添加注释
```

### 输出函数

#### PRINT - 控制台输出

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
```

#### ALERT - 外部推送

```ecs
# 发送推送通知（引号可选，变量用 & 连接）
ALERT 操作完成！
ALERT 已刷 & $count & 次闪光
ALERT 你好 & $1
```

### 按键基础

#### 基本按键

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
```

#### 按键状态控制

```ecs
# 按住按键（需要手动松开）
HOME DOWN      # 按住HOME键
# 保持按住状态...
HOME UP        # 松开HOME键
```

### 摇杆控制

#### 摇杆基础

```ecs
# 左右摇杆
LS UP          # 左摇杆向上推
LS DOWN        # 左摇杆向下推
LS LEFT        # 左摇杆向左推
LS RIGHT       # 左摇杆向右推

RS UP          # 右摇杆向上推
RS DOWN        # 右摇杆向下推
RS LEFT        # 右摇杆向左推
RS RIGHT       # 右摇杆向右推

# 重置摇杆（回中）
LS RESET       # 左摇杆回中
RS RESET       # 右摇杆回中
```

#### 摇杆方向

```ecs
# 十字方向键
UP             # 上
DOWN           # 下
LEFT           # 左
RIGHT          # 右
UPLEFT         # 左上
UPRIGHT        # 右上
DOWNLEFT       # 左下
DOWNRIGHT      # 右下
```

#### 摇杆角度

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

#### 摇杆时序

```ecs
# 摇杆推到指定方向，延时后自动回中
LS UP,100      # 左摇杆向上推100ms后回中
RS 45,200      # 右摇杆45度角200ms后回中

# 手动控制摇杆状态
LS UP          # 推左摇杆向上
WAIT 1000      # 保持1秒
LS RESET       # 手动回中
```

### 条件判断 (IF)

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
ELIF $score >= 60
    PRINT "及格"
ELSE
    PRINT "不及格"
ENDIF
```

### 循环 (FOR)

#### 计次循环

```ecs
# 重复执行指定次数
FOR 10
    A
    WAIT 100
NEXT
```

#### 范围循环

```ecs
# 指定变量和范围
FOR $i = 1 TO 10
    PRINT $i
NEXT

# 指定步长
FOR $i = 0 TO 100 STEP 10
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
NEXT
```

#### 循环控制

```ecs
FOR 100
    IF $skip
        CONTINUE   # 跳过本次循环，继续下一次
    ENDIF

    IF $stop
        BREAK      # 跳出循环
    ENDIF
NEXT
```

### 图像识别

#### 基础图像识别

```ecs
# 图像识别语法：@图像名称
$result = @目标图像

# 获取匹配度（0-100）
$confidence = @闪光特征
PRINT "匹配度: " & $confidence
```

#### 条件判断

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

#### 识别阈值

```ecs
# 不同阈值的应用
IF @图像 > 95
    PRINT "严格匹配：高度确定"
ELIF @图像 > 80
    PRINT "正常匹配：比较确定"
ELIF @图像 > 70
    PRINT "宽松匹配：可能找到"
ELSE
    PRINT "未找到目标"
ENDIF
```

## 基础语法

### 变量声明

#### 常量
以 `_` 开头，必须先定义再使用，只能赋值一次。

```ecs
_测试时间=100
_sum = 1+1
_最大次数 = 50
```

#### 变量  
以 `$` 开头，可重新赋值。

```ecs
$a=1
$test=233
$真=2>1
$count = 0
```

#### 搜图变量
以 `@` 开头，用于图像识别结果。

```ecs
$match = @闪光度
$score = @目标图像
```

### 数据类型

#### 整数
默认32位有符号整数，范围：-2,147,483,648 到 2,147,483,647

```ecs
$number = 42
$hex = 0xFF
```

#### 布尔值
支持 `true` 和 `false` 关键字。

```ecs
$is_ready = true
$isValid = false
$result = 1>2    # false
```

#### 字符串
支持双引号和单引号。

```ecs
$str1 = "内容1"
$str2 = 'abc'
$msg = "你好" & "世界"
```

#### 数组
支持整数数组。

```ecs
$numbers = [1, 2, 3, 4, 5]
$empty = []
```

### 字符串操作

使用 `&` 运算符连接字符串和变量。

```ecs
$name = "伊机控"
$version = "2.0"
$message = "欢迎使用 " & $name & " v" & $version
PRINT $message
# 输出: 欢迎使用 伊机控 v2.0
```

## 表达式和运算符

### 算术运算符

```ecs
$result = 10 + 5      # 加法: 15
$result = 10 - 3      # 减法: 7
$result = 6 * 7       # 乘法: 42
$result = 20 / 4      # 除法: 5
$result = 20 \ 3      # 整除: 6
$result = 10 % 3      # 取余: 1
$result = 2 ^ 8       # 幂运算: 256
```

### 位运算符

```ecs
$result = $a & $b     # 按位与
$result = $a | $b     # 按位或
$result = $a ^ $b     # 按位异或
$result = ~$a         # 按位取反
$result = $a << 2     # 左移
$result = $a >> 2     # 右移
```

### 比较运算符

```ecs
$a > $b       # 大于
$a >= $b      # 大于等于
$a < $b       # 小于
$a <= $b      # 小于等于
$a == $b      # 等于
$a != $b      # 不等于
```

### 逻辑运算符

```ecs
$result = $a and $b    # 逻辑与
$result = $a or $b     # 逻辑或
$result = not $a       # 逻辑非

# 复杂条件
IF ($score >= 60) and ($attendance >= 80)
    PRINT "合格"
ENDIF
```

### 赋值运算符

```ecs
$a = 10        # 基本赋值
$a += 5        # 加法赋值: $a = $a + 5
$a -= 3        # 减法赋值: $a = $a - 3
$a *= 2        # 乘法赋值: $a = $a * 2
$a /= 4        # 除法赋值: $a = $a / 4
$a %= 3        # 取余赋值: $a = $a % 3
```

## 流程控制详解

### 条件语句

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

#### 复杂条件

```ecs
# 多条件组合
IF ($score >= 90) and ($attendance >= 80)
    PRINT "优秀且出勤良好"
ELIF ($score >= 60) or ($extra_credit > 0)
    PRINT "及格或有加分"
ELSE
    PRINT "需要努力"
ENDIF
```

### 循环语句

#### 变量循环

```ecs
# 使用变量控制循环
$count = 5
FOR $count
    PRINT "循环次数: " & $count
    A
    WAIT 100
NEXT
```

#### 循环嵌套

```ecs
# 双层循环
FOR $i = 1 TO 3
    FOR $j = 1 TO 3
        PRINT "i=" & $i & ", j=" & $j
    NEXT
NEXT

# 跳出多层循环
FOR $i = 1 TO 10
    FOR $j = 1 TO 10
        IF $emergency
            BREAK 2    # 跳出2层循环
        ENDIF
    NEXT
NEXT
```

#### 循环控制示例

```ecs
# 跳过特定次数
FOR $i = 1 TO 10
    IF $i == 5
        CONTINUE    # 跳过第5次
    ENDIF
    PRINT "当前: " & $i
NEXT

# 条件跳出
FOR 100
    $found = @目标图像
    IF $found > 90
        PRINT "找到目标，停止循环"
        BREAK
    ENDIF
    WAIT 500
NEXT
```

## 函数定义和调用

### 函数定义

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

#### 带返回值函数

```ecs
FUNC add($a, $b):INT
    RETURN $a + $b
ENDFUNC

FUNC is_positive($number):BOOL
    IF $number > 0
        RETURN true
    ELSE
        RETURN false
    ENDIF
ENDFUNC
```

### 函数调用

```ecs
# 调用无返回值函数
CALL say_hello

# 调用有参数函数
greet "World"

# 调用有返回值函数
$result = add(10, 20)
PRINT "结果: " & $result
```

### 实用函数示例

```ecs
# 安全等待函数
FUNC safe_wait($ms:INT)
    IF $ms > 0
        WAIT $ms
    ENDIF
ENDFUNC

# 最大值函数
FUNC max($a:INT, $b:INT):INT
    IF $a > $b
        RETURN $a
    ELSE
        RETURN $b
    ENDIF
ENDFUNC
```

## 图像识别详解

### 图像识别基础

#### 识别语法

```ecs
# 基本识别
$match = @图像名称

# 直接在条件中使用
IF @目标 > 90
    PRINT "找到目标"
ENDIF
```

#### 匹配度含义

- **100**: 完全匹配
- **90-99**: 高度匹配
- **80-89**: 较好匹配
- **70-79**: 一般匹配
- **< 70**: 匹配度较低

### 实用图像识别示例

#### 等待图像出现

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

IF $i > $max_attempts
    PRINT "超时：对话框未出现"
ENDIF
```

#### 循环检测

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

#### 多阶段识别

```ecs
# 粗略定位 + 精确识别
IF @大范围特征 > 75
    PRINT "可能找到目标区域"
    
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

#### 智能阈值

```ecs
# 根据环境调整阈值
FUNC smart_search($image, $strict, $loose)
    # 先用严格阈值
    IF @$image > $strict
        RETURN "高度确定"
    ENDIF
    
    # 再用宽松阈值
    IF @$image > $loose
        RETURN "可能找到"
    ENDIF
    
    RETURN "未找到"
ENDFUNC

$result = smart_search(@目标, 95, 80)
PRINT $result
```

### 图像识别最佳实践

#### 提高识别准确度

```ecs
# 1. 多次确认
FOR 3
    $match1 = @目标图像
    WAIT 100
    $match2 = @目标图像
    WAIT 100
    
    IF ($match1 > 90) and ($match2 > 90)
        PRINT "确认找到目标"
        BREAK
    ENDIF
NEXT

# 2. 稳定等待
WAIT 2000    # 等待画面稳定
$match = @目标图像
IF $match > 90
    PRINT "稳定识别成功"
ENDIF

# 3. 分阶段验证
IF @粗略特征 > 80
    WAIT 500     # 等待画面稳定
    IF @精确特征 > 90
        PRINT "两阶段验证成功"
    ENDIF
ENDIF
```

## 关键字和内置函数

### 关键字列表

```
# 控制流
IF/ELIF/ELSE/ENDIF
FOR/TO/STEP/NEXT/BREAK/CONTINUE
FUNC/ENDFUNC/RETURN
IMPORT/AS

# 逻辑运算
AND/OR/NOT

# 手柄按键
A/B/X/Y/L/R/ZL/ZR
MINUS/PLUS/HOME/CAPTURE
LCLICK/RCLICK
UP/DOWN/LEFT/RIGHT
RESET

# 数据类型
INT/BOOL/STRING
TRUE/FALSE
```

### 内置函数

#### 系统函数

```ecs
WAIT 50               # 延时50ms（默认50ms）
$t = TIME()           # 获取运行时间（毫秒）
$r = RAND(100)        # 随机数0-100
BEEP 1000, 200        # 蜂鸣器：频率1kHz，持续200ms
```

#### 集合操作

```ecs
APPEND($array, $value) # 数组添加元素
LEN($array)            # 获取数组长度

$numbers = [1, 2, 3]
$count = LEN($numbers)  # 结果: 3
$new = APPEND($numbers, 4)  # 结果: [1, 2, 3, 4]
```

#### 扩展功能

```ecs
# Amiibo模拟（ESP32专属）
AMIIBO $index         # 切换Amiibo槽位

# 模块导入
IMPORT "module.ecs"   # 导入模块（放在lib文件夹中）
```

#### 外部函数调用（FFI）

```ecs
# 声明原生 DLL 函数
EXTERN FUNC Sleep($ms:INT):VOID FROM "kernel32.dll"
EXTERN FUNC GetForegroundWindow():PTR FROM "user32.dll"
EXTERN FUNC MessageBoxW($hwnd:PTR, $text:STRING, $caption:STRING, $flags:INT):INT FROM "user32.dll"
EXTERN FUNC sqrt($x:DOUBLE):DOUBLE FROM "msvcrt.dll"

# 调用方式与普通函数一致
$hwnd = GetForegroundWindow()
MessageBoxW $hwnd, "Hello", "Title", 0
$x = sqrt(2.0)
```

**支持类型：** INT、BOOL、STRING、VOID、PTR（指针）、DOUBLE（双精度浮点）

**注意：** 调用原生函数存在崩溃风险，请确保参数正确。

## 实用示例

### 基础自动化

```ecs
# 基础按键和延时
A
WAIT 100
B
L+R
```

### 智能脚本

```ecs
# 根据条件执行不同操作
$is_ready = true
IF $is_ready
    A
    WAIT 100
ELSE
    B
ENDIF
```

### 循环操作

```ecs
# 重复执行操作
FOR 10
    A
    WAIT 50
NEXT
```

### 图像识别应用

```ecs
# 等待并检测图像
FOR 30
    $result = @目标图像
    IF $result > 90
        PRINT "找到目标"
        A
        BREAK
    ENDIF
    WAIT 1000
NEXT
```

### 综合应用

```ecs
# 自动刷闪光脚本
FUNC check_shiny
    $match = @闪光特征
    IF $match > 95
        RETURN true
    ELSE
        RETURN false
    ENDIF
ENDFUNC

FOR 100
    # 遇到宝可梦
    A
    WAIT 2000
    
    # 检查是否闪光
    IF check_shiny()
        ALERT "发现闪光！"
        BREAK
    ENDIF
    
    # 重置
    HOME
    WAIT 1000
NEXT
```

## 模块化扩展

伊机控支持通过FFI接口扩展脚本功能，开发者可以：

1. **创建模块**: 开发自定义功能模块
2. **导入模块**: 使用 `IMPORT` 语句加载模块  
3. **调用函数**: 模块函数与内置函数使用方式一致

详细信息请参考 [模块系统文档](models.md)。