#nullable enable
using EasyDevice;
using EasyScript;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Core.Maa;

internal sealed record ControllerReport(ushort Buttons = 0, byte Hat = 8,
    byte Lx = 128, byte Ly = 128, byte Rx = 128, byte Ry = 128)
{
    public SwitchReport ToReport() => new() { Button = Buttons, HAT = Hat, LX = Lx, LY = Ly, RX = Rx, RY = Ry };
    public static ControllerReport FromReport(SwitchReport report)
        => new(report.Button, report.HAT, report.LX, report.LY, report.RX, report.RY);
}

/// <summary>同时保存编号和 Down 后的参数，避免仅传编号丢失组合键/摇杆坐标。</summary>
internal sealed record ControllerKey(int Code, ControllerReport State)
{
    public static ControllerKey FromKey(ECKey key)
    {
        SwitchReport report = new();
        key.Down(report);
        return new(key.KeyCode, ControllerReport.FromReport(report));
    }

    public ECKey ToKey() => Code switch
    {
        >= 0 and <= 15 when State.Buttons != 0 => ECKeyUtil.Button((SwitchButton)State.Buttons),
        >= 16 and <= 24 when State.Hat <= 8 => ECKeyUtil.HAT((SwitchHAT)State.Hat),
        32 => ECKeyUtil.LStick(State.Lx, State.Ly),
        33 => ECKeyUtil.RStick(State.Rx, State.Ry),
        _ => throw new ControllerOperationException("InvalidKey", $"不支持的按键编号 {Code}"),
    };
}

internal sealed record ControllerCommand
{
    public string Protocol { get; init; } = "easycon.controller/1";
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");
    public string? RunId { get; init; }
    public long Epoch { get; init; }
    public required string Op { get; init; }
    public ControllerKey? Key { get; init; }
    public ControllerReport? Report { get; init; }
    public int Duration { get; init; }
    public DelayType Timing { get; init; }
    public DirectionKey Direction { get; init; }
    public bool Down { get; init; }
    public byte Index { get; init; }
    public byte[]? Data { get; init; }
    public string? Port { get; init; }
}

internal sealed record ControllerReply(string RequestId, bool Accepted, string? ErrorCode = null,
    InputCompletion Completion = InputCompletion.Accepted, string? Value = null);

internal static class ControllerJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };
}