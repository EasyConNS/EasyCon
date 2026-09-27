# EasyCon 全项目架构与模块边界审查报告

- 审查日期:2026-09-26 · 分支 dev(HEAD = b0a6546 "t1",工作区仅未跟踪 `scripts/delete-nul.bat`)
- 方法:Phase 0 基线(build/test)→ Phase 1 机械扫描 → Phase 2 十二个只读子代理分四波深读全部模块 → Phase 3 全部 P0/P1 由主审亲读调用链复核 → 本报告
- 基线状态:`dotnet build` 0 错误 / 259 警告;`dotnet test -c Release` **977 通过 / 0 失败 / 38 跳过**(6 测试项目全绿)
- 严重度:P0 崩溃/数据损坏 · P1 功能失效 · P2 边界破坏/高维护成本 · P3 改进建议
- 置信度:**确认** = 读通完整调用链;**疑似** = 静态推断未完全验证。标注 **[亲验]** 的条目为主审在 Phase 3 逐行复核过;其余为子代理 grep/精读结论(附录 B)
- 豁免遵守:去 View 化命名空间/物理错位、Core→Avalonia IVT、SdlScancodeMap/SelectionMode 放置、.editorconfig 约定、2026-09-26 GUI 修复清单(仅抽查质量,未作为新发现)均未上报

---

## 1. 执行摘要

**总体健康度:中上。** 能力模型与 MVVM 编译器强制这两条核心架构支柱是真实生效的,脚本执行面严格面向接口,测试文化(977 测试、Fake 接缝、corpus 差分设计)明显高于同类项目平均水平;但存在三条系统性裂缝:**①CI 从未真正执行过 C VM 差分与 OCR 原生链路对拍(38 个 Skip 的 3/4 同源),项目的核心安全网形同虚设;②并发边界在 Device 写循环与 MCP/AI 工具注册两条新链路上失守;③大量死代码与孤儿工程(WinInput、UI.Common、FirmwareService、V1.cs、WS_Message、deps/*.dll、78MB ONNX 模型)稀释了架构叙述**。

Top 5(按修复性价比排序):

1. **[P0] 原生 VM 帧槽操作数零边界检查**(AR-001)——损坏/手写 .ecx 即堆越界读 + 垃圾句柄 retain/release(UAF)。
2. **[P1] CI 上 C VM 差分对拍恒为 Ignore**(AR-006)——托管解释器 ↔ ecs_vm.c 语义漂移无任何机械拦截;Windows 无 cc、macOS 只编译不测。
3. **[P1] Device 重连双竞态**(AR-002/003)——旧写循环不可取消,重连瞬间新旧 Loop 并发写 HID、新旧 SerialPort Open/Close 互踩,间歇性连接失败。
4. **[P1] MCP stdio 传输 stderr 无人读取**(AR-004)——子进程日志写满 4KB 管道即整体挂死,且读循环死亡被空 catch 吞掉。
5. **[P1] 跨模块常量表/映射表手抄漂移**(AR-008/011)——LSP 关键字与内建函数表已和 Script 实际符号双向漂移(提示不存在的 `CALL`、缺 `UNTIL/STRUCT`、OCR 签名错误);WinInput 与 SDLInput 双份 30 键映射表。

---

## 2. 真实依赖图与基线偏差

### 2.1 实际 ProjectReference 图(由 csproj 逐个提取)

```mermaid
graph TD
    subgraph 顶层消费者
        AVALONIA[EasyCon2.Avalonia<br/>GUI 宿主]
        ACORE[EasyCon2.Avalonia.Core<br/>VM/纯逻辑层]
        CLI[EasyCon2.CLI]
    end
    subgraph 组合层
        CORE[EasyCon.Core<br/>能力抽象 + 组合根/Runner]
    end
    subgraph 叶子实现库(零 ProjectReference)
        SCRIPT[EasyCon.Script]
        DEVICE[EasyCon.Device]
        CAPTURE[EasyCon.Capture]
        EZT[EzTesseract]
    end
    subgraph 输入后端
        SDL[EasyCon.SDLInput]
        WIN[EasyCon.WinInput 死项目]
    end
    LSP[EasyCon.Lsp]
    UIC[EasyCon2.UI.Common<br/>net6.0 遗物]

    CLI --> CORE & LSP
    LSP --> SCRIPT
    CORE --> SCRIPT & DEVICE & CAPTURE
    CAPTURE --> EZT
    SDL --> CORE & DEVICE
    WIN --> CORE
    ACORE --> CORE & LSP
    AVALONIA --> UIC & CORE & ACORE & SDL
```

- `EasyCon.Vm` = 纯原生 C99(`native/ecs_vm.c/h + ecs_main.c + build.sh`),**不在 slnx**,无 MSBuild 集成;由 `test/EasyCon.Tests/Support/CvmRunner.cs` 现场编译做差分对拍,CI 的 macOS job 仅编译验证。
- `src/EasyCon.Server` 为**空目录**(无 csproj 无代码);全 src 无 HttpListener/Kestrel/WebSocket/Tcp 代码(原 `Core/Assist/WebSocket.cs` 已于 t0 删除)。现实对外面 = MCP 客户端栈 + LSP(stdio/named pipe/TCP)+ CLI。
- 孤儿/残骸:`test/BenchTest`(不在 slnx,硬编码绝对路径)、`src/EasyCon2`(旧 WinForms,仅 Amiibo/ 残留)、`src/EzCv`、`src/EasyCon.Script.Jit`(仅 bin/obj,git 未跟踪)、`docs/plans/`、`docs/superpowers/`(空目录,git 未跟踪)。

### 2.2 与 §2 基线的偏差表

| 基线声明 | 实际 | 判定 |
|---|---|---|
| Core 是最底层,实现层只依赖 Core | Device/Capture/Script/EzTesseract 均**零依赖**;Core 反向引用三者,是**组合根 + 能力抽象混合体** | 基线描述失真(见 AR-013):能力模型本身干净,但抽象与装配同程序集导致 GamePadAdapter 等被迫 public |
| 脚本运行只面向接口 | `EcxVm.BuildHost` 只消费 IPadInput/IConsoleIo/IVisionService/IFileSystem;两宿主均经 IScriptEngine/IScriptSession | **兑现**(但留 ReadLine/Beep 两个硬编码 Console 洞,AR-026) |
| MVVM 编译器强制 | Avalonia.Core.csproj 零 Avalonia 包,全层 0 `using Avalonia`,无字符串间接 | **兑现**;但 App 项目另有 12 个 VM 在强制区之外(AR-010) |
| 原生边界四条(Vm/OpenCV/Tesseract/SDL) | 属实;另加 CvmRunner 测试侧编译路径 | 见横切 §5.G |
| 静态单例 vs DI 有清晰规则 | 混合使用,诊断日志存在 3 个静态 LogSink + ILogService 四条路径(AR-031) | 部分成立 |
| 「远程控制 HTTP/WS 服务(EasyCon.Server)」 | **不存在**(空目录);t0 已删 WebSocket.cs | 任务书过时,见 §3 注 |

---

## 3. 模块职责速览(12 .NET 项目 + Vm 原生 + 孤儿)

| 项目 | 一句话职责 | 边界健康度 |
|---|---|---|
| EasyCon.Core | 能力抽象(Capabilities)+ 脚本引擎面(IScriptEngine/Session)+ 组合根(Runner/GamePadAdapter/装配) | 🟡 抽象干净,组合根混入致 public 面虚胖(93 public) |
| EasyCon.Device | 串口硬件通信(NintendoSwitch/TTLSerialClient/HID 报文) | 🔴 并发竞态集群(2×P1)+ README 失真 |
| EasyCon.Capture | 采集卡/屏幕捕获(FrameStore 租约)+ OCR 引擎缓存 | 🟡 核心(FrameStore)优秀,边缘(死代码/全局命名空间/静态 OCR 副本)失守 |
| EasyCon.Script | ECS 解析/绑定/SSA/字节码/托管解释器(零依赖叶子) | 🟢 工程质量最高;opcode 双枚举无机械对齐为已知设计债 |
| EasyCon.Vm(native) | MCU 端 C99 字节码解释器 + 对拍参考宿主 | 🔴 内存安全(P0)+ CI 零执行 + 文档失真 |
| EzTesseract | Tesseract/Leptonica 零依赖绑定 | 🟢 内存契约无泄漏;失败路径黑盒 |
| EasyCon.Lsp | ECS 语言服务(复用 Script 语法树) | 🟡 可见性典范(1 public),常量表手抄漂移 |
| EasyCon.Server | (空目录,无代码) | ⚪ 建议删除 |
| EasyCon2.Avalonia.Core | MVVM 纯逻辑层(VM/Services/AiAgent/Mcp/Terminal/Editor) | 🟡 纯度真实生效;Mcp/AiAgent 新链路并发失守 |
| EasyCon2.Avalonia | View 宿主 + 12 个遗留 VM + Services | 🔴 强制区外 VM 群 + 上帝类 + L10n 半失效 |
| EasyCon2.UI.Common | WinForms 时代 resx 资源(net6.0,唯一非 net10 项目) | 🔴 遗物:37MB 死资源,AvaloniaResource 为 no-op |
| EasyCon.WinInput | Windows 输入模拟(钩子+映射) | 🔴 死项目:零生产消费者,映射表与 SDLInput 双份漂移 |
| EasyCon.SDLInput | SDL3 输入后端(唯一生产输入链) | 🟢 修复后质量高于平均;Stop 不复位设备为残留缺陷 |
| EasyCon2.CLI | 命令行入口(8 子命令,LSP 宿主) | 🟡 取消链路好;退出码/能力对齐/输出分流失守 |
| 孤儿:BenchTest、src/EasyCon2、EzCv、Script.Jit | 残骸 | ⚪ 清理项(AR-044) |

---

## 4. 分级发现清单

### 4.1 P0(1 项)

**AR-001 | P0 | 原生内存安全 | src/EasyCon.Vm/native/ecs_vm.c:1146-1150(代表点)【亲验】**
```c
for (int32_t i = 0; i < b; i++)
{
    o->arr.items[i] = R[c + i];   /* c+i 无 fn->nslots 检查 */
    retain(vm, o->arr.items[i]);
}
```
- 影响:帧槽字节操作数(`R[a]/R[b]/R[c]`)在执行期**全部裸信任**——同族还有 exec_data_op 全部算术指令、Slice(:1209/1217)、GetFI(:1436)、PutFI(:1453,`ext` 可为负)、Ret 跨帧写(:1692)。而 Call/CallN 的 `a+i>=nslots`、LoadK/LoadG 的 16 位索引**有**检查,防御不对称。损坏或手写的 .ecx 即堆越界读 + 垃圾值被 `retain/release` → use-after-free。前置条件:编译器正常产物不会触发(`EcsFormat.BuildTable` 自检 C# 侧槽位);但 .ecx 是跨机器分发到 MCU 的产物格式,无签名/校验链,docs/EcmEcxFormat.md §2.3 承诺的「指令流静态抽查」在加载期并不存在(见 AR-036)。
- 修复方向:加载期按 `ecs_op_has_ext` 步进做静态槽位校验,或执行期统一 `nslots` 断言。置信度:确认。

### 4.2 P1(11 项)

**AR-002 | P1 | 并发 | src/EasyCon.Device/JoyStickDevice.cs:52-58 + NintendoSwitchPriv.cs:51-54【亲验】**
```csharp
CancelStaleLoop();                 // 仅 source.Cancel()
source = new();
Task.Run(() => Loop(source.Token), source.Token);
// RunLoop: if (_keystrokes.Count == 0) _ewh.WaitOne();   // 无限等待,不响应 token
```
- 影响:RunLoop 只在 while 顶部检查 token,旧 Loop 重连后仍阻塞在不可取消的 `WaitOne()`;下次按键 `Signal()` → `_ewh.Set()` 唤醒**新旧两个 Loop**,旧 Loop 在复查 token 前进入 lock 并经共享 `clientCon`(已指向新连接)多写一份 HID 报文,随后才退出。27 行注释声称的目标实际未达成;并发进出 lock 还可能丢/重按键状态。
- 修复方向:循环体内每轮复查 token / 改用可取消等待;重连前等待旧任务退出(带超时)。置信度:确认。

**AR-003 | P1 | 并发/资源 | src/EasyCon.Device/Connection/TTLSerialClient.cs:69-73,140-143 + NintendoSwitchCmd.cs:46-47【亲验】**
```csharp
public override void Disconnect()
{
    source?.Cancel();        // 不等待 _t 退出
    ClearQueue();
}
...
finally { _sport.Close(); }        // 旧 Loop
_sport = new SerialPort(...);      // 新 Connect()
```
- 影响:`_TryConnect` 先 `Disconnect()` 再新建连接并异步 `_sport.Open()`;旧 Loop 的 `finally _sport.Close()` 与新 `Open()` 在同一线程池上竞争同一 COM 口(Windows 独占打开)→ UnauthorizedAccessException → 间歇性连接失败。115200 失败回退 9600 重试(JoyStickDevice.cs:62-63)与 `DeviceService.AutoConnect` 重连同端口均踩中。连带:`Disconnect` 后 `_t != null` 守卫使客户端一次性、不可复用(无文档约定);CTS 与 SerialPort 从不 Dispose。
- 修复方向:Disconnect 等待写任务退出(带超时)后再返回。置信度:确认。

**AR-004 | P1 | 并发/进程生命周期 | src/EasyCon2.Avalonia.Core/Mcp/CustomStdioClientTransport.cs:40,66-69【亲验】**
```csharp
RedirectStandardError = true,   // 重定向了,但全程无人读取
...
var transport = new StdioProcessTransport(
    process.StandardInput.BaseStream,
    process.StandardOutput.BaseStream,   // stderr 不在内
    Name);
```
- 影响:MCP 服务器普遍向 stderr 打日志;管道缓冲(约 4KB)写满后子进程 `WriteFile` 阻塞 → 服务器整体挂死、所有工具调用超时。叠加 AR-030 的读循环空 catch,失败完全无诊断。修复方向:接入 `process.StandardError` 后台排空任务(丢弃或记日志),或改 `RedirectStandardError = false`。置信度:确认。

**AR-005 | P1 | 并发 | src/EasyCon2.Avalonia.Core/AiAgent/AiAgentViewModel.cs:229-234 + Tools/ToolRegistry.cs:10【亲验】**
```csharp
private void OnMcpToolsChanged()
{
    _tools.Unregister(t => t is McpToolAdapter);   // 裸 Dictionary 写
    McpTools.RegisterAll(_tools, _mcpManager!);
}
// ToolRegistry: private readonly Dictionary<string, IAiTool> _tools = new(...);
```
- 影响:同一 `ToolRegistry` 被编排器后台线程并发读(`AgentOrchestrator.cs:399 _tools.Get`、`:209 ToToolDefinitions`,经 Task.Run);MCP 配置变更 → `ToolsChanged` 重注册时无任何 `IsGenerating` 守卫(对比 `NewChat`:249 有守卫)。生成期间保存 MCP 配置可触发 Dictionary 并发读写异常/内部结构损坏,正在跑的 ReAct 循环崩溃。`ReloadSkills`(:146)同理。2026-09-26 修复覆盖了关窗取消,但未覆盖此重注册路径。
- 修复方向:重注册前检查 IsGenerating 延迟合并,或 ToolRegistry 加锁。置信度:确认(窗口窄)。

**AR-006 | P1 | 测试防线失效 | test/EasyCon.Tests/Support/CvmRunner.cs:19-21 + .github/workflows/ci.yml:57,79,96,118【亲验】**
```csharp
foreach (var candidate in new[] { "/usr/bin/cc", "/usr/bin/clang", "/usr/bin/gcc" })
    if (File.Exists(candidate)) return candidate;    // Windows 恒 null
// ci.yml:windows job 跑 test.bat(无 cc→CVM 差分全 Ignore);
//         macos-14 job 只跑 build-vm.sh,不跑 dotnet test
```
- 影响:托管解释器 ↔ 原生 C VM 的全部差分护栏(Cvm/Corpus/Fuzz/FullChain/AbiContract 的 C 侧)在 CI 中**恒为 Ignore**——38 个 Skip 中约 28 个同源。`.ecx` 语义漂移(双端 opcode/语义/错误码)没有任何机械拦截,只能靠开发者本机恰好是 Linux/macOS。这是整个 Fuzz 差分体系(设计优秀)失效的唯一原因。
- 修复方向:CvmRunner 用 `which cc`/PATH + Windows(MinGW/clang.exe)工具链探测;或 CI 增加 Linux job、macOS job 增跑 dotnet test。置信度:确认。

**AR-007 | P1 | 错误处理链 | src/EasyCon.Capture/OcrEngineCache.cs:52-61,78-85 + src/EasyCon.Core/Capabilities/EngineCacheOcrService.cs:54【亲验】**
```csharp
catch { return false; }                       // Init:吞掉含 datapath/lang 的原始异常
public IOcrRecognizer GetOrInit(string lang)
{
    ...
    Init(lang, DefaultDataPath, "DEFAULT", "SINGLE_LINE");   // 返回值被忽略
    return _engines[lang].Engine;             // Init 失败 → KeyNotFoundException
}
```
- 影响:tessdata 缺失/语言包名错误时,失败被三层吞没(Init 空 catch → GetOrInit 转成误导性 KeyNotFound → `EngineCacheOcrService.Recognize` 的 `catch { return ""; }`)——脚本 OCR **静默返回空串**,零日志,排障几乎不可能。修复方向:Init 失败抛带原因异常或返回 null;Recognize 至少记日志。置信度:确认。

**AR-008 | P1 | 重复实现/契约漂移 | src/EasyCon.Lsp/Constants.cs:5-33【亲验,对照 Lexer.cs:28-63】**
```csharp
"TRUE", "FALSE", "RESET",
"AND", "OR", "NOT",
"CALL",                      // Lexer 全文无 call 关键字
// 缺 Lexer.cs:36 "until"、:56 "struct";BuiltinFunctions 表仅 14 条(实际 26 条),
// "OCR x, y, width, height" 签名在 Script 中不存在(实为 OCR_CONF/__OCR__)
```
- 影响:关键字表与内建函数表手抄自 EasyCon.Script 且**已双向漂移**——补全会提示不存在的关键字、漏掉 `UNTIL/STRUCT`,hover 展示错误 API 签名与缺失的 ENV/ARG/文件族/NET_* 函数。新增关键字/内建时 LSP 必然漏更,无机械同步。修复方向:由 Script 的 `BuiltinFunctions.Manifest` 与 Lexer 关键字表导出只读集合,LSP 只补 LSP 特有文案。置信度:确认(解析本身无重复,`SyntaxTree.Parse` 复用良好)。

**AR-009 | P1 | 退出码语义 | src/EasyCon2.CLI/Program.cs:96,146【亲验】**
```csharp
runScriptCommand.SetAction(async (parseResult, cancellationToken) =>
{ ...
    outdap.Error($"!!编译失败!!{d.Message}...");
    return;                       // Task 而非 Task<int> → 退出码 0
```
- 影响:`run` 的编译失败、单片机连接失败、采集卡打开失败、意外异常全部退出码 0;同文件 format/ir/modules/compile 却正确返回 0/1——同一 CLI 内纪律分裂,脚本/CI 调用方无法感知失败。`lsp` 参数冲突亦 exit 0(:494-498)。另缺 `catch (OperationCanceledException)`:Ctrl+C 被标为「意外错误」打印堆栈(:245-254)。
- 修复方向:`run`/`lsp` 改 `Task<int>`,失败路径返回非零;补 OCE 分类。置信度:确认。

**AR-010 | P1→按分级准则记 P2 | 分层边界 | src/EasyCon2.Avalonia/ViewModels/(12 文件)【亲验:namespace 全为 EasyCon2.Avalonia.ViewModels】**
```csharp
// SwitchConnectionViewModel.cs:1
using Avalonia.Threading;
...
Dispatcher.UIThread.Post(() => { IsConnectingNintendoSwitch = false; ... });
```
- 影响:「所有 ViewModel 在 Avalonia.Core、VM 不得引用 Avalonia 类型」的**编译器强制对这些文件失效**:12 个 VM 留在 App 项目。逐个判定:SwitchConnection/CaptureConnection/FlashFirmware/WelcomeConsole 仅因 `Dispatcher.UIThread` 耦合(业务零依赖,现成 `IUiDispatcher` 正是为此设计,可下沉);ControllerConnection 只差 IWindowService + Dispatcher;FileTree/KeyMapping/Monitor/ESPConfig 真持 Avalonia 类型(留 App 可接受,但应登记豁免名义)。评级说明:边界破坏而非功能失效,故按 §7 准则记 P2,但**修复优先级等同 P1**(侵入性极低)。
- 修复方向:前 4-5 个 VM 下沉 Core 并注入 IUiDispatcher;其余在 AGENTS.md 豁免清单登记。置信度:确认。

**AR-011 | P2 | 死项目/重复实现 | src/EasyCon.WinInput/(整个项目)【亲验:全仓仅测试工程引用】**
```xml
<!-- EasyCon.WinInput.csproj:9 -->
<PackageReference Include="GamepadApi" />
```
- 影响:生产链硬编码 SDLInput(`ControllerService.cs:69-83`),WinInput 全仓零生产消费者(仅自身测试)。它与 SDLInput 双份手工维护 30 项键盘→Switch 映射与手柄 HAT/摇杆/扳机映射(KeyBinder.cs:24-56 ↔ SdlKeyboardInputBinder.cs:94-126,逐行同构),且同一 `KeyMappingConfig` 磁盘档在两侧字段语义不同(VK vs scancode)——SDL 侧有 SchemaVersion+ResolveEffective 干净降级,WinInput 侧无版本守卫,若「复活」读到新档会静默错键。连带 `GamepadApi` 包的唯一消费者就是它。
- 修复方向:删除项目+测试+包条目(推荐),或文档明确定位为遗留参考。评级说明:边界破坏/维护成本,记 P2;清理优先级高。置信度:确认。

**AR-012 | P2 | 文档失真(面向用户/新维护者的系统性失真) | docs/Framework.md:22,67,70;docs/MODULE_DESIGN.md:36-68;docs/GETTING_STARTED.md:43,141**
- Framework.md:22 声称并列「WinForms UI」↔ 现实为 src/EasyCon2 残骸(不在 slnx);:67 声称「Serial、TCP/IP、蓝牙多协议」↔ 全 src 无 Tcp/Bluetooth 代码,仅 TTL 串口;:70 声称「热插拔」↔ 仅静态枚举端口+被动重连。MODULE_DESIGN.md §1.2-1.3 虚构 `ModbusProtocol` 类与 0xAA55/CRC16 帧格式(与真实类名混杂,更具误导性)。GETTING_STARTED 让用户双击 `EasyCon.exe`/`./ezcon run` ↔ 实际产物为 `EasyCon2.Avalonia.<COMMIT_ID>.exe`,ezcon.exe 仅发布脚本重命名后存在。docs/README.md 引用 5 个不存在路径(src/EasyCon.VPad/README.md 等)。
- 修复方向:Framework/MODULE_DESIGN 归档重写(以高保真的 Pipeline.md/ModuleSystem.md 为范本);GETTING_STARTED/README 修正产物名与链接。置信度:确认。

### 4.3 P2(边界破坏 / 高维护成本)

**EasyCon.Core**

**AR-013 | P2 | 分层/组合根 | src/EasyCon.Core/GamePadAdapter.cs:7 + ECCore.cs:9-17**
```csharp
public sealed class GamePadAdapter(NintendoSwitch easyPad, ...) : ICGamePad
public static List<string> GetDeviceNames() => EasyDevice.ECDevice.GetPortNames();
```
- Core 同时承载能力抽象与组合根,迫使适配器层 public 并把 EasyDevice 具体类型钉进公共面;CLI(Program.cs:207-241)与 GUI(ScriptService.cs:228-246)各自复制约 30 行能力装配,新增能力要改 N 处。修复:Core 提供 `CapabilitySetBuilder` 收敛装配,GamePadAdapter 降 internal。置信度:确认。

**AR-014 | P2 | 定时正确性 | src/EasyCon.Core/IO/CustomSleep.cs:6 + Runner/EcxVm.cs:29,90**
```csharp
private static int CurrTimestamp => (int)(DateTime.Now.Ticks / 10_000);
TimeMs = () => (int)((DateTime.Now.Ticks - startTicks) / 10_000),
```
- WAIT 计时与脚本 `TIME()` 锚在墙钟,NTP 校时/手动改时间会使延迟与计时显著偏差;同文件 `AISleep` 已正确用 Stopwatch。对毫秒精度为卖点的自动化是实打实缺陷。修复:统一 `Stopwatch.GetTimestamp()`。置信度:确认。

**AR-015 | P2 | 取消语义 | src/EasyCon.Core/IO/CustomSleep.cs:23 + GamePadAdapter.cs:19**
- `AISleep(int)` 不接收 CancellationToken,GamePadAdapter 在 LowCPU 档把已拿到的 token 丢弃——脚本「停止」后按键仍睡满整个 duration 再 `Up()`。修复:AISleep 加 token。置信度:确认。

**AR-016 | P2 | 平台缺陷 | src/EasyCon.Core/IO/CustomSleep.cs:94-121**
```csharp
var rem = new Timespec();          // 从未传给系统调用
while (nanosleep(req.tv_sec, req.tv_nsec)) { req = rem; }   // rem 恒 {0,0}
```
- Unix nanosleep 被信号中断后剩余时间被丢弃,睡眠比请求值短;`rem` 应传址回收。置信度:确认(触发依赖信号频率,疑似)。

**AR-017 | P2 | 静默数据丢失 | src/EasyCon.Core/Capabilities/DesktopFileSystem.cs:148,154**
```csharp
public void WriteAllText(string path, string content)
{
    try { File.WriteAllText(path, content); }
    catch { }                         // 写失败静默吞掉,返回 void 无哨兵
}
```
- 脚本 FWRITE 族「成功」而文件缺失。同文件 29/34/120/125 四处 Dispose 空 catch 属合理尽力而为,保留。修复:失败经 Console 能力告警或返回 bool;`CloseAllFiles` 全局重置句柄表依赖 GUI 单跑锁,建议收敛到 Run 局部文件表。置信度:确认。

**AR-018 | P2 | 并发(低概率) | src/EasyCon.Core/Config/ConfigManager.cs:135-141**
- `Save` 的 `.tmp` 路径固定且无锁,两线程并发保存同一配置时交错在同一 tmp 上,后完成的 `File.Move` 抛 FileNotFoundException 直炸调用方。修复:按路径加锁或随机临时名+重试。置信度:疑似(窗口小,调用方分布在多线程)。

**AR-019 | P2 | 死代码簇 | src/EasyCon.Core/Assist/WS_Message.cs:30、ProjectManager.cs:9、OcrDelegateFactory.cs:8、ImgLabelExt.cs:6、Runner/NativeLoader.cs:32**
- WS_Message(7 类型)在 WebSocket.cs 删除后成孤岛;ProjectManager(zip 工程管理器,数百行)、OcrDelegateFactory、ImgLabelExt 全仓零消费者;`NativeLoader.RegisterExternFunctions` 零调用。它们拉入 OpenCvSharp/Capture 依赖却无产出。修复:删除;`DelegateOcr` 旧委托路径按 CapabilityAdapters.cs:62 既定计划退役。置信度:确认。

**AR-020 | P2 | 仓库卫生 | src/EasyCon.Core/deps/(btkeylib.dll、libamiibo.dll、libwdi.dll)**
- 三个原生 dll 全仓零引用、零复制规则、无加载代码,既不参与构建也误导「能力是否已发布」判断。修复:删除或正规纳入 RID 分发并注明来源许可证。置信度:确认。

**AR-021 | P2 | 日志盲区 | src/EasyCon.Core(7 处 Console.WriteLine)+ EasyCon.Device(9 处 Debug.WriteLine)+ EasyCon.Capture(10 处 Debug.WriteLine)**
- 库层绕过自家 ILogService 抽象:Core 无条件 Console(GUI 宿主无控制台=诊断蒸发),Device/Capture 的 Debug.WriteLine Release 编译为空——串口/采集/OCR 故障在用户侧不可归因。修复:库层引入静态 CoreLog 转发器(默认 no-op,宿主接入)。置信度:确认。

**AR-022 | P2 | CLI 配置静默重置 | src/EasyCon2.CLI/ConsoleOutAdapter.cs:9**
- `ConfigErrorReported` 唯一订阅者是 GUI(App.axaml.cs:49);CLI 下配置损坏被静默换成默认值,用户不知情。修复:CLI 启动时订阅并输出 stderr。置信度:确认。

**AR-023 | P2 | LLM 健壮性 | src/EasyCon.Core/LLM/OpenAIChatClient.cs:25,31,220**
```csharp
public static event Action<string>? DebugLog;                  // 静态事件
public static event Action<int, int, string>? RequestRetrying;
_http = new HttpClient { ..., Timeout = TimeSpan.FromMinutes(5) };
```
- (a) 静态事件使订阅者被永久引用(页面反复开关泄漏),多供应商实例共享回调无法区分来源——确认;(b) 流式路径 `ResponseHeadersRead` 下 HttpClient.Timeout 贯穿整个响应体,>5 分钟长流被掐断且标为 retryable 诱发整轮重试——疑似。修复:流式请求 InfiniteTimeSpan+自管首字节超时;事件改实例。置信度:(a) 确认/(b) 疑似。

**EasyCon.Device**

**AR-024 | P2 | 并发 | src/EasyCon.Device/NintendoSwitchCmd.cs:84,101,109 + JoyStickDevice.cs:71-76**
- `Disconnect()` 把 `clientCon` 置 null 与 Loop 线程的 `WriteReport/SendSync/ResetControl` 解引用竞争 → NRE 被 Loop catch 吞掉上报 Error(「断开瞬间设备报错」);断开后旧 Loop 永久阻塞在 WaitOne(`Signal()` 因 IsConnected()==false 永不 Set)→ 每次断开泄漏一个线程池线程。修复:方法入口缓存局部引用;取消时 `_ewh.Set()`。置信度:确认。

**AR-025 | P2 | 潜伏死机制 | src/EasyCon.Device/ECKey.cs:12,18**
```csharp
public KeyStroke(ECKey key, bool up = false, int duration = 0, DateTime time = default)
public readonly DateTime Time = DateTime.Now;    // 构造参数 time 被忽略
```
- 唯一传未来时间的调用点 NintendoSwitchPriv.cs:85(duration 定时释放)因此失效,`ks.Duration > 0` 分支为不可达死代码;长按定时释放机制整体不可用。修复:`Time = time` 或删除死机制。置信度:确认。

**AR-026 | P2 | 能力模型洞 | src/EasyCon.Core/Runner/EcxVm.cs:97,113**
- `ReadLine = () => Console.ReadLine()` 与 `Beep = Console.Beep` 硬编码,绕过 IConsoleIo——GUI 下 FREAD 的 stdin 无处可读。修复:IConsoleIo 增 ReadLine/Beep。置信度:确认。

**AR-027 | P2 | 状态通知双发 | src/EasyCon.Device/Connection/TTLSerialClient.cs:40-44,110-111**
- Connected/Error 每次翻转通知两次(setter Task.Run 异步 + Loop 内同步再发),Task.Run 闭包读 `_status` 可能已被覆盖 → 事件乱序,DeviceService 重复处理。修复:只保留 setter 一处。置信度:确认。

**EasyCon.Capture**

**AR-028 | P2 | 原生并发 | src/EasyCon.Capture/FrameProducer.cs:57 + src/EasyCon2.Avalonia/Services/CaptureService.cs:155-161**
- `VideoCapture` 非线程安全:循环线程 `Read` 与服务线程 `SetProperties`(分辨率/FourCC/FPS)无同步(FrameProducer 未暴露锁),采集中途改参数可能原生层竞争崩溃。修复:属性设置投递到采集循环内执行。置信度:竞争存在确认,崩溃疑似。

**AR-029 | P2 | 全局命名空间污染 | src/EasyCon.Capture/SearchMethod.cs:3**
- 整文件无 namespace,`SearchMethod` 落在全局命名空间,污染所有引用方编译单元。修复:归入 EasyCon.Capture。置信度:确认。

**AR-030 | P2 | 异常黑盒 | src/EasyCon.Capture/OcrEngineCache.cs:58 + CustomStdioClientTransport.cs:142,149-152 + EzTesseract/Interop/NativeLoader.cs:44**
- 三处「失败完全不可见」:OCR Init 空 catch(并入 AR-007 链);MCP 读循环 `catch { /* 忽略 */ }` 使服务器崩溃对会话层不可见(叠加 AR-004);NativeLoader 多候选探测全部失败后不留任何痕迹,「文件在但依赖缺失」与「文件不存在」不可区分。修复:统一「记日志 + 可区分错误」。置信度:确认。

**AR-031 | P2 | 诊断日志四轨 | src/EasyCon2.Avalonia.Core/Services/LogService.cs + App.Commands.cs:17 + LspClientService.cs:19 + AiAgentViewModel.cs:666**
- 同一「诊断日志」职责存在 `App.LogSink`、`LspClientService.LogSink`、`AiAgentViewModel.DiagLog` 三个 `static Action<string>?` 钩子 + ILogService 接口共四条路径,App.axaml.cs:53-59 手工全部接到 `logService.AddLog`。绕过 DI、启动前/Dispose 后调用静默丢失、测试实例化互相污染静态状态。CLI 另建独立 Serilog logger。修复:收敛为单一 IDiagnosticLog 注入。置信度:确认。

**AR-032 | P2 | AI 工具失控面 | src/EasyCon2.Avalonia.Core/AiAgent/SkillExecutor.cs:148-151**
- fork 子 Agent 默认拿到**全部**工具(含 `execute_skill` 自身)且无递归深度守卫——子 Agent 可再 fork,LLM 循环嵌套无上界(每层叠一个 Task.Run)。修复:子 Agent 工具集排除 execute_skill 或限制 fork 深度。置信度:确认。

**AR-033 | P2 | 死代码+坏示范 | src/EasyCon2.Avalonia.Core/Services/FirmwareService.cs:44-51**
- FirmwareService/IFirmwareService 全仓零引用(FlashFirmwareViewModel 自建流程),唯一数据源 `ScriptService.BuildAsync` 恒抛 NotImplementedException;还演示了 `File.WriteAllBytes("temp.bin", ...)` 相对 CWD 写盘。修复:整类删除。置信度:确认。

**AR-034 | P2 | 锁不一致 | src/EasyCon2.Avalonia.Core/Mcp/McpManager.cs:85,104 vs :26-29**
- `RefreshAsync` 无锁 `ToList/Remove`,而 `Connections/GetConnection` 走 `lock(_connections)`(消费方为 AI 工具线程)——并发枚举可抛 InvalidOperationException。修复:统一持锁。置信度:确认(低概率)。

**AR-035 | P2 | 静默吞咽集群 | ConfigService.cs:84-87,97-101 + SkillExecutor.cs:110-113**
- `CheckForUpdate` 空 catch 使网络故障与「无更新」不可区分(需注释);`LoadOrCreate` 的 `catch { return new T(); }` 是第二重兜底,一旦触发**绕过 ConfigManager 的备份+事件上报链路**静默重置配置;`SkillExecutor` 把 OperationCanceledException(用户主动停止)误报为「执行超时」误导模型反思。修复:统一两级上报,删重复兜底,OCE 单独分类。置信度:确认。

**EasyCon.Vm(native)**

**AR-036 | P2 | 契约文档失真 | docs/VmSemanticContract.md:27(S-16)+ docs/EcmEcxFormat.md:68(§2.3)**
- S-16「PutF 写 BYTE 截断 &0xFF」双端都未实现(C:ecs_vm.c:1402-1406 仅 coerce BOOL;C#:EcxInterpreter.cs:1104-1108);§2.3 承诺的加载期「指令流静态抽查(槽位/Jmp 目标/索引)」不存在——而 AR-001 的修复恰恰需要它。契约文档自称「强制 checklist」,失真会诱导错误修改。修复:修文档或补实现。置信度:确认。

**AR-037 | P2 | OOM 自毁路径 | src/EasyCon.Vm/native/ecs_vm.c:181-183,711-768**
- `free(grown)` 后 `vm->objs` 悬垂;load 各表 calloc 失败时计数已置数组为 NULL,宿主按文档惯例调 `ecs_vm_free`(ecs_main.c:257)→ NULL 解引用。MCU 内存紧张时「先 OOM 后崩」。修复:先赋数组后置计数;free 前判 NULL。置信度:确认。

**AR-038 | P2 | 无界递归(恶意输入) | src/EasyCon.Vm/native/ecs_vm.c:275-305,867-889,508-547,614-639**
- 深嵌套数组/结构链使 release/sweep/tostring/加载展开的递归深度=嵌套深度;MCU 栈 4-8KB,crafted 镜像可在 `ecs_vm_load` 期打爆宿主栈。修复:加载期限嵌套深度;release 迭代化。置信度:确认(crafted);真实脚本可达性疑似。

**AR-039 | P2 | 可移植性/UB | src/EasyCon.Vm/native/ecs_vm.c:787-789**
```c
vm->funcs[f].code = (const uint32_t *)(code_base + (size_t)...code_off * 4);
```
- 11 字节函数表使代码区普遍非 4 对齐,`uint32_t*` 直接解引用在严格对齐目标(Cortex-M0 类/XIP)触发对齐异常,-O2 下兼属严格别名 UB;EcxWriter「天然对齐」注释与 EcmEcxFormat.md §1 均为假断言。修复:镜像内按 memcpy 读指令字,或加载期强制对齐并修文档。置信度:非对齐事实确认,MCU 行为疑似。

**AR-040 | P2 | 构建集成 | src/EasyCon.Vm/native/build.sh:5 + test/EasyCon.Tests/Support/CvmRunner.cs:56-58**
- build.sh 与 ci/build-vm.sh 双份维护、无工具链探测;VM 不进 slnx,Windows 开发者改坏 ecs_vm.c 无本地验证路径(测试侧 CVM 差分因 AR-006 恒 Ignore,Windows CI 也不编译)。另有:CvmRunner 先 `ReadToEnd(stdout)` 再 `ReadToEnd(stderr)`,--trace TSV 超 4KB 即经典双管道死锁,120s 超时形同虚设且无 Kill;`WaitForExit(60000)` 后读 ExitCode 可抛。修复:统一构建入口;CvmRunner 改异步双流读取。置信度:确认。

**EasyCon2.Avalonia(App)+ 相关**

**AR-041 | P2 | 上帝类 | src/EasyCon2.Avalonia/ViewModels/MainWindowViewModel.cs(1660 行,:708 亲验)**
```csharp
using var client = new HttpClient();      // VM 内直接做更新检查 HTTP
```
- 至少 12 项职责:设置防抖持久化、更新检查(内嵌 HTTP/JSON/DTO)、项目与脚本 IO 编排、日志环形缓冲+ANSI 解析、监视器编排、标签截图与模板匹配、录制、双布局管理、主题/语言、6 个子窗口入口、MCP/AI 生命周期、关窗清理。非内聚大,是职责混杂。修复:拆 UpdateCheckerService/LogPipeline/LayoutSettings/TagEditorCoordinator。置信度:确认。

**AR-042 | P2 | 调度抽象落空 | src/EasyCon2.Avalonia/ViewModels/ControllerConnectionViewModel.cs:56,109 + MainWindowViewModel.cs:363,418,550**
- 同一文件混用注入的 `IUiDispatcher` 与 `Dispatcher.UIThread.Post`;`?? SynchronousUiDispatcher.Instance` 兜底意味着宿主漏传时事件在后台线程内联执行 UI 逻辑——「生产默认=不封送」。修复:IUiDispatcher 改必选参数,统一走注入实例。置信度:确认。

**AR-043 | P2 | 重入缺陷 | src/EasyCon2.Avalonia/ViewModels/CaptureConnectionViewModel.cs:102-107**
- `ConnectCaptureSource` 缺兄弟 VM 都有的 `if (IsConnecting...) return;` 守卫,且 `Task.Run` 未 await(CS4014 已知)——连接期间可再次点击,第二次 TryConnect 会 Dispose 第一个 producer 后重开设备,部分采集卡二次打不开即卡死「连接中」。这是已知 CS4014 之外的独立缺陷。修复:补守卫 + await。置信度:确认。

**AR-044 | P2 | 仓库卫生(孤儿与死重) | test/BenchTest/Program.cs:15、src/EasyCon2、src/EzCv、src/EasyCon.Script.Jit、src/EasyCon.Server、tools/OpenCvDnnDemo/assets/、EasyCon2.UI.Common**
- BenchTest 硬编码 `D:/repositories/ecstest/main.ecs`(离开该机器必崩),Script 对它的 IVT 疑似已无必要;src/EasyCon2(旧 WinForms)、EzCv、Script.Jit(仅 bin/obj)、EasyCon.Server(空)均为残骸;OpenCvDnnDemo 跟踪 78MB yolo11m.onnx 进 git 且随构建复制;UI.Common 为 net6.0(仓库唯一非 net10)WinForms 遗物,`AvaloniaResource` 声明是 no-op(零 Avalonia 包),37MB 资源中真正被消费的仅 resx 7 条目,Amiibo 图片运行时依赖手工摆放目录(ESPConfigViewModel.cs:343 静默返回 null)。修复:删残骸目录;模型改 LFS/下载脚本;UI.Common 并入唯一消费者。置信度:确认。

**EasyCon.SDLInput / LSP / 测试**

**AR-045 | P2 | 设备状态残留 | src/EasyCon.SDLInput/SdlGamepadInputBinder.cs:32-36**
- `Stop()` 不复位设备——断开/切换控制源时最后一份报告(如按住的摇杆方向)残留在 Switch 上;对照 WinInput 版 Stop() 显式 `_ns.Reset()`。修复:Stop 中调用 Reset(注意与 VPadService 时序)。置信度:确认。

**AR-046 | P2 | IVT 依赖的私有契约 | src/EasyCon.Script/Syntax/SyntaxTree.cs:17**
```csharp
internal CompicationUnit Root { get; init; }   // 公共类型的主体载荷是 internal,类型名还拼错
```
- LSP 全部分析入口依赖 Script 的 IVT 读 Root;AST 块语句类型一半 internal 一半 public,外部 switch 无法穷尽。修复:public 只读根访问器 + 统一可见性,收回对 Lsp 的 IVT;顺带修正 CompilationUnit 拼写。置信度:确认。

**AR-047 | P2 | LSP 功能缺陷 | src/EasyCon.Lsp/Analysis/HoverProvider.cs:47 + Handlers/EcsCompletionHandler.cs:29 + EcsLanguageServer.cs:35**
- struct 悬停字段列表是全文档所有 struct 字段的**并集**(归属错误);补全声明 4 个触发符却忽略位置、任何位置返回同一 90 项全量列表(无前缀过滤,`"("` 无 signatureHelp 支撑);服务器以异常**消息文案** `"Stream closed"` 判定正常断开,TCP accept 循环无 CancellationToken。修复:按 struct 归属过滤;补全按前缀过滤或收窄能力声明;改框架停止事件。置信度:确认。

**AR-048 | P2 | 测试覆盖缺口(两处)** | test/EasyCon.Lsp.Tests(6 文件)+ test/EasyCon2.Avalonia.Core.Tests(8 文件)+ test/EasyCon.Tests/Capabilities/OcrCapabilityTests.cs:214 + PipelineUnificationTests.cs:160
- LSP:66 个测试全在 Analysis/DocumentManager 层,**协议层零覆盖**(无 stdio 往返/分帧/diagnostics 推送端到端)。Avalonia.Core.Tests:Editor/Terminal/TagEditor/ModelsConfig/AlertConfig/Threading 全部零覆盖。OCR:tessdata 探测路径含已不存在的 `net8.0` 目录与 `Depend/opencvsharp` → 恒 Ignore;ExternFfi 在 Windows 平台 Ignore——CI 只跑 windows-latest,原生链路(Tesseract/FFI)在 CI **从未真正运行**。38 个 Skip ≈ 28 个 CVM 无 cc + 2-3 个 OCR 依赖 + 4 个 LLM models.json + 平台条件。修复:内存 Stream 往返测试;tessdata 路径改 net10.0 或固定目录;EasyCon.Tests 对 Capture 的**隐式传递引用**改显式 ProjectReference。置信度:确认。

**AR-049 | P2 | 客户端死代码 | src/EasyCon2.Avalonia/Editor/Lsp/LspSemanticHighlighter.cs + LspTokenTypeMapper.cs(约 200 行)**
- 服务端未注册 SemanticTokens 能力,客户端 `RequestSemanticTokensAsync` 零调用方;编辑器只装了 Completion/Hover/Definition。死代码误导「语义高亮已接入」。修复:删除或服务端补 Handler 后接线。置信度:确认。

**EasyCon2.CLI / 生命周期**

**AR-050 | P2 | CLI 能力对齐 | src/EasyCon2.CLI/Program.cs:207-235**
- CLI 不装配 `Inference`(脚本 ML 指令静默返回 -1 无提示)、`Ocr` 仅在「有采集卡且有标签」时装配(GUI 无条件);finally 不 Dispose OcrEngineCache(原生 Tesseract 句柄)、不对设备 Reset/Disconnect。修复:补齐装配与释放链。置信度:确认。

**AR-051 | P2 | 退出清理双通道 | src/EasyCon2.Avalonia/App.axaml.cs:90-95 + MainWindowViewModel.cs:1573,1580**
- `OnMainWindowClosing` 与 `desktop.Exit` 都 Dispose controllerService/captureService(当前仅因幂等+wasRunning 保护而安全,属依赖巧合);**DeviceService 完全没有 Dispose 且不在清理清单**(串口连接态句柄留给进程退出);MCP DisposeAsync fire-and-forget,子进程可能清理未完成即退进程。修复:清理收敛单一入口,DeviceService 纳入,MCP 同步等待(带超时)。置信度:确认(幂等证据)/风险疑似。

**AR-052 | P2 | L10n 半失效 | src/EasyCon2.Avalonia/ViewModels/(约 30 处)+ Services/ControllerService.cs:55-57 + MainWindowViewModel.cs:1245 + ThemeManager.cs:10-14**
- 用户可见字符串硬编码中文散布约 30 处(ESPConfig/SwitchConnection/CaptureConnection/MainWindow/FileTree/KeyMapping),服务层也输出 UI 文案(「键盘」/「手柄: 」);en_US.axaml 已有 134 键但切语言后这些字符串不变→英文界面半中文。MainWindowViewModel.cs:1245 用**本地化快照字符串**做业务比较(`currentPath != "未选择脚本"`),zh_CN 恰好相等属一踩即碎;ThemeManager 以中文显示名做运行时键。修复:文案入 Locales;状态判断改布尔;主题键走 ThemeKeys 稳定键。置信度:确认。

### 4.4 P3(改进建议)

| 编号 | 位置 | 问题(证据已核) | 修复方向 |
|---|---|---|---|
| AR-053 | src/EasyCon.Core/EasyCon.Core.csproj:7 + Properties/AssemblyInfo.cs:3 | IVT→EasyCon.Tests 在 csproj 与 AssemblyInfo **双重声明** | 删 AssemblyInfo.cs 只留 csproj |
| AR-054 | src/EasyCon.Core(KeyExt、StringExtensions、EngineCacheOcrService、PaddleOnnxOcr、DelegateOcrService、ToolCallAccumulator 等 7 例,grep 验证) | 仅 1 个/0 个消费者却 public;93 个 public 相当比例虚胖 | 逐个降 internal,以公共 API 快照测试锁定 |
| AR-055 | src/EasyCon.Device(ECKey.cs:12、OperationRecords.cs:5,83、CommandCode.cs:3,20、NSExtentions.cs:6、JoyStickDevice.cs:44 public 可变字段 recordState) | 无外部消费却 public;`TTLSerialClient/EasyConPad` 已 internal(封装在变好) | 降 internal;recordState 改 private |
| AR-056 | src/EasyCon.Device/V1.cs:3-144 | `EasyConPad` 死类,内含与 SwitchReport.GetBytes 逐行同构的重复协议实现 + `while(!_stream.CanRead)` 忙等 | 删除 V1.cs |
| AR-057 | src/EasyCon.Device/README.md | 描述不存在的 `SerialPortClient`/`TTLv2SerialClient` 类与「心跳自动重连」 | 按当前代码重写 |
| AR-058 | src/EasyCon.Device/Connection/TTLSerialClient.cs:101-113,135 | `_inBuffer` 写加锁读不加锁的假保护(实际单线程);135 行 catch 丢弃异常对象(故障原因不可诊断) | 去冗余锁并注释线程模型;记录 ex |
| AR-059 | src/EasyCon.Device/JoyStickDevice.cs:25,31、TTLSerialClient.cs:28-32 | CTS/SerialPort 从不 Dispose;`Timeout/Connected` 死成员;客户端一次性语义无文档 | 补 Dispose 链,删死成员 |
| AR-060 | src/EasyCon.Script/Bytecode/EcsOpcode.cs:15-93 ↔ native/ecs_vm.h:19-37 | opcode/syscall 枚举双份按声明顺序隐式对齐,无机械比对(AbiContractTests 的 C 侧受 AR-006 恒跳过) | 解析 ecs_vm.h 的契约测试或代码生成 |
| AR-061 | src/EasyCon.Script/Bytecode/EcxInterpreter.cs(无深度检查)↔ ecs_vm.c:15,990;EcxWriter.cs:45 | 深递归 MCU 报 ECS_ERR_DEPTH、PC 端跑到 OOM;镜像头 max_depth 死元数据(发射端静默钳位 vs max_slots 响亮抛错不对称) | 解释器加同值上限或删字段并文档化 |
| AR-062 | src/EasyCon.Script/Bytecode/EcxInterpreter.cs:275-279,1056-1058 + Core/Runner/EcxVm.cs:135-139 | 错误码 ABI 三处手抄(含裸字面量 `4`,Describe 映射缺 4);Halt 经 `SimError(OK)` 退出也留「错误现场」;宿主委托异常炸穿 Run 不经错误码映射,违背 IScriptSession 契约注释 | 补齐错误码常量;Run 边界包裹宿主异常 |
| AR-063 | src/EasyCon.Script/Modules/ModuleCache.cs:144-159,179,189 | temp+rename 对并发读不安全(Windows 上 Move 覆盖正被读文件抛 IOException,只 catch BytecodeException);.err sidecar 写非原子 | Move 包重试或写锁 | 置信度:疑似 |
| AR-064 | src/EasyCon.Script/ICGamePad.cs:1、IIoAdapter.cs:3、Delegates.cs:1 | 程序集内寄居遗留顶层命名空间 `EasyScript`(3 文件) | 并入 EasyCon.Script 或迁出独立 SPI |
| AR-065 | src/EasyCon.Script/Bytecode/EcxWriter.cs:135 vs 147-148 | 常量池串 >65535 units 计数静默回绕(WriteUtf8 同场景却响亮抛错) | 补显式上限检查 |
| AR-066 | src/EasyCon.Script(Runtime 注释 5 处)/ IScriptEngine.cs:31 | 注释仍指「金标准 SsaEvaluator」(已不存在);`IScriptSession.Run` 对并发/重复 Run 零契约 | 注释指向 VmSemanticContract.md;声明会话执行模型 |
| AR-067 | src/EasyCon.Script(Bytecode/EcxModuleEncoder.cs:14、Runtime/SimpleStringStore.cs:9、CompilationTiming.cs:9、EcxDisassembler.cs:8、EcxLinker.cs:22) | 仅 0-1 个消费者却 public(134 public 中的代表) | 降 internal |
| AR-068 | src/EasyCon.Vm/native/ecs_main.c:234-245,184-186 | 参考宿主未装 host.rand(S-12 落空,双端 Rand 对拍盲区)、BEEP 无 trace 事件、`sigint_seen` 死代码 | 装确定性 rand 桩 + BEEP trace |
| AR-069 | src/EasyCon.Vm/native/ecs_vm.c:690-692,1636-1639 + ecs_main.c print_utf16 | double 常量按宿主内存序(大端 MCU 全错);CallN >8 参 C 拒绝/C# 放行双端分歧;UTF-16 代理对输出非法 UTF-8 | 条件字节序;双端统一 8 参上限并写入 ABI 文档 |
| AR-070 | docs/VM2.md:175 + EcsOpcode.cs:90 + EcsFormat.cs:35 | StickPv 三处均写「高16位x/低16位y」↔ 实现相反(编码器 `x\|(y<<16)`)——按文档实现的第三方宿主摇杆 x/y 对调 | 改文档 |
| AR-071 | docs/VM2.md:68-69 ↔ ecs_vm.c:1563 | 文档称 C 端加载期字符串驻留,实际无 intern(循环引用常量串反复 malloc/free) | 补 C 侧驻留或改文档 |
| AR-072 | src/EasyCon.Capture(Search.cs:31-44 静态 OCR 副本硬编码 chi_sim;ImgLabel.cs:167-170 失败把 "err!!"+ex 写进 ImgBase64 并持久化进 .IL;ImgLabel.cs:94-100 缓存 Mat 无同步 Dispose) | 第二套 OCR 引擎缓存与 OcrEngineCache 平行;错误哨兵串污染标签数据;UI 编辑线程与匹配线程可并发踩已释放 Mat | Search.cs 接入 OcrEngineCache;失败抛异常;缓存不可变快照 | 后者疑似 |
| AR-073 | src/EasyCon.Capture(ImgLabelX.cs、OCRDetect.cs、HSVColor.cs(遗留 namespace EasyCapture)、MatExtensions.Resize、OcrEngineCache.TryGet 等) | public 面约为实际消费面 3 倍,前三个为纯死代码 | 降 internal/删除 |
| AR-074 | src/EasyCon.Capture/FrameProducer.cs:104-114,82 + Capture.cs:58-97 | 循环卡死时放弃释放 VideoCapture(注释自知,句柄泄漏至进程退出且线程永不停);`_loopTask` 锁外读;设备发现靠真开 10 个 VideoCapture 探测,无 NullCapture/Mock 抽象(Linux CI 无法测图像链路) | 暴露未干净关闭状态;锁内读写;引入 ICaptureSource Mock | 部分有意取舍 |
| AR-075 | src/EasyCon2.Avalonia.Core/Services/ICaptureService.cs:22 + Capture/FrameStore.cs:82 | `FrameLease.Mat` 在 Dispose 后仍可触达无守卫(误用即原生崩溃;当前 5 处调用点纪律全部正确) | 加 `_released` 守卫或提供 Snapshot() | 设计事实 |
| AR-076 | src/EasyCon2.Avalonia.Core/AiAgent/Tools/McpToolAdapter.cs:32-34 + ToolRegistry.Register | 超长工具名截断可碰撞 + 字典覆盖语义 → 两个长名 MCP 工具静默互吞 | 截断加哈希后缀;重名记日志 |
| AR-077 | src/EasyCon2.Avalonia.Core/AiAgent/AiAgentViewModel.cs:407 | `_pendingTools` 以工具名为 key,同轮并行调用同名工具时 UI 行互踩(编排器 :384 真并行) | 以 toolCall.Id 为 key |
| AR-078 | src/EasyCon2.Avalonia.Core/Services/LogService.cs:104-111 | 清屏信号可插队进 UI 队列(「清屏→旧批」顺序颠倒已清空日志重现);Dispose 不冲刷残留批次(疑似,毫秒窗口) | 批次代数机制 | 疑似 |
| AR-079 | src/EasyCon2.Avalonia.Core/AiAgent/Tools/DefaultTools.cs:25 | 默认 AI 工具集含公网天气查询 GetWeatherTool(wttr.in),与产品域无关,疑似演示残留 | 移出默认集 |
| AR-080 | src/EasyCon2.Avalonia.Core/AiAgent/AgentOrchestrator.cs:381 | `_currentAssistant` 编排器线程写/UI 线程读,非 volatile(实践风险低) | volatile 或封送 | 疑似 |
| AR-081 | src/EasyCon2.Avalonia.Core/Editor/Lsp/LspClientService.cs:90-104,272-277 | 重连路径新旧 pipe/task 字段覆盖窗口,旧 server Task 泄漏悬挂(进程内,无进程泄漏) | 连接会话对象整体替换 | 疑似 |
| AR-082 | src/EasyCon2.Avalonia.Core/TagEditor/TagEditorViewModel.cs:83-106 | VM 藏排版决策(按字长选字号)、`ParameterComboSelectedIndex` 纯 View 联动 hack、默认标签名是游戏特定文案 | 字号归 View,文案入资源 |
| AR-083 | src/EasyCon2.Avalonia.Core/Mcp/McpServerConnection.cs:27 等 | McpServerConnection(Status 公开可写)、AgentEvent 嵌套 record、IMcpSessionFactory、SkillExecutor、CustomStdioClientTransport、KeyValueEntry、GetWeatherTool 在 App 工程 0 引用 | 降 internal(已有 IVT→Tests) |
| AR-084 | src/EasyCon2.Avalonia/Services/IControllerService.cs:17 + ControllerConnectionViewModel.cs:165 | 接口含 `SetOwnerWindow(Window)` 使其无法随 VM 下沉;消费方绕过接口引用具体类常量 `ControllerService.KeyboardSourceId`;「键盘」显示名两处各写一份 | 常量提升到接口;SetOwnerWindow 参数改 nint/object |
| AR-085 | src/EasyCon2.Avalonia/Editor/ScriptEditorControl.axaml.cs:419,428 + Views/MainWindow.axaml.cs:86-96 | code-behind 在 UI 线程同步读盘解析 ImgLabel;MainWindow 自建并持有 LspClientService 单例+折叠管理+主题切换约 230 行(超「可视化树初始化」授权,`_ = DisposeAsync()` 未等待) | 数据加载移 LspClientService 侧异步预热;LSP 生命周期提为 EditorHostService |
| AR-086 | src/EasyCon2.Avalonia/VPad/JCDrawControl.cs:20 | 覆盖层可见期每 100ms 无条件全量重绘(Render 优先级);AVLN3001 经核实无害(internal 类由代码 new,不经运行时加载器) | 加脏标记;AVLN3001 可注释抑制 |
| AR-087 | src/EasyCon2.Avalonia/ViewModels/KeyMappingViewModel.cs:48-55 | VM 内硬编码画布像素坐标(26 行×4 布局数字)并 using Avalonia.Input/SDLInput | 坐标移资源/布局模型 |
| AR-088 | src/EasyCon2.CLI/Program.cs:23,75-79,295-298,125,328,370,404 + ConsoleOutAdapter.cs:106,125 | 默认串口硬编码 COM22;`--port mock` 魔法串无文档;空 if 死分支;`LoadImgLabels` 前置代码 4 处逐字复制;run 路径日志无条件 ANSI 转义(重定向到文件含 ESC 码)且 Warn/Error 在文件里全记 Information;顶层 NS/engine/session 对所有子命令无条件构造 | 参数化默认值;删死代码;收敛辅助函数;重定向检测剥 ANSI;Severity 映射 |
| AR-089 | src/EasyCon2.CLI/ConsoleOutAdapter.cs:106,125 | `ColorfulConsole`/`AnsiColors` public(Exe 项目无下游) | internal 化 |
| AR-090 | src/EasyCon.SDLInput/SdlEventLoop.cs:50-53 + src/EasyCon2.Avalonia/Services/ControllerService.cs:45-46 | `SDL_Init` 在 try 外且返回值被忽略(原生库缺失→后台线程未处理异常崩溃);构造函数 `OpenExisting` 与事件线程 SDL_Init 竞争(疑似可自愈,依赖 SDL 未文档化行为) | init 入 try 并检查返回值;OpenExisting 延迟到首轮 poll 后 | 后者疑似 |
| AR-091 | test/EasyCon.SDLInput.Tests、WinInput.Tests | SdlEventLoop 线程模型/Detector 并发/Binder 手柄路径零测试;WinInput.Tests 只覆盖纯函数(与死项目一致) | 补 Detector 并发快照测试与 Stop 有界 Join 测试 |
| AR-092 | ci/test.bat:8 | UiTests 排除仅靠 NUnit [Explicit] 默认行为,无 CI 级 filter 兜底(现状合规) | test.bat 加 `--filter "TestCategory!=Manual"` 双保险 |
| AR-093 | docs/README.md:30,33-36,70 | 5 个文档互链指向不存在路径(src/EasyCon.VPad/README.md 等) | 修正链接 |
| AR-094 | tools/OpenCvDnnDemo/Program.cs:1-6 | 注释引用的「EzCv 库」在仓库无源码对应(仅残骸目录),防误导需注明 | 加 README 标注实验性质 |

---

## 5. 横切一致性评估

### 5.D 并发与生命周期
- **出生地/归宿登记在册的共约 40 处**,分为三档:GUI 侧(CaptureService/SDL 三件套/AiAgent 关窗)经 2026-09-26 修复后质量最高;**Device 侧是当前最弱环**(AR-002/003/024:不可取消等待、无 Join、一次性客户端无文档);MCP/AI 新链路次之(AR-004/005/034)。
- UI 封送:`IUiDispatcher` 抽象存在且 Core 层使用统一(未发现绕过),但 App 层混用(AR-042)与 `SynchronousUiDispatcher.Instance` 兜底使抽象可被静默架空。
- IDisposable:FrameStore 租约引用计数、EzTesseract 内存配对、Runner 原生句柄收口良好;缺口集中在 Device(CTS/SerialPort 不 Dispose)、CLI(OcrEngineCache/设备不释放)、App 退出双通道(AR-051)。

### 5.E 错误处理与日志
- 异常策略光谱:Script 编译错=Diagnostic 袋(优秀)→ 运行错=int 错误码(三处手抄,AR-062)→ Device 结果码枚举(一致)→ Mcp/OCR 层空 catch 集群(AR-007/030/035)。**空 catch 全仓 14 处**:6 处 Dispose 尽力而为(合理)、8 处吞失败信号(需治理)。
- 日志通道:单轨(最终都到 LogService/Serilog 文件)但入口四轨(AR-031);库层 Console/Debug 直写造成 Release/GUI 盲区(AR-021);CLI 输出分流纪律分裂(AR-088)。

### 5.F 配置与持久化
- ConfigManager 本体是模范实现(损坏备份+事件上报+原子写),订阅者分布健康;缺口在其外围:CLI 不订阅事件(AR-022)、ConfigService 第二重兜底绕过上报(AR-035)、Save 无锁(AR-018)、Amiibo 资源依赖手工摆放(AR-044)。未发现绕过 ConfigManager 直读配置文件的生产代码。

### 5.G 原生与互操作
- 四条原生边界的托管侧纪律:EzTesseract(内存契约逐项核对无泄漏)、Core Runner(句柄进程级缓存有交代理由)、SDLInput(原生仅 Start() 触碰)均好;OpenCV Mat 通过租约穿透到上层是**有意的性能设计**且 5 处消费纪律一致(AR-075 属防御缺失)。
- 原生侧:ecs_vm.c 内存安全(P0/AR-001/037/038/039)是全项目最大风险点;**构建集成完全游离于 MSBuild/CI 门控之外**(AR-006/040);三份契约文档(VmSemanticContract/EcmEcxFormat/VM2)存在 4 处与实现不符(AR-036/070/071)。
- 平台覆盖:Windows 为主,「Linux Mock 路径」不成立——Capture 无 Mock 抽象(AR-074)、OCR 测试依赖本地 tessdata(AR-048)、CVM 差分依赖 Unix cc(AR-006),三者叠加使非 Windows 与「无外设」环境几乎不可测。

### 5.H 安全与输入边界(原定 EasyCon.Server 的检查点,实际落在 MCP/LSP/CLI)
- 无网络服务面(MCP 仅出站连接 LLM/工具服务器;LSP 默认 stdio/本机命名管道,`--tcp` 是显式 opt-in 的开发模式)。脚本内容直通设备的注入面=产品本意(自动化工具),不构成额外风险。真正值得做的:`.ecx` 镜像在 MCU 侧无校验链(AR-001 是其直接后果)、MCP 工具名截断碰撞(AR-076)、GetWeatherTool 外联(AR-079)。

---

## 6. 正面观察清单(每模块至少一条)

1. **EasyCon.Script(全项目工程质量最高)**:`EcsFormat.BuildTable` 单一事实源驱动发射自检/扫描/反汇编并对漏登 opcode 抛异常(EcsFormat.cs:51-216);Merkle 式模块缓存(cacheKey=SHA256(源⊕接口哈希⊕编译器版本⊕产物指纹)+ 完整性自检 + 失败诊断 sidecar,ModuleCache.cs:9-55);Parser 按行错误恢复对 LSP 友好(Parser.cs:96-289);Fuzz 用 KnownBadSeeds 隔离名单防缺陷复发。
2. **EasyCon.Core**:脚本执行面真正面向接口(EcxVm.BuildHost 只消费能力接口);配置损坏兜底一流(Load 先备份再降级并上报,Save 原子替换);OpenAIChatClient 工程细节扎实(Retry-After+退避+抖动、流内 error 不静默、Key 不进日志)。
3. **EasyCon.Device**:测试接缝(IConnection public + CreateConnection virtual)与 AGENTS.md 声明完全一致,SDLInput.Tests 用真实出队 HID 字节做金样本断言;发送循环补了 catch-all 兜底并写明动机;ClearQueue 作为一等抽象进入接口。
4. **EasyCon.Capture**:FrameStore 无锁引用计数租约(每个 Mat 恰好释放一次,推演过竞争窗口);全部 5 个租约消费点纪律一致;TryConnect 转换期处理(先摘引用再 Dispose+监视定时器防误报)带着竞态根源注释。
5. **EzTesseract**:零依赖达成;GetUTF8Text↔DeleteText、PixDestroy 契约严格配对无泄漏;三包装类 Dispose+finalizer 双保险。
6. **EasyCon.Lsp**:复用 Script 语法树无第二份 Parser;全程序集仅 1 个 public 类型;DocumentState 不可变+ConcurrentDictionary 整体替换,读写无锁竞争。
7. **EasyCon2.Avalonia.Core**:纯度纪律真实生效(0 using Avalonia,DTO 抽象完整);关键陷阱处「为什么」级注释文化(不传 token 的原因、DisposeAsync 首个 await 前同步 Kill 的原因等);MCP 握手失败回收孤儿进程。
8. **EasyCon2.Avalonia**:App.axaml.cs 七个异常/日志源统一路由;IconRegistry/UiPreloader 的 Lazy 异常处理到位;CaptureService 并发设计是全项目最佳实践。
9. **EasyCon.SDLInput**:整表原子替换键位映射避免事件线程竞态;旧 VK 档 ResolveEffective 降级链被测试锁定;SdlGamepadDetector 锁+双检+锁外事件+读侧快照(t1 修复)质量高于仓库平均。
10. **EasyCon2.CLI**:Ctrl+C 取消链路真实贯通(System.CommandLine token → session.Run);LSP 单一入口,stdio 纪律良好;用户可见错误基本可操作。
11. **EasyCon2.UI.Common / EasyCon.WinInput / 孤儿**:无正面观察保留项(WinInput.Tests 的纯函数测试质量尚可,但项目整体建议删除)。
12. **文档**:Pipeline.md/ModuleSystem.md 是「单一事实源」式高保真文档(每个类/公式/缓存键都能对上代码,自带测试锚点索引)——应作为其他文档重写的范本;UiTests 的 PNG 崩溃根因与规避方案写进类注释,可维护性示范级。

---

## 7. 整改路线图

| 顺 | 事项 | 发现 | 影响面 | 工作量 | 性质 |
|---|---|---|---|---|---|
| 1 | **打通 CI 差分防线**:CvmRunner 工具链探测(PATH/which/Windows clang)+ CI 增加 Linux job 或 macOS 增跑 dotnet test | AR-006、AR-048(部分)、AR-092 | CI/workflows+1 测试支持类 | 小 | 可机械化 |
| 2 | **VM 加载期静态槽位校验**(顺带兑现 EcmEcxFormat §2.3 或修文档)+ OOM 路径修复 | AR-001、AR-036、AR-037 | ecs_vm.c | 中 | 需人工(语义决策) |
| 3 | **Device 重连时序修复**:Disconnect 可等待、RunLoop 可取消等待、WriteReport 局部引用 | AR-002、AR-003、AR-024 | Device 3 文件+SDLInput.Tests 回归 | 中 | 需人工(时序设计) |
| 4 | **MCP 稳定性**:stderr 排空任务 + 读循环异常上报 + ToolRegistry 加锁/延迟重注册 | AR-004、AR-005、AR-030(部分)、AR-034 | Avalonia.Core 3 文件 | 小 | 可机械化 |
| 5 | **OCR 失败可见化**:Init 失败保留原因、GetOrInit 语义、Recognize 记日志;tessdata 测试路径修正 | AR-007、AR-048(OCR 部分) | Capture/Core/Tests | 小 | 可机械化 |
| 6 | **删除死项目/死代码簇**:WinInput(+测试+GamepadApi)、UI.Common(并入消费方)、FirmwareService、V1.cs、WS_Message、ProjectManager、OcrDelegateFactory、ImgLabelExt、LspSemanticHighlighter、孤儿目录、deps/*.dll | AR-011、AR-019、AR-020、AR-033、AR-044、AR-049、AR-056 | 跨模块(多为纯删除) | 中 | 可机械化,删 UI.Common 需人工确认 resx 消费 |
| 7 | **LSP 常量表接单一事实源**(关键字+内建函数由 Script 导出) | AR-008、AR-047(部分) | Lsp+Script 小改 | 小 | 需人工(导出面设计) |
| 8 | **CLI 退出码与生命周期**:Task<int>、OCE 分类、Inference/Ocr 装配、Dispose 链、订阅 ConfigErrorReported | AR-009、AR-022、AR-050、AR-088(部分) | CLI | 小 | 可机械化 |
| 9 | **App VM 归位**:4-5 个仅差 Dispatcher 的 VM 下沉 Core,IUiDispatcher 必选化;其余登记豁免;CaptureConnection 补重入守卫 | AR-010、AR-042、AR-043 | App/Core 8 文件 | 中 | 需人工(下沉顺序) |
| 10 | **文档真实性重构**:Framework/MODULE_DESIGN 归档重写(以 Pipeline.md 为范本),GETTING_STARTED/README/Device README/VM2 StickPv/VM2 驻留/VmSemanticContract S-16 修正 | AR-012、AR-057、AR-070、AR-071、AR-036(文档侧)、AR-093 | docs | 中 | 可机械化(逐条核对) |
| 11 | **计时与取消正确性**:Stopwatch 统一、AISleep 加 token、KeyStroke.Time 修复或删除、Nanosleep rem 传址 | AR-014、AR-015、AR-016、AR-025 | Core/Device | 小 | 可机械化 |
| 12 | **库层日志收敛**:CoreLog 转发器接入宿主;诊断日志四轨合一 | AR-021、AR-031 | Core+3 宿主 | 中 | 需人工(接口设计) |
| 13 | **public 面收敛**(Core/Device/Capture/Script/Avalonia.Core 五表逐个降 internal)+ IVT 双重声明清理 + SyntaxTree.Root 公共访问器 | AR-053、AR-054、AR-055、AR-067、AR-046、AR-083、AR-089 | 全库(机械) | 大 | 可机械化,建议配 API 快照测试 |
| 14 | **契约对齐补强**:opcode 双枚举机械比对测试、错误码 ABI 单源、CallN 上限统一、max_depth 策略、对齐读取 | AR-060、AR-061、AR-062、AR-069 | Script+Vm | 中 | 需人工(ABI 决策) |
| 15 | **并发收尾**:ModuleCache 重试、ImgLabel 缓存快照、VideoCapture Set 投递、LogService 批次代数、LspClientService 会话对象、SdlGamepadInputBinder.Stop 复位、SdlEventLoop init 入 try | AR-018、AR-063、AR-072、AR-028、AR-078、AR-081、AR-045、AR-090 | 各模块 | 中 | 部分需人工判断时序 |
| 16 | **L10n 补全**:约 30 处硬编码文案入 Locales、本地化快照比较改布尔、主题键稳定化、服务层退回状态码 | AR-052 | App 8 文件 | 中 | 可机械化(逐条) |
| 17 | 其余 P3(工具名碰撞、pendingTools 键、GetWeatherTool、JCDrawControl 脏标记、KeyMapping 坐标、COM22 默认值、Mock Capture 抽象、FrameLease 守卫等) | AR-064~AR-094 余项 | 零散 | 小→中 | 按需 |

**建议节奏**:1-5 为一个修复波(全部有明确正确答案,合计约 3-5 人日);6-10 为第二波(删除与文档可并行);11-17 按维护节奏消化。P0/P1 项不修不应合入新功能到相关链路。

---

## 8. 附录

### A. 审查覆盖与方法
- Phase 2 四波共 12 个只读 Explore 子代理,覆盖:Core/Device/Script → Capture/Vm(native)/EzTesseract → Avalonia.Core/Avalonia(UI.Common+WinInput+SDLInput 合并一代理)→ Lsp/CLI/测试+tools+docs。每代理输出含 file:line 证据、置信度与正面观察。
- Phase 3 主审亲读复核:ecs_vm.c 槽位操作数、JoyStickDevice.cs 全文、NintendoSwitchPriv.cs 全文、TTLSerialClient.cs 全文、NintendoSwitchCmd.cs 全文、CustomStdioClientTransport.cs 全文、ToolRegistry.cs 全文、AiAgentViewModel(:200-274)、OcrEngineCache.cs 全文、CvmRunner.cs、ci.yml/ci-dev.yml 矩阵、CLI Program.cs(run 动作)、LSP Constants vs Lexer 关键字表、WinInput 消费者 grep、App VM namespace 计数、MainWindowViewModel HttpClient。**全部 P0/P1 条目均已亲验。**
- 任务书范围修正:EasyCon.Server(空目录)的安全检查点已转移到 MCP/LSP/CLI(§5.H);EasyCon.Vm 按「纯原生 C 项目 + 构建脚本」审查。

### B. 复核状态说明
- 标注 **[亲验]** 的 12 条(AR-001~009 及 AR-010/011 的关键证据、AR-041 的 HttpClient):主审逐行读过完整调用链。
- 其余「确认」条目:子代理以全仓 grep(消费方逐一枚举)+ 精读得出,主审抽查未发现反例,但未逐条重走调用链;「疑似」条目已注明未验证的前置条件(并发窗口、多进程、大端目标、SDL 行为等)。
- 已知豁免项(§6 of 任务书)均未上报;2026-09-26 修复抽查结论:SendOrStopAsync 局部 CTS、McpSessionFactory 孤儿回收、SdlGamepadDetector 锁+快照、ScriptEditorControl IsConnecting 守卫等修复到位,唯一残留为 MCP 工具重注册路径未纳入同类守卫(已并入 AR-005)。

### C. 构建与测试基线(2026-09-26)
- `dotnet build EasyCon2.slnx`:0 错误,259 警告(CS4014×1、CS0067×3、AVLN5001 Watermark 过时约 250、AVLN3001×1)。
- `dotnet test EasyCon2.slnx -c Release`:EasyCon.Tests 799 通过/38 跳过,Lsp.Tests 66,SDLInput.Tests 18,Avalonia.Core.Tests 59,UiTests 24,WinInput.Tests 11 —— 合计 **977 通过 / 0 失败 / 38 跳过**。
- 38 个 Skip 构成:约 28 个 CVM 差分(无 cc,AR-006)、2-3 个 OCR 外部依赖(AR-048)、4 个 LLM models.json、2-4 个平台条件。
