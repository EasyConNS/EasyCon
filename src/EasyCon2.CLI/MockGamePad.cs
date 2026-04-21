using EasyScript;

class MockGamePad : ICGamePad
{
    public DelayType DelayMethod => DelayType.Normal;

    public void ClickButtons(GamePadKey key, int duration, CancellationToken token)
    {
        Console.WriteLine($"[MOCK] Button {key} CLICK {duration}ms");
        Thread.Sleep(duration);
    }

    public void PressButtons(GamePadKey key)
    {
        Console.WriteLine($"[MOCK] Button {key} DOWN");
    }

    public void ReleaseButtons(GamePadKey key)
    {
        Console.WriteLine($"[MOCK] Button {key} UP");
    }

    public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token)
    {
        Console.WriteLine($"[MOCK] Stick {key} ({x},{y}) CLICK {duration}ms");
        Thread.Sleep(duration);
    }

    public void SetStick(GamePadKey key, byte x, byte y)
    {
        if (x == 128 && y == 128)
            Console.WriteLine($"[MOCK] Stick {key} RESET");
        else
            Console.WriteLine($"[MOCK] Stick {key} ({x},{y})");
    }

    public void ChangeAmiibo(uint index)
    {
        Console.WriteLine($"[MOCK] Amiibo -> {index}");
    }
}