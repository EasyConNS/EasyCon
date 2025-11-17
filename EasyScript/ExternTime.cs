namespace EasyScript;

public class ExternTime
{
    private readonly long _TIME;

    public ExternTime(DateTime t)
    {
        _TIME = t.Ticks;
    }

    public int CurrTimestamp => (int)GetTimestamp();

    private long GetTimestamp() => (DateTime.Now.Ticks - _TIME) / 10_000;
}
