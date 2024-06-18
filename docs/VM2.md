## V2指令集设计文档

设计思路：
1字节opcode,多字节参数
高度抽象

opcode类型：
-立即数指令
-移动寄存器指令
-算数运算
-移位，位运算
-分支处理，跳转指令
-栈操作
-函数调用
-系统功能调用
-按键指令

寄存器设计：
R0-R15共16个寄存器，可以用4bit表示编号,大小32bit

堆栈设计：
执行栈
调用栈
循环栈

标志位：
除零标志
溢出标志
比较结果标志

#### 空指令
`nop`

#### 立即数指令
`set`
`set_8`,rd,<imn> // 将立即数<imn>放到寄存器rd中
`set_16`
`set_32`

#### 移动指令
`move`,rd,rs //将rs内容复制到rd寄存器
`clear`,rd //重置rd寄存器

#### 算数指令
加减乘除（立即数与寄存器，寄存器与寄存器）
`inc`,rd,<imn>
`add`,rd,rs
`add2`,rd,r1,r2
`addi`,rd,rs,<imn>
`dec`
`sub`
`sub2`
`subi`
`mul`,rd,rs
`div`
`mod`

#### 位运算指令
`and`,rd,rs
`or`
`not`
`xor`
`shl`,rd,<imn> //左移
`shr`,rd,<imn> //右移

#### 分支跳转指令
`beq` //比较相等 //和0,和imn,和寄存器
`bne` //比较不等
`bgt` //比较大于
`bgeq` //比较大于等于

`jmp` //无条件相对跳转
`jtrue` //成功跳转
`jfalse` //失败跳转

#### 栈指令
`push`
`pop`
`pushall`
`popall`

#### 函数指令
`call`,<addr> //保存PC指针
`ret`

#### 按键指令（太过常用所以内置）
`key`,<code>,n //n*10ms持续时间
`key_cmpress`,<code>,n //n*50ms持续时间
`key_down`,<code>
`key_up`,<code> //松开按键
`stick`,L|R,<code>,n //n*50ms持续时间
`stick_down`,L|R,<code>
`stick_reset`,L|R,<code>

#### 系统调用指令
`srv`,<num>,[args] //调用<num>号系统命令（按键进阶、等待、获取时间戳等），具体命令参数使用的寄存器自行定义

#### 程序结束指令
`end`