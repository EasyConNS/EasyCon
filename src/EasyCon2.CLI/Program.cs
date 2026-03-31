// See https://aka.ms/new-console-template for more information
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Script;
using EasyCon.Script.Parsing;
using EasyCon.Script.Runner;
using EasyDevice;
using OpenCvSharp;
using System.CommandLine;

string defaultCOMPort = "COM22";

OpenCVCapture cvcap = new();
NintendoSwitch NS = new();
EasyRunner runner = new();

Console.WriteLine("------------------------------------------");
Console.WriteLine("----    EasyCon CLI Runner v0.0.1     ----");
Console.WriteLine("---- 仅供内部测试，不代表最终发布表现 ----");
Console.WriteLine("------------------------------------------");

var rootCommand = new RootCommand("EasyCon CLI Runner");

var runScriptCommand = new Command("run", "运行伊机控脚本");

#region 命令行参数解析
var scriptOption = new Argument<string>("file")
{
    Description = "要执行的脚本"
};
scriptOption.Validators.Add(result =>
{
    if(!File.Exists(result.GetValueOrDefault<string>()))
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
    string COM = parseResult.GetValue(portOption)?? defaultCOMPort;
    bool verbose = parseResult.GetValue(verboseOption);

    // 输出接口
    var outdap = new ConsoleOutAdapter();

    Console.WriteLine($"准备执行脚本...  环境信息=>采集设备：{vId}[{refs}]  单片机端口：{COM}");

    var scriptBasePath = Path.GetDirectoryName(file);
    scriptBasePath = Path.GetFullPath(scriptBasePath);
    Console.WriteLine("准备加载搜图标签...");
    Console.WriteLine($"标签（脚本）路径：{scriptBasePath}({ECCore.ImgDir}所在目录)");
    var (label, total, repeat) = ECCore.LoadImgLabels(scriptBasePath, AppDomain.CurrentDomain.BaseDirectory);
    Console.WriteLine($"已加载标签：{label.Count()}/{total}, {(repeat > 0 ? $"重复标签：{repeat}" : "")}");

    outdap.Log("正在解析脚本...");
    try
    {
        runner.Load(file, label.Select(il => new ExternalVariable(il.name, () =>
        {
            il.Search(cvcap.GetMatFrame(), out var md);
            return (int)md;
        }))
        );
    }
    catch(ParseException ex)
    {
        outdap.Error($"!!编译失败!!{ex.Message}: 行{ex.Index + 1}");
        return;
    }

    if (runner.HasKeyAction)
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

    if(runner.NeedILLoad)
    {
        outdap.Log("准备打开采集卡...");
        if (!cvcap.Open(vId, (int)refs))
        {
            outdap.Error("采集卡打开失败！！");
            return;
        }
        outdap.Info("采集卡打开成功.");
    }

    outdap.Info($"==>开始执行脚本：{file}");
    try
    {
        runner.Run(outdap, new GamePadAdapter(NS), cancellationToken);
        outdap.Info("脚本运行完成");
    }
    catch (ScriptException ex)
    {
        outdap.Warn($"!!运行出错!!{ex.Message}: 行{ex.Address}");
    }
    catch (Exception exx)
    {
        Console.WriteLine();
        Console.WriteLine(exx.StackTrace);
        outdap.Error($"!!意外错误!!{exx.Message}");
    }
});

rootCommand.Subcommands.Add(runScriptCommand);
return rootCommand.Parse(args).Invoke();

