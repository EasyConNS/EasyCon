using EasyCon.Core.Config;
using EasyCon.Core.Input;
using EasyDevice;
using SDL;

namespace EasyCon.SDLInput;

public sealed class SdlKeyboardInputBinder : IInputBinder, IDisposable
{
    private readonly SdlEventLoop _eventLoop;
    private readonly NintendoSwitch _switch;
    private bool _enabled;
    private bool _started;
    private Func<bool>? _escapeKeydown;
    private Func<bool>? _escapeKeyup;

    private readonly Dictionary<int, Action<NintendoSwitch, bool>> _keyMap = [];

    public SdlKeyboardInputBinder(SdlEventLoop eventLoop, NintendoSwitch @switch)
    {
        _eventLoop = eventLoop;
        _switch = @switch;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _enabled = true;
        _eventLoop.KeyEvent += OnKeyEvent;
    }

    public void Stop()
    {
        _started = false;
        _enabled = false;
        _eventLoop.KeyEvent -= OnKeyEvent;
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
    }

    public void UpdateKeyMapping(KeyMappingConfig mapping)
    {
        _keyMap.Clear();

        void AddButton(int scancode, SwitchButton button)
        {
            if (scancode == 0) return;
            var key = ECKeyUtil.Button(button);
            _keyMap[scancode] = (sw, down) =>
            {
                if (down) sw.Down(key); else sw.Up(key);
            };
        }

        void AddHat(int scancode, SwitchHAT hat)
        {
            if (scancode == 0) return;
            var key = ECKeyUtil.HAT(hat);
            _keyMap[scancode] = (sw, down) =>
            {
                if (down) sw.Down(key); else sw.Up(key);
            };
        }

        void AddDirection(int scancode, DirectionKey dkey, bool isLeft)
        {
            if (scancode == 0) return;
            _keyMap[scancode] = (sw, down) =>
            {
                if (isLeft)
                {
                    sw.LeftDirection(dkey, down);
                }
                else
                {
                    sw.RightDirection(dkey, down);
                }
            };
        }

        void AddHatDirection(int scancode, DirectionKey dkey)
        {
            if (scancode == 0) return;
            _keyMap[scancode] = (sw, down) =>
            {
                sw.HatDirection(dkey, down);
            };
        }

        AddButton(mapping.A, SwitchButton.A);
        AddButton(mapping.B, SwitchButton.B);
        AddButton(mapping.X, SwitchButton.X);
        AddButton(mapping.Y, SwitchButton.Y);
        AddButton(mapping.L, SwitchButton.L);
        AddButton(mapping.R, SwitchButton.R);
        AddButton(mapping.ZL, SwitchButton.ZL);
        AddButton(mapping.ZR, SwitchButton.ZR);
        AddButton(mapping.Plus, SwitchButton.PLUS);
        AddButton(mapping.Minus, SwitchButton.MINUS);
        AddButton(mapping.Capture, SwitchButton.CAPTURE);
        AddButton(mapping.Home, SwitchButton.HOME);
        AddButton(mapping.LClick, SwitchButton.LCLICK);
        AddButton(mapping.RClick, SwitchButton.RCLICK);

        AddHat(mapping.UpRight, SwitchHAT.TOP_RIGHT);
        AddHat(mapping.DownRight, SwitchHAT.BOTTOM_RIGHT);
        AddHat(mapping.UpLeft, SwitchHAT.TOP_LEFT);
        AddHat(mapping.DownLeft, SwitchHAT.BOTTOM_LEFT);

        AddHatDirection(mapping.Up, DirectionKey.Up);
        AddHatDirection(mapping.Down, DirectionKey.Down);
        AddHatDirection(mapping.Left, DirectionKey.Left);
        AddHatDirection(mapping.Right, DirectionKey.Right);

        AddDirection(mapping.LSUp, DirectionKey.Up, true);
        AddDirection(mapping.LSDown, DirectionKey.Down, true);
        AddDirection(mapping.LSLeft, DirectionKey.Left, true);
        AddDirection(mapping.LSRight, DirectionKey.Right, true);
        AddDirection(mapping.RSUp, DirectionKey.Up, false);
        AddDirection(mapping.RSDown, DirectionKey.Down, false);
        AddDirection(mapping.RSLeft, DirectionKey.Left, false);
        AddDirection(mapping.RSRight, DirectionKey.Right, false);
    }

    public void RegisterEscapeKey(Func<bool> keydown, Func<bool> keyup)
    {
        _escapeKeydown = keydown;
        _escapeKeyup = keyup;
    }

    public void HandleKeyEvent(int scancode, bool down)
    {
        if (!_started || !_enabled) return;

        if (scancode == (int)SDL_Scancode.SDL_SCANCODE_ESCAPE && _escapeKeydown != null)
        {
            if (down)
                _escapeKeydown();
            else
                _escapeKeyup?.Invoke();
            return;
        }

        if (_keyMap.TryGetValue(scancode, out var action))
        {
            action(_switch, down);
        }
    }

    private void OnKeyEvent(SDL_KeyboardEvent ev)
    {
        HandleKeyEvent((int)ev.scancode, (bool)ev.down);
    }

    public void Dispose()
    {
        Stop();
    }
}