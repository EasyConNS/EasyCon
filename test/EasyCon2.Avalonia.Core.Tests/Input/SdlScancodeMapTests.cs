using Avalonia.Input;
using EasyCon2.Avalonia.Core.Input;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// SdlScancodeMap 的不变量测试：Avalonia Key → SDL 扫描码映射，以及显示名称转换。
/// </summary>
[TestFixture]
public class SdlScancodeMapTests
{
    [Test]
    public void FromAvaloniaKey_MapsRepresentativeKeys()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.A), Is.EqualTo(4));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.Z), Is.EqualTo(29));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.D1), Is.EqualTo(30));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.D0), Is.EqualTo(39));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.Return), Is.EqualTo(40));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.Escape), Is.EqualTo(41));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.Space), Is.EqualTo(44));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.F1), Is.EqualTo(58));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.F12), Is.EqualTo(69));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.NumPad0), Is.EqualTo(98));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.Decimal), Is.EqualTo(99));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.LeftCtrl), Is.EqualTo(224));
        });
    }

    [Test]
    public void FromAvaloniaKey_MapsExtendedFunctionKeys()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.F13), Is.EqualTo(104));
            Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.F24), Is.EqualTo(115));
        });
    }

    [Test]
    public void FromAvaloniaKey_UnsupportedKeyReturnsMinusOne()
    {
        Assert.That(SdlScancodeMap.FromAvaloniaKey(Key.None), Is.EqualTo(-1));
    }

    [Test]
    public void ToDisplayName_ZeroIsPlaceholder()
    {
        Assert.That(SdlScancodeMap.ToDisplayName(0), Is.EqualTo("—"));
    }

    [Test]
    public void ToDisplayName_MapsRepresentativeScancodes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SdlScancodeMap.ToDisplayName(4), Is.EqualTo("A"));
            Assert.That(SdlScancodeMap.ToDisplayName(39), Is.EqualTo("0"));
            Assert.That(SdlScancodeMap.ToDisplayName(69), Is.EqualTo("F12"));
            Assert.That(SdlScancodeMap.ToDisplayName(104), Is.EqualTo("F13"));
            Assert.That(SdlScancodeMap.ToDisplayName(115), Is.EqualTo("F24"));
        });
    }
}