using Avalonia.Input;
using EasyCon2.Avalonia.Core.Input;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// SdlScancodeMap 的按键策略层不变量测试：清除键 / 系统保留键判定，
/// 并记录底层扫描码映射本身不因策略而改变。
/// </summary>
[TestFixture]
public class SdlScancodeMapPolicyTests
{
    [Test]
    public void IsClearKey_TrueForEscapeAndBack()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SdlScancodeMap.IsClearKey(Key.Escape), Is.True);
            Assert.That(SdlScancodeMap.IsClearKey(Key.Back), Is.True);
        });
    }

    [Test]
    public void IsClearKey_FalseForRegularKeys()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SdlScancodeMap.IsClearKey(Key.A), Is.False);
            Assert.That(SdlScancodeMap.IsClearKey(Key.Space), Is.False);
        });
    }

    [Test]
    public void IsReservedKey_TrueForSystemKeys()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SdlScancodeMap.IsReservedKey(Key.LWin), Is.True);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.RWin), Is.True);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.Apps), Is.True);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.PrintScreen), Is.True);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.Pause), Is.True);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.CapsLock), Is.True);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.NumLock), Is.True);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.Scroll), Is.True);
        });
    }

    [Test]
    public void IsReservedKey_FalseForMappableKeys()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SdlScancodeMap.IsReservedKey(Key.A), Is.False);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.Space), Is.False);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.LeftShift), Is.False);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.LeftCtrl), Is.False);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.Tab), Is.False);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.Return), Is.False);
            Assert.That(SdlScancodeMap.IsReservedKey(Key.F1), Is.False);
        });
    }

    [Test]
    public void FromAvaloniaKey_UnchangedByPolicyLayer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.Escape), Is.EqualTo(41));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.Back), Is.EqualTo(42));
        });
    }
}