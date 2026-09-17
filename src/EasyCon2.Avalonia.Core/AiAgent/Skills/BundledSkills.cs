using EasyCon.Core.LLM.Skills;

namespace EasyCon2.Avalonia.Core.AiAgent.Skills;

/// <summary>
/// 内置技能：直接在代码中初始化 Skill 对象，不从文件系统加载。
/// 优先级最低，用户/项目级同名技能可覆盖。
/// </summary>
public static class BundledSkills
{
    public static List<Skill> CreateAll()
    {
        return
        [
            CreateEcscriptAuthoring(),
            CreateDeviceControl(),
            CreateVisionAnalysis(),
        ];
    }

    private static Skill CreateEcscriptAuthoring()
    {
        var manifest = new SkillManifest
        {
            Name = "ecscript-authoring",
            Description = "ECScript 脚本的编写、理解、修改、调试与格式化。触发词：脚本、script、ecs、语法、代码、编写、函数、变量、循环、编译、格式化。",
            TriggerTools = new HashSet<string> { "read_script", "write_script", "edit_script", "compile_script", "format_script", "grep_script" },
            Priority = 50,
        };

        const string body = """
            # ECScript 脚本编写

            ## 编码规范（铁律）

            - 关键字必须全大写（IF/FOR/FUNC/WAIT/PRINT），禁止混用大小写
            - 逻辑运算符全小写（and/or/not）
            - 变量以 `$` 开头
            - 脚本以行为基础，每条指令独占一行
            - 流程控制块必须换行书写，IF/ENDIF、FOR/NEXT、WHILE/END 等各占一行，禁止同行
            - **严格遵循语法**：只能使用语法说明中明确列出的关键字、函数、结构，禁止自创语法、臆造函数
            - **写完必须编译**：每次 write_script 后必须调用 compile_script 验证，失败则查 get_logs 修复
            - **完整流程**：write_script → compile_script →（失败则修复重编）→ run_script
            - **不确定的语法宁可不用**：没把握时用更简单的写法，或先向用户确认

            ## 核心速查（高频用法）

            - 注释：`# 注释`
            - 输出：`PRINT "文本" & $变量`
            - 推送：`ALERT 已完成 & $count & 次`
            - 延时：`WAIT 100`（毫秒）
            - 按键：`A`/`B`/`X`/`Y`/`L`/`R`/`ZL`/`ZR`/`HOME`/`PLUS`/`MINUS`/`CAPTURE`
            - 按住/松开：`HOME DOWN`/`HOME UP`
            - 指定延时：`A 100`（按 100ms）
            - 摇杆方向：`LS UP/DOWN/LEFT/RIGHT/...`，`RS` 同理
            - 摇杆角度：`LS 0`（右）/`LS 90`（上）/`LS 180`（左）/`LS 270`（下）
            - 摇杆回中：`LS RESET`
            - 条件：`IF $a > 0` ... `ELIF` ... `ELSE` ... `ENDIF`（无 THEN）
            - 计次循环：`FOR 10` ... `NEXT`
            - 范围循环：`FOR $i = 1 TO 10` ... `NEXT`
            - 无限循环：`FOR` ... `NEXT`，用 `BREAK` 跳出（最多 3 层）
            - While/Until：`WHILE cond`...`END` / `UNTIL cond`...`END`
            - 图像识别：`$match = @标签`（返回 0-100 匹配度）；`IF @目标 > 90`...`ENDIF`
            - 函数定义：`FUNC name($p:TYPE):TYPE` ... `ENDFUNC`；返回 `RETURN $value`
            - 函数调用：`name($arg)`（表达式）或 `name $arg`（语句）

            ## 需要详细语法时

            以上是高频用法。遇到以下情况，调用 `read_skill(skill_name="ecscript-authoring", reference="syntax-full.md")` 获取完整语法参考：

            - 需要位运算、整除、取余、字符串转义等运算符细节
            - 需要数组切片、结构体、递归、重载、EXTERN/IMPORT 等高级特性
            - 需要完整的内置函数清单（TIME/RAND/LEN/FRAME/OCR/JQ/AMIIBO 等）
            - 编译错误信息看不懂，需要对照语法定位

            ## 脚本模板

            常见自动化场景的参考脚本，按需调用 `read_skill` 拉取：

            - 孵蛋/放生类脚本：`read_skill(skill_name="ecscript-authoring", reference="cookbook-eggs.md")`
            - 过帧/SL 类脚本：`read_skill(skill_name="ecscript-authoring", reference="cookbook-frame-skip.md")`
            """;

        var references = new Dictionary<string, string>
        {
            ["syntax-full.md"] = """
                # ECScript 完整语法参考

                ## 编码规范

                - 关键字必须全大写（IF/FOR/FUNC/WAIT/PRINT），禁止混用大小写
                - 逻辑运算符全小写（and/or/not）
                - 变量以 `$` 开头
                - 脚本以行为基础，每条指令独占一行
                - 流程控制块必须换行书写，IF/ENDIF、FOR/NEXT、WHILE/END 等必须各占一行，禁止写在同一行

                ## 基础操作

                - 注释：`# 注释内容`
                - 输出：`PRINT "文本" & $变量`（& 拼接）
                - 推送：`ALERT 已完成 & $count & 次`
                - 延时：`WAIT 100`（毫秒）或直接写数字 `500`

                ## 按键与摇杆

                - 按键：`A`/`B`/`X`/`Y`/`L`/`R`/`ZL`/`ZR`/`HOME`/`PLUS`/`MINUS`/`CAPTURE`/`LCLICK`/`RCLICK`
                - 方向键（DPad）：`UP`/`DOWN`/`LEFT`/`RIGHT`/`UPLEFT`/`UPRIGHT`/`DOWNLEFT`/`DOWNRIGHT`
                - 按住/松开：`HOME DOWN`/`HOME UP`
                - 指定延时：`A 100`（按 100ms）
                - 摇杆方向：`LS UP/DOWN/LEFT/RIGHT/UPLEFT/UPRIGHT/DOWNLEFT/DOWNRIGHT`，`RS` 同理
                - 摇杆角度：`LS 0`（右）/`LS 90`（上）/`LS 180`（左）/`LS 270`（下），0-360 度
                - 摇杆回中：`LS RESET`
                - 摇杆时序：`LS UP,100`（推 100ms 后自动回中）

                ## 流程控制（每个关键字独占一行）

                ### 条件
                ```
                IF $a > 0 # 没有THEN关键字
                    # 条件为真时执行
                ELIF $a == 0
                    # 其他条件
                ELSE
                    # 以上都不满足
                ENDIF
                ```

                ### 循环
                - 计次循环：`FOR 10`...`NEXT`
                - 范围循环：`FOR $i = 1 TO 10`...`NEXT`
                - 无限循环：`FOR`...`NEXT`，用 `BREAK` 跳出
                - While 循环：`WHILE $count < 10`...`END`
                - Until 循环：`UNTIL $count >= 10`...`END`（先执行再判断）
                - 跳出/跳过：`BREAK [层数]`（最多 3 层）/`CONTINUE`

                ## 图像识别

                - 语法：`$match = @标签名称`（返回 0-100 匹配度）
                - 条件判断：`IF @目标 > 90`...`ENDIF`
                - 匹配度：90-100 高度匹配，80-89 较好，70-79 一般，<70 较低
                - 需要在脚本同目录的 `imglabel/` 下有对应 IL 标签文件

                ## 变量与数据类型

                - 变量：`$name = 值`，支持中文变量名 `$生命值 = 100`
                - 整数：32 位有符号，`$n = 42`
                - 布尔：比较表达式产生，`$flag = 1 > 0`
                - 字符串：`"双引号"` 或 `'单引号'`，支持转义 `\n \t \\ \"`
                - 数组：`$arr = [1, 2, 3]`，访问 `$arr[0]`，切片 `$arr[1:4]`

                ## 运算符

                - 算术：`+ - * /`，整除 `\`，取余 `%`
                - 位运算：`& | ^ ~ << >>`
                - 比较：`== != < > <= >=`
                - 逻辑：`and or not`（全小写）
                - 字符串：`& +`（连接），`in`（包含检测）
                - 赋值：`= += -= *= /= \= %= &= |= ^= <<= >>=`

                ## 函数

                - 定义：`FUNC name($param:TYPE):TYPE`...`ENDFUNC`
                - 返回：`RETURN $value`
                - 调用：`name($arg)`（表达式）或 `name $arg`（语句）
                - 无返回值调用：`CALL name`
                - 类型标注：`:INT`/`:BOOL`/`:STRING`，INT 可省略
                - 递归/重载均支持

                ## 结构体

                - 定义：`STRUCT Name`...`END`，字段 `$x:INT`
                - 实例化：`$p = Point{}`
                - 访问：`$p.x = 10`

                ## 内置函数

                - `TIME()` 运行时间 / `RAND(max)` 随机数 / `LEN(arr)` 长度
                - `APPEND(arr,val)` 添加元素 / `STRING(val)` 转字符串 / `INT(val)` 转整数
                - `BEEP(freq,dur)` 蜂鸣器 / `ENV(name)` 环境变量
                - `ENCODE(bytes,编码)` 字节数组编码 / `JQ(json,query)` JSON 查询
                - `FRAME(x,y,w,h)` 截屏 / `OCR(x,y,w,h)` 文字识别 / `ROI(img,x,y,w,h)` 区域提取
                - `AMIIBO(index)` 切换 Amiibo 槽位

                ## 模块与 FFI

                - 导入库：`IMPORT "lib.ecs"`（放在脚本开头，库文件在 `lib/` 目录）
                - 外部函数：`EXTERN FUNC name($p:TYPE):TYPE FROM "dll.dll"`，支持 `AS` 指定导出名
                """,

            ["cookbook-eggs.md"] = """
                # 孵蛋/放生类脚本模板

                ## 孵蛋循环（通用骨架）

                ```
                # 孵蛋：取蛋 → 步行孵化 → 检测孵化 → 循环
                # $count 已孵化计数
                $count = 0
                FOR
                    # 取蛋（按 A 在培育屋对话）
                    A 100
                    WAIT 200
                    A 100
                    WAIT 300

                    # 步行孵化（原地转圈模拟步数）
                    LS UP,5000
                    LS UP,5000

                    # 检测孵化（标签 imglabel/hatched.il）
                    IF @hatched > 90
                        $count = $count + 1
                        PRINT "已孵化 " & $count & " 只"

                        # 放生流程（进入盒子、选择、放生）
                        # ...根据具体游戏调整按键序列

                        # 满 N 只后推送通知
                        IF $count % 30 == 0
                            ALERT "已孵化 " & $count & " 只，注意检查"
                        ENDIF
                    ELSE
                        # 未孵化，继续步行
                        WAIT 1000
                    ENDIF
                NEXT
                ```

                ## 设计要点

                - **先编译再运行**：每次修改后必须 `compile_script` 验证
                - **标签前置**：图像识别依赖 `imglabel/` 下的 IL 标签文件，缺失会一直返回 0
                - **状态可观测**：关键节点用 `PRINT` 输出，便于 `get_logs` 排查
                - **异常退出**：长时间无变化时考虑加超时跳出逻辑
                """,

            ["cookbook-frame-skip.md"] = """
                # 过帧/SL 类脚本模板

                ## 精准过帧循环

                ```
                # 精准过帧：循环跳过剧情动画帧
                # $target 目标帧数
                $target = 10000
                $i = 0
                WHILE $i < $target
                    # 跳过动画
                    B 100
                    WAIT 50
                    A 100
                    WAIT 200

                    $i = $i + 1

                    # 周期性检查是否到达目标画面
                    IF $i % 100 == 0
                        IF @target_frame > 90
                            PRINT "已到达目标帧 " & $i
                            BREAK
                        ENDIF
                    ENDIF
                END

                PRINT "过帧完成"
                ALERT "过帧完成"
                ```

                ## SL（存档读档）循环

                ```
                # SL：存档 → 触发事件 → 不满意则读档重试
                # $tries 已尝试次数
                $tries = 0
                FOR
                    # 触发事件
                    A 100
                    WAIT 3000

                    # 检测是否命中目标（如紫光、稀有）
                    IF @rare_event > 90
                        PRINT "命中目标！停止 SL"
                        ALERT "命中目标"
                        BREAK
                    ENDIF

                    # 未命中 → 读档重试
                    HOME 100
                    WAIT 500
                    A 100      # 选择读档
                    WAIT 2000

                    $tries = $tries + 1
                    PRINT "第 " & $tries & " 次 SL 未命中"
                NEXT
                ```

                ## 设计要点

                - **过帧精度**：硬件延时（USB 传输、按键处理）会影响精度，关键帧检测建议用图像标签而非纯计数
                - **SL 安全性**：确保存档点正确，否则可能卡死；建议先 `get_frame` 确认画面再执行
                - **资源回收**：长时间运行注意 `BREAK` 出口，避免无限循环耗电
                """,
        };

        return new Skill(manifest, body, references);
    }

    private static Skill CreateDeviceControl()
    {
        var manifest = new SkillManifest
        {
            Name = "device-control",
            Description = "控制脚本在设备上的运行、停止，以及设备/视频源/手柄连接状态查询。触发词：运行、执行、自动、跑脚本、运行脚本、设备、连接、单片机、手柄。",
            TriggerTools = new HashSet<string> { "run_script", "stop_script", "get_device_status", "get_logs" },
            Priority = 60,
        };

        const string body = """
            # 设备控制与脚本执行

            ## 脚本执行铁律

            1. **严格遵循 ECScript 语法**：只能使用语法说明中明确列出的关键字、函数、语法结构，绝对禁止自创语法、臆造函数或编造不存在的 API。编写脚本时配合 `ecscript-authoring` 技能。
            2. **写完必须编译**：每次 write_script 后必须调用 compile_script 验证，编译不通过则查看 get_logs 定位错误并修复，直到编译成功后才能 run_script
            3. **完整流程**：write_script → compile_script →（失败则修复重编）→ run_script
            4. **不确定的语法宁可不用**：如果对某个语法结构没有把握，用更简单确定的写法替代，或先向用户确认

            ## 执行注意

            - 脚本运行是异步的，run_script 后脚本在后台执行，不会阻塞对话
            - 运行中的脚本可以通过 stop_script 随时停止
            - run_script 前应通过 get_device_status 确认设备已连接
            - 发现异常时立即 stop_script 并报告
            - 一次只能运行一个脚本，已有脚本运行时 run_script 会返回 Retryable，需先 stop_script

            ## 失败处理

            - 编译失败：调 `get_logs` 查看错误信息，对照 `ecscript-authoring` 技能的语法参考修复，重新编译
            - 设备未连接：提示用户连接单片机/采集卡，不要盲目重试
            - 运行无响应：先 `stop_script`，再 `get_device_status` 检查连接状态
            """;

        return new Skill(manifest, body);
    }

    private static Skill CreateVisionAnalysis()
    {
        var manifest = new SkillManifest
        {
            Name = "vision-analysis",
            Description = "通过截取当前视频画面让 AI 理解图像内容，进行视觉观察、画面分析、状态识别。触发词：画面、屏幕、截图、看看、识别、看到、图像、视觉、观察。",
            TriggerTools = new HashSet<string> { "get_frame" },
            Priority = 70,
        };

        const string body = """
            # 视觉观察模式

            通过 `get_frame` 获取当前视频画面进行分析。

            ## 调用规则

            1. **每次只能调用一次 get_frame**，禁止在同一次响应中同时调用 get_frame 和其他工具
            2. 调用 get_frame 后，本轮响应只能分析画面内容，不得调用任何其他工具
            3. 分析完成后在回复中说明观察结果和下一步计划
            4. 下一轮响应再根据分析结果执行操作

            ## 分析要求

            - 仔细描述画面中的关键元素（界面、文字、状态、图标等）
            - 将画面内容与用户目标对比，判断当前进度
            - 明确说明下一步需要做什么（继续观察 / 执行操作 / 任务完成）

            ## 视觉 + 脚本联合的 ReAct 循环

            当任务同时涉及视觉观察和脚本执行时，遵循以下循环模式（每轮只做一件事）：

            **观察轮**（只做一件事）：
            - 调用 get_frame → 收到画面 → 分析内容 → 回复分析结果和下一步计划
            - 禁止同时调用其他工具

            **行动轮**（不获取画面）：
            - 根据上一轮的分析结果执行操作（编写脚本、编译验证、运行脚本、停止脚本等）
            - 行动完成后说明执行结果

            **验证轮**（行动后的观察）：
            - 调用 get_frame 验证行动结果 → 分析是否达成目标 → 决定继续或结束

            **反思轮**（验证后的自我评估，当遇到问题时触发）：
            - 评估：上一步是否成功？结果是否可靠？
            - 识别：推理是否有漏洞？是否遗漏关键信息？
            - 调整：应该坚持、修正还是彻底改变路径？
            - 更新：是否需要修正对问题的理解？

            ## 主动结束条件

            满足以下任一条件时，停止调用工具并回复用户：
            - 用户的目标已明确达成
            - 确认无法完成任务（设备异常、画面无变化等）
            - 连续两次操作结果相同，说明策略无效
            - 剩余轮次不足 5 轮

            ## 注意事项

            - 不要假设操作一定成功，每次行动后必须 get_frame 验证
            - 脚本运行是异步的，run_script 后需等待再 get_frame
            - 发现异常时立即 stop_script 并报告
            - 轮次有限，每一步都要有明确目的
            - 遇到连续失败时，先反思再行动，避免盲目重试
            """;

        return new Skill(manifest, body);
    }
}