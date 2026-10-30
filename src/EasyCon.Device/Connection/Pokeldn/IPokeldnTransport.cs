namespace EasyDevice.Connection.Pokeldn;

/// <summary>每个连接只由一个工作线程访问；取消和超时必须使 I/O 有界退出。</summary>
internal interface IPokeldnTransport : IDisposable
{
    int ConnectTimeoutMilliseconds { get; }
    PokeldnStatus Connect(CancellationToken token);
    PokeldnStatus GetStatus(CancellationToken token);
    void SendReport(byte[] report, CancellationToken token);
    void Stop(CancellationToken token);
    void Disconnect(CancellationToken token);
}