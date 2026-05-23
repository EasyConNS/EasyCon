using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace EasyCon.Script.Binding;

/// <summary>
/// 内置函数 Callable 实现。每个静态方法对应一个内置函数的执行逻辑。
/// </summary>
internal static class BuiltinCallable
{
    public static Value ImplWait(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var ms = args[0].AsInt();
        CustomDelay.Delay(ms, token);
        return Value.Void;
    }

    public static Value ImplPrint(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var s = args[0].AsString();
        var output = s.EndsWith('\\') ? s[..^1] : s;
        ctx.Output?.Print(output, !ctx.CancelLineBreak);
        ctx.CancelLineBreak = s.EndsWith('\\');
        return Value.Void;
    }

    public static Value ImplAlert(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var s = args[0].AsString();
        var output = s.EndsWith('\\') ? s[..^1] : s;
        ctx.Output?.Alert(output);
        return Value.Void;
    }

    public static Value ImplRand(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var max = args[0].AsInt();
        max = max < 0 ? 0 : max;
        return ctx.Rand.Next(max);
    }

    public static Value ImplTimestamp(ReadOnlySpan<Value> _, IEvalContext ctx, CancellationToken token)
    {
        return ctx.Timestamp;
    }

    public static Value ImplAmiibo(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var index = args[0].AsInt();
        if (index > 9) return Value.Void;
        ctx.GamePad?.ChangeAmiibo((uint)index);
        return Value.Void;
    }

    public static Value ImplBeep(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var freq = args[0].AsInt();
        if (freq < 37 || freq > 32767) throw new Exception("BEEP参数freq范围不正确(37~32767)");
        Console.Beep(freq, args[1].AsInt());
        return Value.Void;
    }

    public static Value ImplOcr(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var result = ctx.Ocr?.Invoke(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsString()) ?? "OCR NOT SUPPORT";
        return Value.FromString(result);
    }

    public static Value ImplJq(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var json = JsonDocument.Parse(args[0].AsString()).RootElement;
        var query = args[1].AsString();

        var current = json;
        var i = 0;
        while (i < query.Length)
        {
            if (query[i] == '.')
            {
                i++;
                var start = i;
                while (i < query.Length && query[i] != '.' && query[i] != '[')
                    i++;
                current = current.GetProperty(query[start..i]);
            }
            else if (query[i] == '[')
            {
                i++;
                var start = i;
                while (query[i] != ']')
                    i++;
                current = current[int.Parse(query[start..i])];
                i++;
            }
            else
            {
                i++;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.Number => current.GetInt32(),
            JsonValueKind.String => Value.FromString(current.GetString()!),
            JsonValueKind.True => Value.FromBool(true),
            JsonValueKind.False => Value.FromBool(false),
            JsonValueKind.Array => Value.CreateArray(ScriptType.Int,
                current.EnumerateArray().Select(e => (Value)e.GetInt32())),
            _ => Value.FromString(current.GetRawText()),
        };
    }

    public static Value ImplConvertInt(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return args[0].ToInt();
    }

    public static Value ImplConvertString(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return args[0].ToString();
    }

    public static Value ImplLength(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return args[0].Length;
    }

    public static Value ImplAppend(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return args[0].Append(args[1]);
    }

    public static Value ImplStrEncode(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var array = args[0].AsArray();
        var bytes = new byte[array.Count];
        for (int i = 0; i < array.Count; i++)
            bytes[i] = array[i].AsByte();
        return args[1].AsString() switch
        {
            "unicode" => Encoding.Unicode.GetString(bytes),
            "utf8" or _ => Encoding.UTF8.GetString(bytes)
        };
    }

    public static Value ImplEnv(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        return Environment.GetEnvironmentVariable(args[0].AsString()) ?? "";
    }

    public static Value ImplPixel(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var frame = ctx.Frame?.Invoke(-1,-1,-1,-1);
        if (frame == null) throw new Exception("无法获取帧数据");
        dynamic img = frame;
        int x = args[0].AsInt();
        int y = args[1].AsInt();
        int width = (int)img.Width;
        int height = (int)img.Height;
        if (x < 0 || x >= width || y < 0 || y >= height)
            throw new Exception($"像素坐标越界 ({x}, {y})，帧大小 {width}x{height}");
        dynamic pixel = img[x, y];
        var instance = new EcsStruct(BuiltinFunctions.PixelStructDef);
        instance.SetField(instance.Definition.Fields[0], (int)(byte)pixel.R);
        instance.SetField(instance.Definition.Fields[1], (int)(byte)pixel.G);
        instance.SetField(instance.Definition.Fields[2], (int)(byte)pixel.B);
        instance.SetField(instance.Definition.Fields[3], (int)(byte)pixel.A);
        return Value.FromStruct(instance);
    }

    public static Value ImplFrame(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var base64 = ctx.Frame?.Invoke(-1, -1, -1, -1);
        return Value.FromString(base64 ?? "!!ERR!!");
    }
    public static Value ImplFrameROI(ReadOnlySpan<Value> args, IEvalContext ctx, CancellationToken token)
    {
        var base64 = ctx.Frame?.Invoke(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt());
        return Value.FromString(base64 ?? "!!ERR!!");
    }
    /// <summary>
    /// 获取所有内置函数及其对应的 Callable。
    /// Timestamp 需要额外的 timestampFactory 闭包参数。
    /// </summary>
    public static ImmutableArray<(FunctionSymbol Symbol, ICallable Callable)> GetAll()
    {
        return
        [
            (BuiltinFunctions.Wait, new DelegateCallable(ImplWait)),
            (BuiltinFunctions.Print, new DelegateCallable(ImplPrint)),
            (BuiltinFunctions.Alert, new DelegateCallable(ImplAlert)),
            (BuiltinFunctions.Rand, new DelegateCallable(ImplRand)),
            (BuiltinFunctions.Timestamp, new DelegateCallable(ImplTimestamp)),
            (BuiltinFunctions.Amiibo, new DelegateCallable(ImplAmiibo)),
            (BuiltinFunctions.Beep, new DelegateCallable(ImplBeep)),
            (BuiltinFunctions.Ocr, new DelegateCallable(ImplOcr)),
            (BuiltinFunctions.Env, new DelegateCallable(ImplEnv)),
            (BuiltinFunctions.Length, new DelegateCallable(ImplLength)),
            (BuiltinFunctions.Append, new DelegateCallable(ImplAppend)),
            (BuiltinFunctions.StrEncode, new DelegateCallable(ImplStrEncode)),
            (BuiltinFunctions.IntConvert, new DelegateCallable(ImplConvertInt)),
            (BuiltinFunctions.StrConvert, new DelegateCallable(ImplConvertString)),
            (BuiltinFunctions.Jq, new DelegateCallable(ImplJq)),
            (BuiltinFunctions.Pixel, new DelegateCallable(ImplPixel)),
            (BuiltinFunctions.Frame, new DelegateCallable(ImplFrame)),
            (BuiltinFunctions.FrameRoi, new DelegateCallable(ImplFrameROI)),
        ];
    }
}