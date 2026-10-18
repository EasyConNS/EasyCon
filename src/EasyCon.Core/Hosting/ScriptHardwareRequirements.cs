using EasyCon.Script;
using EasyCon.Script.Binding;
using EasyCon.Script.Bytecode;

namespace EasyCon.Core.Hosting;

/// <summary>宿主从链接后保留的调用判断实时采集需求；NeedIL 还包含已有图片的 ROI/OCR。</summary>
public static class ScriptHardwareRequirements
{
    public static IReadOnlyList<string> ReferencedLabels(CompileResult compilation)
        => compilation.Image is not { } image ? [] : image.Functions
            .SelectMany(function => function.Instructions)
            .Where(instruction => instruction.Op == EcsOpcode.Img)
            .Select(instruction => image.Consts[instruction.B].Str ?? throw new InvalidDataException("标签名常量不是字符串"))
            .Distinct(StringComparer.Ordinal).ToArray();

    public static bool RequiresLiveCapture(CompileResult? compilation)
    {
        if (compilation?.Image is not { } image) return false;
        return image.Functions.Any(function => function.Instructions.Any(instruction =>
            instruction.Op == EcsOpcode.Img ||
            instruction.Op == EcsOpcode.CallN && instruction.Ext < (uint)image.Natives.Count &&
            (image.Natives[(int)instruction.Ext].Name == BuiltinFunctions.CaptureHole.Name ||
                image.Natives[(int)instruction.Ext].Name == BuiltinFunctions.TesseractHole.Name)));
    }
}