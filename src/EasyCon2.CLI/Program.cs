// See https://aka.ms/new-console-template for more information
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Config;
using EasyCon.Core.Hosting;
using EasyCon.Core.Maa;
using EasyCon.Core.Notifications;
using EasyCon.Core.Runner;
using EasyCon.Lsp;
using EasyCon.Script;
using EasyCon.Script.Ssa;
using EasyCon.Script.Syntax;
using EasyCon2.CLI;
using EasyDevice;
using EasyScript;
using OpenCvSharp;
using Serilog;
using System.Collections.Immutable;
using System.CommandLine;
using System.Text;

// 设置控制台输出编码为 UTF-8，解决中文乱码问题
Console.OutputEncoding = Encoding.UTF8;

bool isFormatCommand = args.Length > 0 && args[0] == "format";
bool isLspCommand = args.Length > 0 && args[0] == "lsp";
bool isMcpCommand = args.Length > 0 && args[0] == "--mcp";
bool isLabelCommand = args.Length > 0 && args[0] == "labels";
bool isMaaCommand = args.Length > 0 && args[0] == "maa";

string defaultCOMPort = "COM22";

using MaaHostSession maaHost = new();
IControllerSession NS = maaHost.Controller;
EasyCon.Core.Script.IScriptEngine engine = new EasyCon.Core.Script.EasyScriptEngine();
EasyCon.Core.Script.IScriptSession? session = null;

// 配置损坏必须让用户知道：CLI 无 GUI 弹窗，备份/重置事件直通 stderr
EasyCon.Core.Config.ConfigManager.ConfigErrorReported += (path, msg) =>
    Console.Error.WriteLine($"[配置] {msg}");
// 库层诊断转发（CoreLog 默认仅 Debug.WriteLine，Release 下会丢失）
EasyCon.Core.Logging.CoreLog.Sink = msg => Console.Error.WriteLine(msg);

if (!isFormatCommand && !isLspCommand && !isMcpCommand && !isLabelCommand && !isMaaCommand)
{
    Console.WriteLine("------------------------------------------");
    Console.WriteLine("----    EasyCon CLI Runner v0.0.1     ----");
    Console.WriteLine("---- 仅供内部测试，不代表最终发布表现 ----");
    Console.WriteLine("------------------------------------------");
}

var rootCommand = new RootCommand("EasyCon CLI Runner");

var runScriptCommand = new Command("run", "运行伊机控脚本");
var portDevCommand = new Command("port", "单片机端口功能");
var videoCommand = new Command("video", "视频采集设备功能");
var formatCommand = new Command("format", "格式化脚本");
var irCommand = new Command("ir", "打印 SSA IR（中间表示）");
var modulesCommand = new Command("modules", "输出模块项目的独立编译清单（依赖图/缓存/导出，ModuleSystem.md M7）");
var compileCommand = new Command("compile", "脚本编译为 ECX 镜像（独立编译管线；可与 ecs-vm run 组成全链路）");

#region 命令行参数解析
var scriptOption = new Argument<string>("file")
{
    Description = "要执行的脚本"
};
scriptOption.Validators.Add(result =>
{
    if (!File.Exists(result.GetValueOrDefault<string>()))
    {
        result.AddError("脚本文件不存在");
    }
});

var deviceIdOption = new Option<int>("--device", "-d")
{
    Description = "视频采集卡序号",
    DefaultValueFactory = _ => 0
};
var deviceTypeOption = new Option<VideoCaptureAPIs>("--videotype", "-vt")
{
    Description = "采集卡类型",
    DefaultValueFactory = _ => VideoCaptureAPIs.ANY
};
var verboseOption = new Option<bool>("--verbose")
{
    Description = "显示更多输出（调试专用）",
    DefaultValueFactory = _ => false
};
var portOption = new Option<string>("--port", "-p")
{
    Description = "联机设备端口；传 mock 使用虚拟单片机（无硬件试跑）",
    DefaultValueFactory = _ => defaultCOMPort
};

deviceIdOption.Validators.Add(result =>
{
    if (result.GetValueOrDefault<int>() < 0)
    {
        result.AddError("采集卡序号不能是负数");
    }
});

runScriptCommand.Arguments.Add(scriptOption);
runScriptCommand.Options.Add(deviceIdOption);
runScriptCommand.Options.Add(deviceTypeOption);
runScriptCommand.Options.Add(portOption);
runScriptCommand.Options.Add(verboseOption);
var profilesOption = new Option<string?>("--profiles") { Description = "GUI/CLI 共用的 JSON 配置（controller_profile 与 capture_profile）；显式命令行参数只覆盖对应一侧" };
runScriptCommand.Options.Add(profilesOption);
var compatibilityOption = new Option<LegacyCompatibilityProfile>("--compatibility")
{
    Description = "旧标签整数评分契约：LegacyGui 截断，LegacyCli 向上取整",
    DefaultValueFactory = _ => LegacyCompatibilityProfile.LegacyCli,
};
runScriptCommand.Options.Add(compatibilityOption);
var ocrProfileOption = new Option<string?>("--ocr-profile") { Description = "显式 Maa OCR 模型与旧参数映射 JSON；不隐式替换未验收的旧 OCR 配置" };
runScriptCommand.Options.Add(ocrProfileOption);
#endregion

runScriptCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string file = parseResult.GetValue(scriptOption)!;
    var vId = parseResult.GetValue(deviceIdOption);
    var refs = parseResult.GetValue(deviceTypeOption);
    string COM = parseResult.GetValue(portOption) ?? defaultCOMPort;
    bool verbose = parseResult.GetValue(verboseOption);
    string? profilesPath = parseResult.GetValue(profilesOption);
    HostProfiles? profiles = profilesPath == null ? null : HostProfiles.Load(profilesPath).WithOverrides(
        port: parseResult.GetResult(portOption)?.Implicit == false ? COM : null,
        captureIndex: parseResult.GetResult(deviceIdOption)?.Implicit == false ? vId : null,
        backend: parseResult.GetResult(deviceTypeOption)?.Implicit == false ? refs.ToString() : null);
    if (profiles?.Controller != null)
    {
        if (profiles.Controller.Transport != "Serial") throw new InvalidDataException("仅支持 Serial 手柄配置");
        COM = profiles.Controller.Endpoint ?? "";
    }

    // 输出接口（同时写入滚动日志文件）。using 声明确保早退路径也会落盘。
    var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
    Directory.CreateDirectory(logDir);
    using var fileLogger = new LoggerConfiguration()
        .WriteTo.File(
            Path.Combine(logDir, "easycon-.log"),
            rollingInterval: RollingInterval.Day,
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            retainedFileCountLimit: null,
            shared: false,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}")
        .CreateLogger();
    var outdap = new ConsoleOutAdapter { FileLogger = fileLogger };

    Console.WriteLine($"准备执行脚本...  环境信息=>采集设备：{vId}[{refs}]  单片机端口：{COM}");

    var scriptBasePath = Path.GetDirectoryName(file) ?? "";
    scriptBasePath = Path.GetFullPath(scriptBasePath);
    Console.WriteLine("准备加载搜图标签...");
    Console.WriteLine($"标签（脚本）路径：{scriptBasePath}({ECCore.ImgDir}所在目录)");
    LabelDiscovery labels = LegacyLabelCatalog.FromParents(scriptBasePath, AppDomain.CurrentDomain.BaseDirectory);
    Console.WriteLine($"已加载标签：{labels.Selected.Count}/{labels.TotalFiles}");


    outdap.Log("正在解析脚本...");
    session = engine.LoadFile(file, new EasyCon.Core.Script.ScriptHostOptions
    {
        Compile = ScriptCompileProfiles.Interactive(labels.Selected.Select(label => label.Name)),
    });
    Console.WriteLine(session.Info.Timing?.ToReport());
    var diag = session.Info.Diagnostics;
    if (diag.HasErrors())
    {
        HashSet<int> errlist = [];
        foreach (var d in diag)
        {
            if (!errlist.Add(d.Location.StartLine))
                continue;
            outdap.Error($"!!编译失败!!{d.Message}: 行{d.Location.StartLine + 1} 在({d.FileName})");
        }
        return 1;
    }

    LegacyCompatibilityProfile compatibility = parseResult.GetValue(compatibilityOption);
    LabelGeneration? generation = null;
    IReadOnlyList<string> requiredLabels = ScriptHardwareRequirements.ReferencedLabels(session.Info);
    if (requiredLabels.Count != 0)
    {
        string? ocrPath = parseResult.GetValue(ocrProfileOption) ?? Environment.GetEnvironmentVariable("EASYCON_MAA_OCR_PROFILE");
        LabelConversionResult conversion = MaaLabelConverter.Build(labels, MaaLabelConverter.CacheRoot(scriptBasePath), compatibility,
            ocrProfile: string.IsNullOrWhiteSpace(ocrPath) ? null : MaaOcrProfile.Load(ocrPath));
        foreach (LabelDiagnostic diagnostic in conversion.Report.Diagnostics)
            outdap.Log($"标签 {diagnostic.Code}: {diagnostic.Label}: {diagnostic.Message}");
        generation = conversion.Generation ?? throw new InvalidDataException("标签资源转换未发布");
        generation.Preflight(requiredLabels);
    }
    bool isMock = COM.Equals("mock", StringComparison.OrdinalIgnoreCase);

    if (session.Info.KeyAction)
    {
        if (isMock)
        {
            outdap.Info("使用虚拟单片机(Mock)模式");
        }
        else
        {
            outdap.Log("准备连接单片机...");
            NS.Log += (message) =>
            {
                if (verbose)
                    outdap.Print($"NS LOG >> {message}");
            };
            NS.BytesSent += (port, bytes) =>
            {
                if (verbose)
                    outdap.Print($"{port} >> {string.Join(" ", bytes.Select(b => b.ToString("X2")))}");
            };
            NS.BytesReceived += (port, bytes) =>
            {
                if (verbose)
                    outdap.Print($"{port} << {string.Join(" ", bytes.Select(b => b.ToString("X2")))}");
            };
            if (NS.TryConnect(COM) != ControllerConnectResult.Success)
            {
                outdap.Error("单片机连接失败！！");
                return 1;
            }
            outdap.Info("单片机连接成功.");
        }
    }

    bool requiresCapture = ScriptHardwareRequirements.RequiresLiveCapture(session.Info);
    if (requiresCapture)
    {
        outdap.Log("准备打开采集源...");
        bool opened = profiles?.Capture is { } captureProfile
            ? captureProfile.Source != null
                ? maaHost.Frames.Connect(captureProfile.Source, captureProfile.Normalization)
                : captureProfile.Index is { } index
                    ? maaHost.Frames.Connect(index, profiles.CaptureBackend(), captureProfile.Normalization)
                    : throw new InvalidDataException("采集配置没有可用来源；请显式选择，不会回退 0 号源")
            : maaHost.Frames.Connect(vId, refs);
        if (!opened)
        {
            outdap.Error("采集源打开失败！！");
            return 1;
        }
        outdap.Info($"采集源已连接: {maaHost.Frames.SourceId} ({maaHost.Frames.Backend})");
    }
    using AlertService alertService = new(
        resultLogger: message => outdap.Print(message),
        imageProvider: () =>
        {
            using FrameLease? lease = maaHost.Frames.AcquireLatest();
            return NotificationImage.FromFrame(lease?.Mat);
        });
    outdap.AlertService = alertService;

    // 能力装配：唯一装配点（ScriptHostAssembler）。CLI 只提供原料，
    // OCR/推理/宿主环境/文件能力的默认值与释放统一由租约承担。


    // 已有图像的 ROI 始终可用，只有 FRAME 与标签匹配借用实时采集。
    FrameDelegate? frameDelegate = null;
    RoiDelegate roiDelegate = MatExtensions.CropBase64;
    if (maaHost.Frames.CaptureConnected)
    {
        frameDelegate = FrameDelegateFactory.CreateFrame(() => maaHost.Frames.AcquireLatest());
    }

    outdap.Info($"==>开始执行脚本：{file}\n");

    try
    {
        maaHost.Runs.Run("ecs", Path.GetFullPath(file), !isMock && session.Info.KeyAction, cancellationToken, scope =>
        {
            using MaaVisionService? vision = generation == null ? null : new(generation,
                () => scope.AcquireFrame(maaHost.Frames.AcquireLatest), scope.Token);
            using CapabilityLease capabilityLease = ScriptHostAssembler.Assemble(new ScriptHostContext
            {
                PadInput = session.Info.KeyAction ? isMock ? new PadInputAdapter(new MockGamePad()) : scope.CreatePad() : null,
                Console = new EasyCon.Core.Capabilities.ConsoleIoAdapter(outdap),
                Frame = frameDelegate == null ? null : FrameDelegateFactory.CreateFrame(() => scope.AcquireFrame(maaHost.Frames.AcquireLatest)),
                Roi = roiDelegate,
                VisionService = vision,
                MaaOcrProfilePath = parseResult.GetValue(ocrProfileOption),
                CancellationToken = scope.Token,
                AppDir = AppDomain.CurrentDomain.BaseDirectory,
            });
            session.Run(scope.Token, capabilityLease.Capabilities);
        }, requiresCapture: requiresCapture, compatibilityProfile: compatibility.ToString(), resourceGeneration: generation?.Id);
        outdap.Info("脚本运行完成");
    }
    catch (OperationCanceledException)
    {
        outdap.Warn("脚本已终止");
        return maaHost.Runs.LastResult?.ErrorCode == "UserStop" ? 130 : 1;
    }
    catch (ScriptException ex)
    {
        string lineInfo = ex.Address > 0 ? $": 行{ex.Address}" : "";
        outdap.Warn($"!!运行出错!!{ex.Message}{lineInfo}");
        return 1;
    }
    catch (Exception exx)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(exx.StackTrace);
        outdap.Error($"!!意外错误!!{exx.Message}");
        return 2;
    }
    finally
    {
        if (!isMock)
        {
            try { NS.Disconnect(); } catch { /* 断开失败交由进程退出兜底 */ }
        }
        try
        {
            using CancellationTokenSource notificationTimeout = new(TimeSpan.FromSeconds(60));
            await alertService.FlushAsync(notificationTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            outdap.Warn("等待通知发送超时，剩余通知将在退出时取消，请在 QQ 中核对。");
        }
        finally
        {
            maaHost.Frames.Disconnect();
        }
    }
    return 0;
});

#region 端口功能
var portListOption = new Option<bool>("--list", "-l")
{
    Description = "列出所有端口"
};
portDevCommand.Options.Add(portListOption);

portDevCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var listport = parseResult.GetValue(portListOption);
    ECCore.GetDeviceNames().ToList().ForEach(name =>
    {
        Console.WriteLine(name);
    });
});

portDevCommand.Validators.Add(result =>
{
    if (!result.GetValue(portListOption))
    {
        result.AddError("请使用 --list 参数列出可用端口");
    }
});
#endregion

#region 视频设备功能
var videoListOption = new Option<bool>("--list", "-l")
{
    Description = "列出所有可用的视频采集设备"
};
videoCommand.Options.Add(videoListOption);

videoCommand.SetAction(async (parseResult, cancellationToken) =>
{
    // Validator 已保证 --list 必填，恒为 true
    CaptureSourceCatalog.Enumerate(VideoCaptureAPIs.ANY).ToList().ForEach(dev =>
    {
        Console.WriteLine($"[{dev.Index}] {dev.DisplayKey} (id={dev.Id}, temporary={!dev.Persistent})");
    });
});

videoCommand.Validators.Add(result =>
{
    if (!result.GetValue(videoListOption))
    {
        result.AddError("请使用 --list 参数列出可用设备");
    }
});
#endregion


var formatOutputOption = new Option<string>("-o", "输出文件");

formatCommand.Arguments.Add(scriptOption);
formatCommand.Options.Add(formatOutputOption);
formatCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string file = parseResult.GetValue(scriptOption)!;
    string? outputFile = parseResult.GetValue(formatOutputOption);

    var scriptBasePath = Path.GetDirectoryName(file) ?? "";
    scriptBasePath = Path.GetFullPath(scriptBasePath);
    var (label, total, repeat) = ECCore.LoadImgLabels(scriptBasePath, AppDomain.CurrentDomain.BaseDirectory);

    session = engine.LoadFile(file, new EasyCon.Core.Script.ScriptHostOptions
    {
        Compile = ScriptCompileProfiles.Interactive(label.Select(il => il.name)),
        Capabilities = new EasyCon.Core.Capabilities.CapabilitySet(),
    });
    var diag = session.Info.Diagnostics;

    if (diag.HasErrors())
    {
        foreach (var d in diag)
        {
            Console.Error.WriteLine($"line {d.Location.StartLine + 1}: {d.Message}");
        }
        return 1;
    }

    var formatted = session.Info.FormatCode();

    if (!string.IsNullOrEmpty(outputFile))
    {
        File.WriteAllText(outputFile, formatted);
    }
    Console.Write(formatted);

    return 0;
});

var irOptimizeOption = new Option<bool>("--raw")
{
    Description = "输出优化前的原始 SSA IR"
};
irCommand.Arguments.Add(scriptOption);
irCommand.Options.Add(irOptimizeOption);
irCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string file = parseResult.GetValue(scriptOption)!;
    bool raw = parseResult.GetValue(irOptimizeOption);

    var scriptBasePath = Path.GetDirectoryName(file) ?? "";
    scriptBasePath = Path.GetFullPath(scriptBasePath);
    var (label, total, repeat) = ECCore.LoadImgLabels(scriptBasePath, AppDomain.CurrentDomain.BaseDirectory);

    session = engine.LoadFile(file, new EasyCon.Core.Script.ScriptHostOptions
    {
        Compile = ScriptCompileProfiles.Interactive(label.Select(il => il.name)),
        Capabilities = new EasyCon.Core.Capabilities.CapabilitySet(),
    });
    var diag = session.Info.Diagnostics;

    if (diag.HasErrors())
    {
        foreach (var d in diag)
        {
            Console.Error.WriteLine($"line {d.Location.StartLine + 1}: {d.Message}");
        }
        return 1;
    }

    Console.Write(DumpIr(engine, session, file, raw));
    return 0;
});

static string DumpIr(EasyCon.Core.Script.IScriptEngine engine, EasyCon.Core.Script.IScriptSession session,
    string file, bool beforeOptimize)
{
    if (!beforeOptimize)
    {
        var prog = session.Info.Program;
        if (prog == null)
            return string.Join("\n", session.Info.Diagnostics.Where(d => d.IsError).Select(d => $"error: {d.Message}"));
        return SsaPrinter.Dump(prog);
    }

    // 优化前：关闭优化重新编译（同一脚本文件与标签扩展名）
    var (label, _, _) = ECCore.LoadImgLabels(
        Path.GetFullPath(Path.GetDirectoryName(file) ?? ""), AppDomain.CurrentDomain.BaseDirectory);
    var rerun = engine.LoadFile(file, new EasyCon.Core.Script.ScriptHostOptions
    {
        Compile = ScriptCompileProfiles.Interactive(label.Select(il => il.name), optimize: false),
    });
    return rerun.Info.Program != null
        ? SsaPrinter.Dump(rerun.Info.Program)
        : string.Join("\n", rerun.Info.Diagnostics.Where(d => d.IsError).Select(d => $"error: {d.Message}"));
}

rootCommand.Subcommands.Add(runScriptCommand);
rootCommand.Subcommands.Add(portDevCommand);
rootCommand.Subcommands.Add(videoCommand);
rootCommand.Subcommands.Add(formatCommand);
rootCommand.Subcommands.Add(irCommand);

modulesCommand.Arguments.Add(scriptOption);
modulesCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string file = parseResult.GetValue(scriptOption)!;
    // MCU 分发档：8 位槽位 + obj/ 缓存；同时加载识图标签作为外部变量，
    // 否则含 @标签 的脚本在此命令下无法通过绑定（docs/McuBytecodeDelivery.md）。
    var (label, _, _) = ECCore.LoadImgLabels(
        Path.GetDirectoryName(Path.GetFullPath(file)) ?? "", AppDomain.CurrentDomain.BaseDirectory);
    var project = EasyCon.Script.Modules.ProjectCompiler.CompileProject(
        file, ScriptCompileProfiles.Distributable(label.Select(il => il.name)));

    Console.WriteLine($"独立编译：{(project.Success ? "成功" : "失败")}  缓存命中 {project.CacheHits} / 未命中 {project.CacheMisses} / 错误重放 {project.ErrorHits} / GC 清理 {project.GarbageCollected}");
    Console.WriteLine();
    Console.WriteLine($"{"#",4}  {"模块",-12} {"函数",4} {"导出",4} {"导入",4} {"init",-5} 槽位");
    for (int i = 0; i < project.Artifacts.Count; i++)
    {
        var a = project.Artifacts[i];
        Console.WriteLine($"{i,4}  {a.Name,-12} {a.Functions.Count,4} {a.Exports.Count,4} {a.Imports.Count,4} {(a.HasInit ? "有" : "-"),-5} {a.Functions.Max(f => f.NSlots)}");
    }
    Console.WriteLine();
    Console.WriteLine($"入口 = {(project.Image != null ? project.Image.Functions[project.Image.Entry].Name : "-")}");
    foreach (var w in project.Warnings)
        Console.WriteLine($"警告: {w}");
    foreach (var d in project.Diagnostics)
        Console.Error.WriteLine($"错误: {d}");
    return project.Success ? 0 : 1;
});

rootCommand.Subcommands.Add(modulesCommand);

compileCommand.Arguments.Add(scriptOption);
var outOption = new Option<string>("--out", "-o")
{
    Description = "输出 .ecx 路径（缺省 = 脚本同名 .ecx）",
};
var stripOption = new Option<bool>("--no-strip")
{
    Description = "保留调试段（SEC_LINES/DEBUG_NAMES）；缺省省略（MCU 发布形态）",
};
var diagFormatOption = new Option<string>("--diagnostic-format")
{
    Description = "诊断输出格式：text（缺省）| json（结构化错误码/位置，CI 消费）",
};
compileCommand.Options.Add(stripOption);
compileCommand.Options.Add(outOption);
compileCommand.Options.Add(diagFormatOption);
compileCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string file = parseResult.GetValue(scriptOption)!;
    bool strip = !parseResult.GetValue(stripOption);
    string outPath = parseResult.GetValue(outOption) ?? Path.ChangeExtension(file, ".ecx");
    bool jsonDiag = parseResult.GetValue(diagFormatOption) == "json";

    // MCU 分发档：obj/ 缓存 + 可分发 .ecx（单流化后全部产物同形态，容量由烧录前预检判定）。
    var (label, _, _) = ECCore.LoadImgLabels(
        Path.GetDirectoryName(Path.GetFullPath(file)) ?? "", AppDomain.CurrentDomain.BaseDirectory);
    var project = EasyCon.Script.Modules.ProjectCompiler.CompileProject(
        file, ScriptCompileProfiles.Distributable(label.Select(il => il.name)));
    if (jsonDiag)
    {
        Console.WriteLine(EasyCon.Script.DiagnosticFormat.ToJson(project.Diagnostics));
        if (!project.Success)
            return 1;
    }
    else
    {
        foreach (var w in project.Warnings)
            Console.WriteLine($"警告: {w}");
        if (!project.Success)
        {
            foreach (var d in project.Diagnostics)
                Console.Error.WriteLine($"错误: {d}");
            return 1;
        }
    }
    File.WriteAllBytes(outPath, EasyCon.Script.Bytecode.EcsContainer.WriteImage(project.Image!, stripDebug: strip));
    Console.WriteLine($"已生成 {outPath}（{new FileInfo(outPath).Length} B，{project.Image!.Functions.Count} 函数{(strip ? "，stripDebug" : "，含调试段")}，模块: {string.Join(" → ", project.Artifacts.Select(a => a.Name))}）");
    Console.WriteLine($"执行: ecs-vm run {outPath}");
    return 0;
});

rootCommand.Subcommands.Add(compileCommand);

var lspCommand = new Command("lsp", "启动 ECS 语言服务端");
var stdioOption = new Option<bool>("--stdio")
{
    Description = "使用 stdio 通信（默认）"
};
var tcpOption = new Option<string?>("--tcp")
{
    Description = "使用 TCP 通信，格式: [host:]port"
};
lspCommand.Options.Add(stdioOption);
lspCommand.Options.Add(tcpOption);
lspCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var useStdio = parseResult.GetValue(stdioOption);
    var tcp = parseResult.GetValue(tcpOption);
    if (useStdio && tcp != null)
    {
        Console.Error.WriteLine("错误: --stdio 和 --tcp 不能同时指定");
        return 1;
    }
    if (tcp != null)
    {
        var parts = tcp.Split(':');
        var portStr = parts.Length == 2 ? parts[1] : parts[0];
        var host = parts.Length == 2 ? parts[0] : "127.0.0.1";
        if (!int.TryParse(portStr, out var port) || port is < 1 or > 65535)
        {
            Console.Error.WriteLine($"错误: 无效的端口号 '{portStr}'，范围 1-65535");
            return 1;
        }
        await EcsLanguageServer.RunTcpAsync(host, port);
    }
    else
    {
        await EcsLanguageServer.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
    }
    return 0;
});
rootCommand.Subcommands.Add(lspCommand);

rootCommand.Subcommands.Add(AgentCommand.Create());
rootCommand.Subcommands.Add(FlowCommand.Create(maaHost));
rootCommand.Subcommands.Add(MaaHostCommand.Create());
rootCommand.Subcommands.Add(LabelsCommand.Create());
rootCommand.Subcommands.Add(ServeCommand.Create());

// --mcp：stdio 模式的 MCP Server（任意外部 agent 经标准 MCP 协议驱动工作区工具）
if (isMcpCommand)
{
    var mcpRegistry = new EasyCon.Core.LLM.Agent.Tools.ToolRegistry();
    EasyCon.Core.LLM.Agent.Tools.WorkspaceFileTools.RegisterAll(
        mcpRegistry, new EasyCon.Core.LLM.Agent.Tools.WorkspaceRoot(() => Directory.GetCurrentDirectory()));
    var mcpSkills = new EasyCon.Core.LLM.Skills.SkillRegistry();
    foreach (var skill in EasyCon.Core.LLM.Agent.Skills.BundledSkills.CreateAll())
        mcpSkills.Register(skill);
    EasyCon.Core.LLM.Skills.SkillLoader.LoadToRegistry(
        mcpSkills, EasyCon.Core.LLM.Skills.SkillLoader.GetSearchPaths(Directory.GetCurrentDirectory()).ToList());
    mcpRegistry.Register(new EasyCon.Core.LLM.Agent.Tools.ListSkillsTool(mcpSkills));
    mcpRegistry.Register(new EasyCon.Core.LLM.Agent.Tools.ReadSkillTool(mcpSkills));
    // Flow 服务同源工具（设备查询/连接、编排图运行）——外部 agent 与前端画布能力对等
    var flowState = new EasyCon.Core.Flow.FlowServiceState();
    EasyCon.Core.Flow.FlowServiceTools.RegisterAll(mcpRegistry, flowState);
    var allowDangerous = args.Contains("--allow-dangerous");
    Console.Error.WriteLine("[mcp] EasyCon MCP Server (stdio) 已启动，工具目录: " + Directory.GetCurrentDirectory()
        + (allowDangerous ? "（已授权危险工具）" : "（危险工具默认不导出，加 --allow-dangerous 开启）"));
    try
    {
        await EasyCon.Core.LLM.Agent.Mcp.McpServerHost.RunStdioAsync(
            mcpRegistry, "easycon-cli", "1.0", CancellationToken.None, allowDangerous);
    }
    finally
    {
        flowState.Dispose();
    }
    return 0;
}

return rootCommand.Parse(args).Invoke();