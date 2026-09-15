namespace EasyCon.Core.Capabilities;

/// <summary>
/// 宿主环境能力（ARG/APP）。
/// ENV 在桌面参考语义中读进程环境变量（EcxHost.ReferenceSyscall），不经本接口。
/// </summary>
public interface IHostEnvironment
{
    /// <summary>ARG(i) 参数表（越界返回空串）。</summary>
    string[] Args { get; }

    /// <summary>__APP__ 应用目录。</summary>
    string AppDir { get; }
}