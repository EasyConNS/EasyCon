using EasyCon.Core.Capabilities;
using EasyCon.Script;
using EasyCon.Script.Binding;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Core.Runner;

/// <summary>
/// 桌面 ECX 执行桥（统一链路的执行端，docs/Pipeline.md）：
/// 把能力集（<see cref="CapabilitySet"/>，输入/控制台/环境/文件/采集/视觉/OCR/推理）
/// 装配成 <see cref="EcxHost"/> 委托与评估桥，用 <see cref="EcxInterpreter"/> 执行
/// <see cref="EcxImage"/>，并把 VM 错误码映射回 <see cref="ScriptException"/>。
/// 事件语义与金标准 SsaEvaluator 逐项对齐（对拍测试锁定）。
/// </summary>
public static class EcxVm
{
    /// <summary>
    /// 能力集执行入口：由 <see cref="CapabilitySet"/> 生成 host 委托与评估桥
    /// （能力模型纯属 PC 装配层，VM 核不感知）。执行到完成/取消/出错，
    /// 返回入口函数返回值（顶层 RETURN）。
    /// </summary>
    /// <param name="nativeSymbols">全项目 extern 符号（FFI 原生按名分发的签名来源）。</param>
    public static Value Run(EcxImage image, CapabilitySet capabilities, CancellationToken token,
        string[]? args = null, ImmutableArray<FunctionSymbol> nativeSymbols = default)
    {
        var startTicks = DateTime.Now.Ticks;
        var rand = new Random();

        var externMap = new Dictionary<(string Lib, string Name), FunctionSymbol>();
        foreach (var sym in nativeSymbols)
        {
            if (string.Equals(sym.LibraryName, "internal", StringComparison.Ordinal))
                continue;
            externMap.TryAdd((sym.LibraryName, sym.ExternalName), sym);
        }
        NativeLoader? loader = null;

        var host = BuildHost(capabilities, token, args, startTicks, rand);
        host.Native = (name, argv, ctx) =>
        {
            // 内建/采集洞：复用金标准的 BuiltinCallable 实现（文件 IO/ENCODE/JQ 等）
            if (BuiltinMap.TryGetValue(name, out var callable))
                return ctx.FromValue(callable.Invoke(ToValues(argv, ctx), capabilities, token));

            // FFI：按名分发（BytecodeEncoder.BuildNativeName → "库!导出名"）
            var bang = name.IndexOf('!');
            if (bang > 0
                && externMap.TryGetValue((name[..bang], name[(bang + 1)..]), out var sym))
            {
                loader ??= new NativeLoader();
                var fn = loader.ResolveFunction(sym);
                return ctx.FromValue(fn.Invoke(ToValues(argv, ctx), capabilities, token));
            }
            return null;   // → ERR_NOSUCHNATIVE
        };

        try
        {
            int code = EcxInterpreter.Run(image, host, out _, out var errorFunc, out var errorPc, out var result, token);
            if (code == EcxInterpreter.OK)
                return result;
            if (code == EcxInterpreter.CANCELLED)
                throw new OperationCanceledException(token);
            // Address 语义 = 源码行（1 基，经行号表映射；无表回退 0），pc 保留在消息里供诊断
            var faultFunc = errorFunc >= 0 && errorFunc < image.Functions.Count ? image.Functions[errorFunc] : null;
            var sourceLine = faultFunc?.LineAt(errorPc) ?? 0;
            throw new ScriptException(
                $"!!运行出错!!{Describe(code)}（函数 {image.FunctionName(errorFunc)}，指令 {errorPc}）", sourceLine);
        }
        finally
        {
            DesktopFileSystem.Instance.CloseAllFiles();
        }
    }

    static EcxHost BuildHost(CapabilitySet capabilities, CancellationToken token, string[]? args,
        long startTicks, Random rand)
    {
        IPadInput? input = capabilities.Input;
        IVisionService? vision = capabilities.Vision;
        IConsoleIo? console = capabilities.Console;
        return new EcxHost
        {
            Args = capabilities.Environment?.Args ?? args ?? [],
            AppDir = capabilities.Environment?.AppDir ?? AppDomain.CurrentDomain.BaseDirectory,   // __APP__
            WaitMs = ms => CustomDelay.Delay(ms, token),
            TimeMs = () => (int)((DateTime.Now.Ticks - startTicks) / 10_000),
            Rand = max => rand.Next(max),
            ImgLabel = name => vision != null
                ? vision.MatchLabel(name)
                : throw new Exception("图像标签匹配器未初始化"),
            Print = (s, newline) => console?.Print(s, newline),
            Alert = s => console?.Alert(s),
            ReadLine = () => Console.ReadLine() ?? "",   // v1 FREAD stdin 走控制台（ImplFRead）
            Key = (k, d) => input?.ClickButtons((GamePadKey)k, d, token),
            KeyState = (k, d) =>
            {
                if (d != 0)
                    input?.PressButtons((GamePadKey)k);
                else
                    input?.ReleaseButtons((GamePadKey)k);
            },
            StickSet = (s, x, y) => input?.SetStick(s == 1 ? GamePadKey.RS : GamePadKey.LS, (byte)x, (byte)y),
            StickClick = (s, x, y, d) => input?.ClickStick(s == 1 ? GamePadKey.RS : GamePadKey.LS, (byte)x, (byte)y, d, token),
            Amiibo = i => input?.ChangeAmiibo((uint)i),
            Beep = (f, d) =>
            {
                if (f is < 37 or > 32767)
                    throw new Exception("BEEP参数freq范围不正确(37~32767)");
                Console.Beep(f, d);
            },
        };
    }

    static readonly Dictionary<string, ICallable> BuiltinMap =
        BuiltinCallable.GetAll().Concat(BuiltinCallable.GetCaptureHoleCallables())
            .ToDictionary(t => t.Symbol.Name, t => (ICallable)t.Callable, StringComparer.Ordinal);

    static Value[] ToValues(TaggedValue[] argv, EcxNativeContext ctx)
    {
        var args = new Value[argv.Length];
        for (int i = 0; i < argv.Length; i++)
            args[i] = ctx.ToValue(argv[i]);
        return args;
    }

    static string Describe(int code) => code switch
    {
        EcxInterpreter.ERR_DIVZERO => "整数除零",
        EcxInterpreter.ERR_INDEX => "下标越界",
        EcxInterpreter.ERR_TYPE => "类型错误",
        EcxInterpreter.ERR_NOSUCHNATIVE => "原生函数未实现",
        _ => $"虚拟机错误码 {code}",
    };
}