using EasyCon.WinInput;
using EasyDevice;
using GamepadApi;
using System.Numerics;

namespace EasyCon.WinInput.Tests;

public class GamepadMapperTests
{
    static readonly GamepadMappingConfig DefaultConfig = new();

    [Test]
    public void Map_NoButtonsPressed_AllButtonsZero()
    {
        var state = new GamepadState();
        var report = GamepadMapper.Map(state, DefaultConfig);

        Assert.Multiple(() =>
        {
            Assert.That(report.Button, Is.EqualTo((ushort)0));
            Assert.That(report.HAT, Is.EqualTo((byte)SwitchHAT.CENTER));
            Assert.That(report.LX, Is.EqualTo(128));
            Assert.That(report.LY, Is.EqualTo(128));
            Assert.That(report.RX, Is.EqualTo(128));
            Assert.That(report.RY, Is.EqualTo(128));
        });
    }

    [Test]
    public void Map_SingleButtonA_IsSwitchXY_MapsToSwitchB()
    {
        var state = new GamepadState { Buttons = GamepadButtons.A };
        var config = new GamepadMappingConfig { IsSwitchXY = true };
        var report = GamepadMapper.Map(state, config);

        Assert.That(report.Button & (ushort)SwitchButton.B, Is.Not.Zero);
        Assert.That(report.Button & (ushort)SwitchButton.A, Is.Zero);
    }

    [Test]
    public void Map_SingleButtonA_NotSwitchXY_MapsToSwitchA()
    {
        var state = new GamepadState { Buttons = GamepadButtons.A };
        var config = new GamepadMappingConfig { IsSwitchXY = false };
        var report = GamepadMapper.Map(state, config);

        Assert.That(report.Button & (ushort)SwitchButton.A, Is.Not.Zero);
        Assert.That(report.Button & (ushort)SwitchButton.B, Is.Zero);
    }

    [Test]
    public void Map_TriggerBelowThreshold_NotPressed()
    {
        var state = new GamepadState { LeftTrigger = 0.05f, RightTrigger = 0.05f };
        var config = new GamepadMappingConfig { TriggerThreshold = 0.1f };
        var report = GamepadMapper.Map(state, config);

        Assert.That(report.Button & (ushort)SwitchButton.ZL, Is.Zero);
        Assert.That(report.Button & (ushort)SwitchButton.ZR, Is.Zero);
    }

    [Test]
    public void Map_TriggerAboveThreshold_Pressed()
    {
        var state = new GamepadState { LeftTrigger = 0.5f, RightTrigger = 0.5f };
        var config = new GamepadMappingConfig { TriggerThreshold = 0.1f };
        var report = GamepadMapper.Map(state, config);

        Assert.That(report.Button & (ushort)SwitchButton.ZL, Is.Not.Zero);
        Assert.That(report.Button & (ushort)SwitchButton.ZR, Is.Not.Zero);
    }

    [Test]
    public void Map_StickCenter_OutputCenter()
    {
        var state = new GamepadState
        {
            LeftStick = new Vector2(0f, 0f),
            RightStick = new Vector2(0f, 0f),
        };
        var report = GamepadMapper.Map(state, DefaultConfig);

        Assert.That(report.LX, Is.EqualTo(128));
        Assert.That(report.LY, Is.EqualTo(128));
        Assert.That(report.RX, Is.EqualTo(128));
        Assert.That(report.RY, Is.EqualTo(128));
    }

    [Test]
    public void Map_StickFullRight_OutputMax()
    {
        var state = new GamepadState { LeftStick = new Vector2(1f, 0f) };
        var report = GamepadMapper.Map(state, DefaultConfig);

        Assert.That(report.LX, Is.EqualTo(255));
    }

    [Test]
    public void Map_StickFullLeft_OutputMin()
    {
        var state = new GamepadState { LeftStick = new Vector2(-1f, 0f) };
        var report = GamepadMapper.Map(state, DefaultConfig);

        Assert.That(report.LX, Is.EqualTo(1));
    }

    [Test]
    public void Map_RightStick_WritesToRY_NotLY()
    {
        var state = new GamepadState
        {
            LeftStick = new Vector2(0f, 0.5f),
            RightStick = new Vector2(0.8f, -0.3f),
        };
        var report = GamepadMapper.Map(state, DefaultConfig);

        // Left stick Y should reflect left input only
        var expectedLY = (byte)Math.Clamp((int)(-0.5f * 127) + 128, 0, 255);
        Assert.That(report.LY, Is.EqualTo(expectedLY));

        // Right stick values should be independent
        var expectedRX = (byte)Math.Clamp((int)(0.8f * 127) + 128, 0, 255);
        var expectedRY = (byte)Math.Clamp((int)(0.3f * 127) + 128, 0, 255);
        Assert.That(report.RX, Is.EqualTo(expectedRX));
        Assert.That(report.RY, Is.EqualTo(expectedRY));
    }

    [Test]
    public void Map_DPadUp_MapsToTop()
    {
        var state = new GamepadState { Buttons = GamepadButtons.DPadUp };
        var report = GamepadMapper.Map(state, DefaultConfig);

        Assert.That(report.HAT, Is.EqualTo((byte)SwitchHAT.TOP));
    }

    [Test]
    public void Map_MultipleButtons_AllMapped()
    {
        var state = new GamepadState
        {
            Buttons = GamepadButtons.A | GamepadButtons.LeftShoulder | GamepadButtons.Start,
        };
        var config = new GamepadMappingConfig { IsSwitchXY = true };
        var report = GamepadMapper.Map(state, config);

        Assert.That(report.Button & (ushort)SwitchButton.B, Is.Not.Zero, "A→B (SwitchXY)");
        Assert.That(report.Button & (ushort)SwitchButton.L, Is.Not.Zero, "LeftShoulder→L");
        Assert.That(report.Button & (ushort)SwitchButton.PLUS, Is.Not.Zero, "Start→PLUS");
    }
}