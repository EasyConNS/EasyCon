using EasyCon.Core.Config;
using SDL;

namespace EasyCon.SDLInput.Tests;

/// <summary>校验默认键盘映射的版本与小键盘（KP_PLUS/KP_MINUS）回归。</summary>
[TestFixture]
public class SdlKeyMappingDefaultsTests
{
    [Test]
    public void Create_UsesCurrentSchemaVersion()
    {
        Assert.That(SdlKeyMappingDefaults.Create().SchemaVersion, Is.EqualTo(KeyMappingConfig.CurrentSchemaVersion));
    }

    [Test]
    public void Create_MapsNumpadPlusAndMinus()
    {
        KeyMappingConfig mapping = SdlKeyMappingDefaults.Create();

        Assert.Multiple(() =>
        {
            Assert.That(mapping.Plus, Is.EqualTo((int)SDL_Scancode.SDL_SCANCODE_KP_PLUS));
            Assert.That(mapping.Minus, Is.EqualTo((int)SDL_Scancode.SDL_SCANCODE_KP_MINUS));
            Assert.That(mapping.Plus, Is.EqualTo(87));
            Assert.That(mapping.Minus, Is.EqualTo(86));
            Assert.That(mapping.Plus, Is.Not.Zero);
            Assert.That(mapping.Minus, Is.Not.Zero);
        });
    }
}