using EasyCon.Core.Capabilities;
using EasyCon.Script;

namespace EasyCon.Core.Script;

/// <summary>
/// 脚本引擎装配选项（P6 收敛面）：能力集 + 编译选项 + 脚本参数。
/// </summary>
public sealed class ScriptHostOptions
{
    public CapabilitySet Capabilities { get; init; } = new();

    public CompileOptions Compile { get; init; } = new() { UseDiskCache = false };

    /// <summary>ARG(i) 脚本参数。</summary>
    public string[] Args { get; init; } = [];
}

/// <summary>
/// 脚本引擎（P6 收敛面，取代 IRunner/EasyRunner 旧界面）：
/// 源码/文件 → <see cref="IScriptSession"/>（编译产物 + 执行）。
/// </summary>
public interface IScriptEngine
{
    /// <summary>编译源码并返回会话（诊断经 <see cref="IScriptSession.Info"/> 查看）。</summary>
    IScriptSession FromSource(string code, ScriptHostOptions options);

    /// <summary>编译脚本文件并返回会话；相对 FFI 库路径在运行时以主脚本目录为基准。</summary>
    IScriptSession LoadFile(string path, ScriptHostOptions options);
}

/// <summary>一次编译的会话：编译产物查看 + 执行。</summary>
public interface IScriptSession
{
    /// <summary>编译结果（诊断 / EcxImage / NativeSymbols / KeyAction / NeedIL / Timing）。</summary>
    CompileResult Info { get; }

    /// <summary>
    /// 执行镜像到完成/取消/出错（错误同 EcxVm 语义抛 ScriptException/OperationCanceledException）。
    /// <paramref name="capabilities"/> 为 null 时用编译时的 <see cref="ScriptHostOptions.Capabilities"/>
    /// （GUI 等宿主的能力在运行期才就绪，可在 Run 时传入）。
    /// </summary>
    void Run(CancellationToken cancellationToken, CapabilitySet? capabilities = null);
}