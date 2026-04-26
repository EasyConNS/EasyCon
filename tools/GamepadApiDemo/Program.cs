using GamepadApi;

var manager = new GamepadManager();
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

manager.DeviceConnected += device =>
{
    Console.WriteLine($"\n  >> 手柄 {device.Index} 已连接");
};

manager.DeviceDisconnected += index =>
{
    Console.WriteLine($"\n  >> 手柄 {index} 已断开");
};

manager.StateChanged += (device, state) =>
{
    var pressed = new List<string>();
    foreach (GamepadButtons flag in Enum.GetValues<GamepadButtons>())
    {
        if (flag == GamepadButtons.None) continue;
        if (state.Buttons.HasFlag(flag))
            pressed.Add(flag.ToString());
    }

    var ls = state.LeftStick;
    var rs = state.RightStick;

    Console.SetCursorPosition(0, 3);
    Console.WriteLine($"  手柄 {device.Index} | Packet: {state.PacketNumber}                  ");
    Console.WriteLine($"  按键: {(pressed.Count > 0 ? string.Join(", ", pressed) : "(无)")}                ");
    Console.WriteLine($"  左摇杆: ({ls.X,6:F3}, {ls.Y,6:F3})    右摇杆: ({rs.X,6:F3}, {rs.Y,6:F3})");
    Console.WriteLine($"  左扳机: {state.LeftTrigger,5:F3}              右扳机: {state.RightTrigger,5:F3}              ");
};

Console.WriteLine("=== GamepadApi Demo ===");
Console.WriteLine("连接手柄后自动显示状态，按 Ctrl+C 退出\n");

manager.Start();

try
{
    await Task.Delay(-1, cts.Token);
}
catch (OperationCanceledException)
{
    manager.Stop();
    Console.WriteLine("\n已退出。");
}