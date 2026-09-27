namespace EasyDevice;

public partial class NintendoSwitch
{
    const int MINIMAL_INTERVAL = 30;

    readonly SwitchReport _report = new();

    DateTime _nextSendTime = DateTime.MinValue;
    private readonly EventWaitHandle _ewh = new(false, EventResetMode.ManualReset);

    public void ApplyReport(SwitchReport report)
    {
        lock (this)
        {
            _keystrokes.Clear();
            _report.Button = report.Button;
            _report.HAT = report.HAT;
            _report.LX = report.LX;
            _report.LY = report.LY;
            _report.RX = report.RX;
            _report.RY = report.RY;
            Signal();
        }
    }

    void Signal()
    {
        if (this.IsConnected())
            _ewh.Set();
    }

    void Loop(CancellationToken token)
    {
        // 写循环没有任何上层兜底：一次未观察异常会让循环静默死亡，
        // 表现为"按键永远发不出去"且用户无感知。捕获、上报并退出。
        try
        {
            RunLoop(token);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"设备发送循环异常退出: {ex.Message}");
            StatusChanged?.Invoke(Status.Error);
        }
    }

    private void RunLoop(CancellationToken token)
    {
        int sleep = 0;
        while (!token.IsCancellationRequested)
        {
            if (_keystrokes.Count == 0)
            {
                // 可取消等待：重连/断开后旧循环不再阻塞在无限 WaitOne 上，
                // 且 token 先于任何共享状态写入被检查（避免新旧 Loop 并发写报告）
                if (WaitHandle.WaitAny(new WaitHandle[] { _ewh, token.WaitHandle }) == WaitHandle.WaitTimeout)
                    continue;
            }
            else
                _ewh.WaitOne(sleep);
            if (token.IsCancellationRequested)
                return;
            if (DateTime.Now < _nextSendTime)
                Thread.Sleep((int)(_nextSendTime - DateTime.Now).TotalMilliseconds);
            sleep = int.MaxValue;
            lock (this)
            {
                if (_reset)
                {
                    _report.Reset();
                    _reset = false;
                }
                foreach (var ks in _keystrokes.Values.ToArray())
                {
                    if (ks.Time > DateTime.Now)
                    {
                        var n = (int)(ks.Time - DateTime.Now).TotalMilliseconds;
                        if (n > 0 && n < sleep)
                            sleep = n;
                    }
                    else if (ks.Up)
                    {
                        ks.Key.Up(_report);
                        _keystrokes.Remove(ks.KeyCode);
                    }
                    else
                    {
                        ks.Key.Down(_report);
                        if (ks.Duration > 0)
                        {
                            _keystrokes[ks.KeyCode] = new KeyStroke(ks.Key, true, 0, DateTime.Now + TimeSpan.FromMilliseconds(ks.Duration));
                            if (ks.Duration < sleep)
                                sleep = ks.Duration;
                        }
                        else
                            _keystrokes.Remove(ks.KeyCode);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[Send {DateTime.Now:ss.fff}] {_report}");

                WriteReport(_report.GetBytes());
                _nextSendTime = DateTime.Now.AddMilliseconds(MINIMAL_INTERVAL);
                _ewh.Reset();
            }
        }
    }
}