// See https://aka.ms/new-console-template for more information
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Runner;
using EasyCon.Lsp;
using EasyCon.Script;
using EasyCon.Script.Ssa;
using EasyCon.Script.Syntax;
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

string defaultCOMPort = "COM22";

NintendoSwitch NS = new();
EasyCon.Core.Script.IScriptEngine engine = new EasyCon.Core.Script.EasyScriptEngine();
EasyCon.Core.Script.IScriptSession? session = null;

if (!isFormatCommand && !isLspCommand)
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
    Description = "联机设备端口",
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
#endregion

runScriptCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string file = parseResult.GetValue(scriptOption)!;
    var vId = parseResult.GetValue(deviceIdOption);
    var refs = parseResult.GetValue(deviceTypeOption);
    string COM = parseResult.GetValue(portOption) ?? defaultCOMPort;
    bool verbose = parseResult.GetValue(verboseOption);

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
    var (label, total, repeat) = ECCore.LoadImgLabels(scriptBasePath, AppDomain.CurrentDomain.BaseDirectory);
    Console.WriteLine($"已加载标签：{label.Count()}/{total}, {(repeat > 0 ? $"重复标签：{repeat}" : "")}");


    OpenCVCapture? cvcap = null;
    outdap.Log("正在解析脚本...");
    session = engine.LoadFile(file, new EasyCon.Core.Script.ScriptHostOptions
    {
        Compile = new CompileOptions { ExtVars = [.. label.Select(il => il.name)], UseDiskCache = false },
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
        return;
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
            if (NS.TryConnect(COM) != NintendoSwitch.ConnectResult.Success)
            {
                outdap.Error("单片机连接失败！！");
                return;
            }
            outdap.Info("单片机连接成功.");
        }
    }

    if (session.Info.NeedIL)
    {
        cvcap = new();
        outdap.Log("准备打开采集卡...");
        if (!cvcap.Open(vId, (int)refs))
        {
            outdap.Error("采集卡打开失败！！");
            return;
        }
        outdap.Info("采集卡打开成功.");

        // 设置采集卡分辨率为1080p
        cvcap.SetProperties(1920, 1080);
    }

    FrameProducer? producer = null;
    if (cvcap != null)
    {
        producer = new FrameProducer(cvcap);
        producer.Start();
    }

    // 能力装配（P6）：帧/ROI/标签/OCR/推理经服务接口注入
    var capabilities = new EasyCon.Core.Capabilities.CapabilitySet
    {
        Console = new EasyCon.Core.Capabilities.ConsoleIoAdapter(outdap),
    };

    if (cvcap != null && label.Count() > 0)
    {
        var labelDict = label.ToDictionary(il => il.name);

        var frameDelegate = FrameDelegateFactory.CreateFrame(() => producer!.Store.AcquireLatest());
        capabilities.Capture = new EasyCon.Core.Capabilities.DelegateCaptureSource(frameDelegate);

        LabelMatchDelegate labelMatchDelegate = lblName =>
        {
            if (!labelDict.TryGetValue(lblName, out var il)) return 0;
            using var lease = producer!.Store.AcquireLatest();
            if (lease == null || lease.Mat.Empty()) return 0;
            il.Search(lease.Mat, out var md, AppDomain.CurrentDomain.BaseDirectory + "Tessdata");
            return (int)Math.Ceiling(md);
        };
        capabilities.Vision = new EasyCon.Core.Capabilities.DelegateVisionService(
            MatExtensions.CropBase64, labelMatchDelegate);

        capabilities.Ocr = new EasyCon.Core.Capabilities.TesseractOcrService(
            new EasyCon.Capture.OcrEngineCache
            {
                DefaultDataPath = AppDomain.CurrentDomain.BaseDirectory + "Tessdata"
            });
    }
    outdap.Info($"==>开始执行脚本：{file}\n");

    try
    {
        ICGamePad pad = isMock ? new MockGamePad() : new GamePadAdapter(NS);
        capabilities.Input = new EasyCon.Core.Capabilities.PadInputAdapter(pad);
        session.Run(cancellationToken, capabilities);
        outdap.Info("脚本运行完成");
    }
    catch (ScriptException ex)
    {
        outdap.Warn($"!!运行出错!!{ex.Message}: 行{ex.Address}");
    }
    catch (Exception exx)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(exx.StackTrace);
        outdap.Error($"!!意外错误!!{exx.Message}");
    }
    finally
    {
        producer?.Dispose();
    }
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
    var listDevices = parseResult.GetValue(videoListOption);
    if (!listDevices)
    {
    }

    ECCapture.GetCaptureCamera().ToList().ForEach(dev =>
    {
        Console.WriteLine($"[{dev.index}] {dev.name}");
        // Console.WriteLine($"  [{index}] {name}");
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
        Compile = new CompileOptions { ExtVars = [.. label.Select(il => il.name)], UseDiskCache = false },
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
        Compile = new CompileOptions { ExtVars = [.. label.Select(il => il.name)], UseDiskCache = false },
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
        Compile = new CompileOptions
        {
            ExtVars = [.. label.Select(il => il.name)],
            Optimize = false,
            UseDiskCache = false,
        },
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
    var project = EasyCon.Script.Modules.ProjectCompiler.CompileProject(file);

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
compileCommand.Options.Add(outOption);
compileCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string file = parseResult.GetValue(scriptOption)!;
    string outPath = parseResult.GetValue(outOption) ?? Path.ChangeExtension(file, ".ecx");

    var project = EasyCon.Script.Modules.ProjectCompiler.CompileProject(file);
    foreach (var w in project.Warnings)
        Console.WriteLine($"警告: {w}");
    if (!project.Success)
    {
        foreach (var d in project.Diagnostics)
            Console.Error.WriteLine($"错误: {d}");
        return 1;
    }
    File.WriteAllBytes(outPath, EasyCon.Script.Bytecode.EcxWriter.Write(project.Image!, stripDebug: false));
    Console.WriteLine($"已生成 {outPath}（{new FileInfo(outPath).Length} B，{project.Image!.Functions.Count} 函数，模块: {string.Join(" → ", project.Artifacts.Select(a => a.Name))}）");
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
        return;
    }
    if (tcp != null)
    {
        var parts = tcp.Split(':');
        var portStr = parts.Length == 2 ? parts[1] : parts[0];
        var host = parts.Length == 2 ? parts[0] : "127.0.0.1";
        if (!int.TryParse(portStr, out var port) || port is < 1 or > 65535)
        {
            Console.Error.WriteLine($"错误: 无效的端口号 '{portStr}'，范围 1-65535");
            return;
        }
        await EcsLanguageServer.RunTcpAsync(host, port);
    }
    else
    {
        await EcsLanguageServer.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
    }
});
rootCommand.Subcommands.Add(lspCommand);

return rootCommand.Parse(args).Invoke();