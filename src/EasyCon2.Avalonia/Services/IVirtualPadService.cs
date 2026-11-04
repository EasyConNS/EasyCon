using Avalonia.Controls;
using EasyCon.Core.Config;
using EasyCon.Core.Input;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// 虚拟手柄覆盖层服务抽象（实现在 <see cref="EasyCon2.Avalonia.VPad.VPadService"/>），
/// 让上层（Services/Shell）只依赖端口而不依赖 VPad 实现类型。
/// </summary>
public interface IVirtualPadService
{
    bool IsActive { get; }
    event Action? Exited;
    event Action<int, bool>? OverlayKeyEvent;
    void SetOwner(Window owner);
    void Show();
    void Exit();
    void Deactivate();
    void SwitchInput(IInputBinder binder);
    void UpdateKeyMapping(KeyMappingConfig mapping);
    void RegisterEscapeKey(Func<bool> keydown, Func<bool> keyup);
}