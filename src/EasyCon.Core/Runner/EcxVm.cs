using EasyCon.Script;
using EasyCon.Script.Binding;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Core.Runner;

/// <summary>
/// 桌面 ECX 执行桥（统一链路的执行端，docs/Pipeline.md）：
/// 把设备宿主（IO/手柄/OCR/采集/标签）与文件 IO/FFI 原生装配到 <see cref="EcxHost"/>，
/// 用 <see cref="EcxInterpreter"/> 执行 <see cref="EcxImage"/>，并把 VM 错误码映射回
/// <see cref="ScriptException"/>。事件语义与金标准 SsaEvaluator 逐项对齐（对拍测试锁定）。
/// </summary>
public static class EcxVm
{
    /// <summary>执行镜像到完成/取消/出错；返回入口函数返回值（顶层 RETURN）。</summary>
    /// <param name="nativeSymbols">全项目 extern 符号（FFI 原生按名分发的签名来源）。</param>
    public static Value Run(EcxImage image, IIoAdapter? ioAdapter, ICGamePad? pad,
        OcrDelegate? ocr, OcrInitDelegate? ocrInit, Func<int> ocrConf,
        FrameDelegate? frameProvider, RoiDelegate? roiProvider, LabelMatchDelegate? labelMatch,
        CancellationToken token, string[]? args = null,
        ImmutableArray<FunctionSymbol> nativeSymbols = default)
    {
        var startTicks = DateTime.Now.Ticks;
        var rand = new Random();
        var evalCtx = new HostEvalContext(ioAdapter, pad, ocr, ocrInit, ocrConf,
            frameProvider, roiProvider, labelMatch, rand, args ?? []);

        var externMap = new Dictionary<(string Lib, string Name), FunctionSymbol>();
        foreach (var sym in nativeSymbols)
        {
            if (string.Equals(sym.LibraryName, "internal", StringComparison.Ordinal))
                continue;
            externMap.TryAdd((sym.LibraryName, sym.ExternalName), sym);
        }
        NativeLoader? loader = null;

        var host = new EcxHost
        {
            Args = args ?? [],
            AppDir = AppDomain.CurrentDomain.BaseDirectory,   // __APP__
            WaitMs = ms => CustomDelay.Delay(ms, token),
            TimeMs = () => (int)((DateTime.Now.Ticks - startTicks) / 10_000),
            Rand = max => rand.Next(max),
            ImgLabel = name => labelMatch != null
                ? labelMatch(name)
                : throw new Exception("图像标签匹配器未初始化"),
            Print = (s, newline) => ioAdapter?.Print(s, newline),
            Alert = s => ioAdapter?.Alert(s),
            ReadLine = () => Console.ReadLine() ?? "",   // v1 FREAD stdin 走控制台（ImplFRead）
            Key = (k, d) => pad?.ClickButtons((GamePadKey)k, d, token),
            KeyState = (k, d) =>
            {
                if (d != 0)
                    pad?.PressButtons((GamePadKey)k);
                else
                    pad?.ReleaseButtons((GamePadKey)k);
            },
            StickSet = (s, x, y) => pad?.SetStick(s == 1 ? GamePadKey.RS : GamePadKey.LS, (byte)x, (byte)y),
            StickClick = (s, x, y, d) => pad?.ClickStick(s == 1 ? GamePadKey.RS : GamePadKey.LS, (byte)x, (byte)y, d, token),
            Amiibo = i => pad?.ChangeAmiibo((uint)i),
            Beep = (f, d) =>
            {
                if (f is < 37 or > 32767)
                    throw new Exception("BEEP参数freq范围不正确(37~32767)");
                Console.Beep(f, d);
            },
        };

        host.Native = (name, argv, ctx) =>
        {
            // 内建/采集洞：复用金标准的 BuiltinCallable 实现（文件 IO/ENCODE/JQ 等）
            if (BuiltinMap.TryGetValue(name, out var callable))
                return ctx.FromValue(callable.Invoke(ToValues(argv, ctx), evalCtx, token));

            // FFI：按名分发（BytecodeEncoder.BuildNativeName → "库!导出名"）
            var bang = name.IndexOf('!');
            if (bang > 0
                && externMap.TryGetValue((name[..bang], name[(bang + 1)..]), out var sym))
            {
                loader ??= new NativeLoader();
                var fn = loader.ResolveFunction(sym);
                return ctx.FromValue(fn.Invoke(ToValues(argv, ctx), evalCtx, token));
            }
            return null;   // → ERR_NOSUCHNATIVE
        };

        int code;
        try
        {
            code = EcxInterpreter.Run(image, host, out _, out var errorFunc, out var errorPc, out var result, token);
            if (code == EcxInterpreter.OK)
                return result;
            if (code == EcxInterpreter.CANCELLED)
                throw new OperationCanceledException(token);
            throw new ScriptException(
                $"!!运行出错!!{Describe(code)}（函数 {image.FunctionName(errorFunc)}，指令 {errorPc}）", errorPc);
        }
        finally
        {
            BuiltinCallable.CloseAllFiles();
        }
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

    /// <summary>BuiltinCallable 所需的 IEvalContext 适配（宿主委托直通）。</summary>
    sealed class HostEvalContext(
        IIoAdapter? ioAdapter, ICGamePad? pad, OcrDelegate? ocr, OcrInitDelegate? ocrInit,
        Func<int> ocrConf, FrameDelegate? frame, RoiDelegate? roi, LabelMatchDelegate? labelMatch,
        Random rand, string[] args) : IEvalContext
    {
        public ICGamePad? GamePad => pad;
        public IIoAdapter? IoAdapter => ioAdapter;
        public OcrDelegate? Ocr => ocr;
        public OcrInitDelegate? OcrInit => ocrInit;
        public Func<int> OcrConf => ocrConf;
        public FrameDelegate? Frame => frame;
        public RoiDelegate? Roi => roi;
        public LabelMatchDelegate? LabelMatch => labelMatch;
        public Random Rand => rand;
        public int Timestamp => (int)((DateTime.Now.Ticks - _startTicks) / 10_000);
        public bool CancelLineBreak { get; set; }
        public string[] Args => args;

        readonly long _startTicks = DateTime.Now.Ticks;

        public Value EvaluateFunctionBody(FunctionSymbol function)
            => throw new InvalidOperationException("宿主上下文不支持函数体执行");
    }
}