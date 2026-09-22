using EasyDevice;
using SDL;

namespace EasyCon.SDLInput.Tests;

/// <summary>
/// 键盘 → HID 链路端到端测试：以 FakeConnection 替代串口，驱动 SdlKeyboardInputBinder，
/// 断言设备出队并写出的 HID 报文，全程不触碰单片机。
/// </summary>
[TestFixture]
public class KeyboardMappingHidTests
{
    private FakeConnection _connection = null!;
    private TestSwitch _switch = null!;
    private SdlEventLoop _loop = null!;
    private SdlKeyboardInputBinder _binder = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new FakeConnection("fake://test");
        _switch = new TestSwitch(_connection);
        _loop = new SdlEventLoop();
        _binder = new SdlKeyboardInputBinder(_loop, _switch);
        _binder.UpdateKeyMapping(SdlKeyMappingDefaults.Create());
        _binder.Start();
        Assert.That(_switch.TryConnect("fake://test"), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
    }

    [TearDown]
    public void TearDown()
    {
        _binder.Dispose();
        _loop.Dispose();
        _switch.Disconnect();
    }

    [Test]
    public void DefaultButtonMappings_PressAndRelease_ToggleExpectedButtonBit()
    {
        (int Scancode, SwitchButton Button)[] mappings =
        [
            ((int)SDL_Scancode.SDL_SCANCODE_L, SwitchButton.A),
            ((int)SDL_Scancode.SDL_SCANCODE_K, SwitchButton.B),
            ((int)SDL_Scancode.SDL_SCANCODE_I, SwitchButton.X),
            ((int)SDL_Scancode.SDL_SCANCODE_J, SwitchButton.Y),
            ((int)SDL_Scancode.SDL_SCANCODE_G, SwitchButton.L),
            ((int)SDL_Scancode.SDL_SCANCODE_T, SwitchButton.R),
            ((int)SDL_Scancode.SDL_SCANCODE_F, SwitchButton.ZL),
            ((int)SDL_Scancode.SDL_SCANCODE_R, SwitchButton.ZR),
            ((int)SDL_Scancode.SDL_SCANCODE_Z, SwitchButton.CAPTURE),
            ((int)SDL_Scancode.SDL_SCANCODE_C, SwitchButton.HOME),
            ((int)SDL_Scancode.SDL_SCANCODE_Q, SwitchButton.LCLICK),
            ((int)SDL_Scancode.SDL_SCANCODE_E, SwitchButton.RCLICK),
        ];

        foreach ((int scancode, SwitchButton button) in mappings)
        {
            AssertButtonMapping(scancode, button);
        }
    }

    [Test]
    public void NumpadPlusAndMinus_MapToPlusMinusButtons()
    {
        AssertButtonMapping((int)SDL_Scancode.SDL_SCANCODE_KP_PLUS, SwitchButton.PLUS);
        AssertButtonMapping((int)SDL_Scancode.SDL_SCANCODE_KP_MINUS, SwitchButton.MINUS);
    }

    [Test]
    public void LeftStickDirections_MapToStickCoordinates()
    {
        SwitchReport up = Press((int)SDL_Scancode.SDL_SCANCODE_W);
        Assert.Multiple(() =>
        {
            Assert.That(up.LX, Is.EqualTo(128));
            Assert.That(up.LY, Is.EqualTo(0));
        });
        Release((int)SDL_Scancode.SDL_SCANCODE_W);

        SwitchReport down = Press((int)SDL_Scancode.SDL_SCANCODE_S);
        Assert.Multiple(() =>
        {
            Assert.That(down.LX, Is.EqualTo(128));
            Assert.That(down.LY, Is.EqualTo(255));
        });
        Release((int)SDL_Scancode.SDL_SCANCODE_S);

        SwitchReport left = Press((int)SDL_Scancode.SDL_SCANCODE_A);
        Assert.That(left.LX, Is.EqualTo(0));
        Release((int)SDL_Scancode.SDL_SCANCODE_A);

        SwitchReport right = Press((int)SDL_Scancode.SDL_SCANCODE_D);
        Assert.That(right.LX, Is.EqualTo(255));
        Release((int)SDL_Scancode.SDL_SCANCODE_D);
    }

    [Test]
    public void RightStickDirections_MapToStickCoordinates()
    {
        SwitchReport up = Press((int)SDL_Scancode.SDL_SCANCODE_UP);
        Assert.Multiple(() =>
        {
            Assert.That(up.RX, Is.EqualTo(128));
            Assert.That(up.RY, Is.EqualTo(0));
        });
        Release((int)SDL_Scancode.SDL_SCANCODE_UP);

        SwitchReport down = Press((int)SDL_Scancode.SDL_SCANCODE_DOWN);
        Assert.Multiple(() =>
        {
            Assert.That(down.RX, Is.EqualTo(128));
            Assert.That(down.RY, Is.EqualTo(255));
        });
        Release((int)SDL_Scancode.SDL_SCANCODE_DOWN);

        SwitchReport left = Press((int)SDL_Scancode.SDL_SCANCODE_LEFT);
        Assert.That(left.RX, Is.EqualTo(0));
        Release((int)SDL_Scancode.SDL_SCANCODE_LEFT);

        SwitchReport right = Press((int)SDL_Scancode.SDL_SCANCODE_RIGHT);
        Assert.That(right.RX, Is.EqualTo(255));
        Release((int)SDL_Scancode.SDL_SCANCODE_RIGHT);
    }

    [Test]
    public void DequeuedPacket_MatchesReportBytes()
    {
        _binder.HandleKeyEvent((int)SDL_Scancode.SDL_SCANCODE_L, true);
        Assert.That(_connection.TryTake(out byte[] _, 2000), Is.True);

        _binder.HandleKeyEvent((int)SDL_Scancode.SDL_SCANCODE_W, true);
        Assert.That(_connection.TryTake(out byte[] packet, 2000), Is.True);
        byte[] expected = ((IReporter)_switch).GetReport().GetBytes();

        Assert.That(packet, Is.EqualTo(expected));
    }

    [Test]
    public void GoldenBytes_PressingLKeyWritesExactHidPacket()
    {
        _binder.HandleKeyEvent((int)SDL_Scancode.SDL_SCANCODE_L, true);
        Assert.That(_connection.TryTake(out byte[] packet, 2000), Is.True);
        byte[] expected = [0x00, 0x01, 0x01, 0x08, 0x04, 0x02, 0x01, 0x80];

        Assert.That(packet, Is.EqualTo(expected));
    }

    [Test]
    public void UnboundScancode_ProducesNoWrite()
    {
        _binder.HandleKeyEvent((int)SDL_Scancode.SDL_SCANCODE_SPACE, true);

        Assert.That(_connection.TryTake(out byte[] _, 200), Is.False);
    }

    [Test]
    public void DisabledBinder_ProducesNoWrite()
    {
        _binder.SetEnabled(false);
        _binder.HandleKeyEvent((int)SDL_Scancode.SDL_SCANCODE_L, true);

        Assert.That(_connection.TryTake(out byte[] _, 200), Is.False);
    }

    [Test]
    public void EscapeKey_InvokesCallbackWithoutHidWrite()
    {
        bool invoked = false;
        _binder.RegisterEscapeKey(
            () =>
            {
                invoked = true;
                return true;
            },
            () => true);

        _binder.HandleKeyEvent((int)SDL_Scancode.SDL_SCANCODE_ESCAPE, true);

        Assert.Multiple(() =>
        {
            Assert.That(invoked, Is.True);
            Assert.That(_connection.TryTake(out byte[] _, 200), Is.False);
        });
    }

    private void AssertButtonMapping(int scancode, SwitchButton button)
    {
        SwitchReport pressed = Press(scancode);
        Assert.That((pressed.Button & (ushort)button), Is.Not.Zero, $"{button} 按下时置位");

        SwitchReport released = Release(scancode);
        Assert.That((released.Button & (ushort)button), Is.Zero, $"{button} 松开时清位");
    }

    private SwitchReport Press(int scancode)
    {
        _binder.HandleKeyEvent(scancode, true);
        Assert.That(_connection.TryTake(out byte[] _, 2000), Is.True, $"按下 scancode {scancode} 后应写出 HID 报文");
        return ((IReporter)_switch).GetReport();
    }

    private SwitchReport Release(int scancode)
    {
        _binder.HandleKeyEvent(scancode, false);
        Assert.That(_connection.TryTake(out byte[] _, 2000), Is.True, $"松开 scancode {scancode} 后应写出 HID 报文");
        return ((IReporter)_switch).GetReport();
    }
}