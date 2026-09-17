namespace EasyCon.Core.Capabilities;

/// <summary>
/// 文件族能力（EcsSyscall 1-9 的宿主语义）。
/// 标准句柄 0=stdin / 1=stdout / 2=stderr 的行断协议由 VM 参考语义（ReferenceSyscall）承担，
/// 本接口只面向 >= 3 的文件句柄与整文件便捷 API。失败语义与桌面参考实现一致：返回约定哨兵值，不抛异常。
/// </summary>
public interface IFileSystem
{
    /// <summary>FOpen：mode "r"/"w"/"a"；失败返回 -1。</summary>
    long Open(string path, string mode);

    /// <summary>FRead 文件读：count &lt;= 0 读至末尾；失败/末尾返回空串。</summary>
    string Read(long handle, int count);

    /// <summary>FWrite 文件写：返回写入长度（-1 失败）。</summary>
    int Write(long handle, string data);

    /// <summary>FClose：句柄不存在时静默。</summary>
    void Close(long handle);

    /// <summary>FEof：句柄不存在返回 true。</summary>
    bool Eof(long handle);

    /// <summary>READFILE；失败返回空串。</summary>
    string ReadAllText(string path);

    /// <summary>WRITEFILE。</summary>
    void WriteAllText(string path, string content);

    /// <summary>APPENDFILE。</summary>
    void AppendAllText(string path, string content);

    /// <summary>FILE_EXISTS。</summary>
    bool Exists(string path);
}