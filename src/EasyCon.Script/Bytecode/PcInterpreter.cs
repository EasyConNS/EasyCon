using EasyCon.Script.Symbols;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// PC 扩展指令执行入口：仅消费 Desktop 内存计划，保留 int 操作数和动态帧。
/// 不依赖 InstructionCodec，不生成或加载扩展 ECX；MCU 格式与 C VM 无须修改。
/// </summary>
public static class PcInterpreter
{
    public static int Run(EcxImage program, EcxHost host, out Value result, CancellationToken token = default)
        => Run(program, host, out _, out _, out _, out result, token);

    public static int Run(EcxImage program, EcxHost host, out TaggedValue rawResult, out int errorFunc,
        out int errorPc, out Value result, CancellationToken token = default)
    {
        if (program.Target != ScriptTarget.Desktop)
        {
            rawResult = default;
            result = Value.Void;
            errorFunc = -1;
            errorPc = -1;
            return EcxInterpreter.ERR_IMAGE;
        }
        return InstructionExecutor.Run(program, host, out rawResult, out errorFunc, out errorPc, out result, token);
    }
}