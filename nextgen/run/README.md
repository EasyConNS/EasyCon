# 运行资源作用域

`easycon_sense::run::RunScope` 在单次原语派发外提供运行资源边界，Expr／IR 和组合子由外部 [ir 执行器](../ir/README.md) 负责。使用示例见 [模块文档与可编译示例](../src/run.rs)，测试见 [作用域测试](../src/run/tests.rs)。

## 装配与所有权

宿主先创建物理采集会话和能力提供者，再装配 `RunBindings`：

| 资源 | 作用域持有方式 | 退出行为 |
|---|---|---|
| videos／audio | 拥有本运行专用的订阅 Box | 销毁订阅，不停止外部物理来源 |
| pads | 独占可变借用整个手柄状态 | 全中立复位，保留外部连接 |
| matchers／ocr／text／meta | 借用外部提供者 | 结束借用，NotRequired |

每个运行从 `VideoSession::subscribe()`／`AudioSession::subscribe()` 创建自己的订阅，避免共享音频游标。把手柄加入作用域即授予整个状态的独占控制：即使未调用动作也会在退出时复位，不恢复原先按住的状态，不做跨运行按键计数。物理来源的 stop 和外部手柄的 disconnect 仍由其所有者负责。

matchers／ocr 可借用 [NativeMatcher／NativeOcr](../native-work/README.md)。RunScope 会等待这次调用返回，但不拥有或关闭其线程；运行结束后宿主可以复用，最终显式 `shutdown().await`。模型配置由上层工作流提供，资源生命周期 token 与单次运行 token 分开。

本次运行若需要时钟／日志，宿主应单独创建 MetaContext；RunScope 不会重置传入上下文的时钟。TypeText 仅是借用端口，当前没有文本状态释放契约或真实后端。

## 调用与结果

`RunScope::new(bindings, &parent)` 创建子取消令牌。parent 取消向下传播；`run.cancel()` 仅取消本作用域，不取消父级或兄弟。`execute(call).await` 调用现有 PrimitiveHost，返回结构化输出或 `Arc<PrimitiveError>`：

- Recoverable 错误可由调用方处理，作用域仍可使用。
- Cancelled 错误锁定本作用域取消状态。
- 第一次 RunFatal 锁存原始错误，此后所有调用均返回同一原因，不再进入提供者，包括 Meta 原语。
- 已交付动作的成功回执保留，晚到取消由 finish 裁决，不伪造成尚未发送。

调用方在等待自己启动的工作结束后，必须用 `run.finish(RunOutcome::from(result))` 或显式候选结果取得 `RunCompletion<T>`。该 API 消费作用域，不能重复调用 finish；可变借用也阻止尚有 execute future 时执行 finish。

finish 先裁决已锁存的致命错误和取消，再清理，最后重新检查取消与清理失败。优先级为清理失败、致命失败、取消、普通候选结果；被覆盖的候选成功值、typed 错误或取消保存在 prior 中，按覆盖顺序排列。RunOutcome::from 将取消错误归一化为 Cancelled；具体取消来源由 IR 引擎记录。需要 UserStop／Shutdown／Aborted 时，宿主使用 IR 的 RunControl 和 execute_controlled；本层继续只负责资源收尾，不解析停止原因。

## 清理报告

`CleanupReport.entries` 为每项资源记录 capability、binding 和状态；手柄额外记录连接 session：

- NotRequired：提供者只借用，无本次运行拥有的清理工作。
- Released：订阅已销毁，或中立报告成功交付。手柄保留 InputReceipt，包括 Written／Accepted、序号和时间；不能据此认定游戏已响应。
- ReleaseUnconfirmed：复位返回 typed 错误，释放不能确认。故障会话拒绝复位时也属于此状态。

每个手柄都使用新的未取消 token，清理失败后继续处理其他资源。任何 ReleaseUnconfirmed 将最终结果升级为 RunFailure::CleanupFailed（RunFatal），原候选结果仍可检查。Drop 只尝试一次收尾并忽略报告，不能替代显式 finish。

清理是同步调用，沿用底层事务预算；异步宿主仍需安排并等待受管理的阻塞工作。这里不创建外部任务，也不提供节点级任务收拢、截止裁决、取消原因树或 trace。提供者应返回 typed 错误而非 panic；不保证从提供者 panic、进程中止或卡死的驱动中完成清理。


## IR 子作用域

IR 执行器通过内部 execute_in 使用局部子 token，子请求取消不锁定整个运行。子作用域只释放自身触碰的按键状态，清理使用新 token；订阅仍属于运行，不在每轮重试中重新订阅。子报告保存到 Execution.child_cleanup，失败由引擎锁存为 RunFatal。恢复、超时与并行的任务收拢由 IR 的本地中央调度队列完成；本模块继续保持原语宿主与运行资源职责。
