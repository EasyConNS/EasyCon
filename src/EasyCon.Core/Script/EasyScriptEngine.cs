using EasyCon.Core.Capabilities;
using EasyCon.Core.Runner;
using EasyCon.Script;

namespace EasyCon.Core.Script;

/// <summary>
/// <see cref="IScriptEngine"/> 默认实现：Compilation 模块管线编译 → EcxVm 能力装配执行。
/// 编译选项经 <see cref="ScriptHostOptions.Compile"/> 传入（桌面约定 UseDiskCache=false，可覆写）。
/// </summary>
public sealed class EasyScriptEngine : IScriptEngine
{
    public IScriptSession FromSource(string code, ScriptHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(Compilation.CompileSource(code, options.Compile), options);
    }

    public IScriptSession LoadFile(string path, ScriptHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(Compilation.CompileFile(path, options.Compile), options);
    }

    sealed class Session(CompileResult result, ScriptHostOptions options) : IScriptSession
    {
        public CompileResult Info => result;

        public void Run(CancellationToken cancellationToken, CapabilitySet? capabilities = null)
        {
            if (result.Image is not { } image)
                return;
            EcxVm.Run(image, capabilities ?? options.Capabilities, cancellationToken, options.Args, result.NativeSymbols);
        }
    }
}