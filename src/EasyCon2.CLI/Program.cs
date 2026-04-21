// See https://aka.ms/new-console-template for more information
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Syntax;
using EasyDevice;
using EasyScript;
using OpenCvSharp;
using System.Collections.Immutable;
using System.CommandLine;
using System.Text;

// 设置控制台输出编码为 UTF-8，解决中文乱码问题
Console.OutputEncoding = Encoding.UTF8;

bool isFormatCommand = args.Length > 0 && args[0] == "format";

string defaultCOMPort = "COM22";

NintendoSwitch NS = new();
EasyRunner runner = new();

if (!isFormatCommand)
{
    Console.WriteLine("------------------------------------------");
    Console.WriteLine("----    EasyCon CLI Runner v0.0.1     ----");
    Console.WriteLine("---- 仅供内部测试，不代表最终发布表现 ----");
    Console.WriteLine("------------------------------------------");
}

var rootCommand = new RootCommand("EasyCon CLI Runner");

var runScriptCommand = new Command("run", "运行伊机控脚本");
var runLuaCommand = new Command("runlua", "运行lua脚本");
var portDevCommand = new Command("port", "单片机端口功能");
var videoCommand = new Command("video", "视频采集设备功能");
var formatCommand = new Command("format", "格式化脚本");

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

    // 输出接口
    var outdap = new ConsoleOutAdapter();

    Console.WriteLine($"准备执行脚本...  环境信息=>采集设备：{vId}[{refs}]  单片机端口：{COM}");

    var scriptBasePath = Path.GetDirectoryName(file) ?? "";
    scriptBasePath = Path.GetFullPath(scriptBasePath);
    Console.WriteLine("准备加载搜图标签...");
    Console.WriteLine($"标签（脚本）路径：{scriptBasePath}({ECCore.ImgDir}所在目录)");
    var (label, total, repeat) = ECCore.LoadImgLabels(scriptBasePath, AppDomain.CurrentDomain.BaseDirectory);
    Console.WriteLine($"已加载标签：{label.Count()}/{total}, {(repeat > 0 ? $"重复标签：{repeat}" : "")}");


    OpenCVCapture? cvcap = null;
    outdap.Log("正在解析脚本...");
    var diag = runner.Load(file, [.. label.Select(il => il.name)]);

    if (diag.HasErrors())
    {
        HashSet<int> errlist = [];
        foreach (var d in diag)
        {
            if (!errlist.Add(d.Location.StartLine))
                continue;
            outdap.Error($"!!编译失败!!{d.Message}: 行{d.Location.StartLine + 1}");
        }
        return;
    }

    bool isMock = COM.Equals("mock", StringComparison.OrdinalIgnoreCase);

    if (runner.HasKeyAction)
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

    if (runner.NeedILLoad)
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
    var externalGetters = label.ToDictionary(il => il.name, il => (Func<int>)(() =>
    {
        if (cvcap == null) throw new Exception("采集卡初始化异常");
        il.Search(cvcap!.GetMatFrame(), out var md);
        return (int)md;
    }));
    outdap.Info($"==>开始执行脚本：{file}");

    try
    {
        ICGamePad pad = isMock ? new MockGamePad() : new GamePadAdapter(NS);
        runner.Run(outdap, pad, externalGetters, cancellationToken);
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

runLuaCommand.Arguments.Add(scriptOption);
runLuaCommand.SetAction(async (parseResult, cancellationToken) =>
{

    string file = parseResult.GetValue(scriptOption)!;
    LuaRunner.ExecuteFile(file);
});

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

    var diag = runner.Load(file, [.. label.Select(il => il.name)]);

    if (diag.HasErrors())
    {
        foreach (var d in diag)
        {
            Console.Error.WriteLine($"line {d.Location.StartLine + 1}: {d.Message}");
        }
        return 1;
    }

    var formatted = runner.ToCode();

    if (!string.IsNullOrEmpty(outputFile))
    {
        File.WriteAllText(outputFile, formatted);
    }
    Console.Write(formatted);

    return 0;
});

rootCommand.Subcommands.Add(runScriptCommand);
rootCommand.Subcommands.Add(runLuaCommand);
rootCommand.Subcommands.Add(portDevCommand);
rootCommand.Subcommands.Add(videoCommand);
rootCommand.Subcommands.Add(formatCommand);
return rootCommand.Parse(args).Invoke();